using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 게임의 컴파일된 코드를 읽어 behaviour 가 무엇을 듣고 무엇을 바꾸는지 기록한다.
    /// </summary>
    /// <remarks>
    /// Unity 컴파일 파이프라인 안에서 돌므로 게임 팀은 패키지 설치 외에 할 일이 없다. IL2CPP 변환 전에 돌기 때문에
    /// 빌드 형식과 무관하게 결과가 남는다.
    ///
    /// 끄는 define 없이 세 가지로 에디터를 보호한다: 모든 루프가 유계이고, 어셈블리마다 시간 예산이 있으며,
    /// 어떤 예외든 <see cref="Process"/> 에서 잡혀 컴파일러의 원본 어셈블리를 돌려준다.
    ///
    /// 게임 어셈블리에는 attribute 하나와 압축된 리소스 둘만 쓴다. 메서드 본문과 이름은 건드리지 않는다.
    /// </remarks>
    public sealed class AffordanceILPostProcessor : ILPostProcessor
    {

        /// <summary>
        /// 어셈블리 하나의 분석 시간 한도. 넘으면 그때까지 닿은 만큼만 보고한다.
        /// </summary>
        /// <remarks>
        /// 루프는 이미 유계다. 이 한도는 유한하지만 느린 어셈블리 때문에 컴파일이 몇 분씩 멈추는 것을 막는다.
        /// </remarks>
        private const long BudgetMilliseconds = 10000;

        /// <summary>
        /// 엔진, 툴체인, 이 벤더에 속하는 어셈블리 이름.
        /// </summary>
        /// <remarks>
        /// 포함 목록이 아니라 제외 목록이다. 이 패키지 참조를 요구하면 auto-reference 가 닿지 않는 assembly definition
        /// 어셈블리를 놓친다.
        ///
        /// <c>UnityPlayMcp</c> 전체를 제외해 함께 설치된 형제 SDK 도 건너뛴다. 대가로 어셈블리 이름이 이 접두어로
        /// 시작하는 게임은 <see cref="WillProcess"/> 에서 아무 보고 없이 건너뛰어진다.
        /// </remarks>
        private static readonly string[] SkippedPrefixes =
        {
            "UnityPlayMcp", "Unity.UnityPlayMcp",
            "UnityEngine", "UnityEditor", "Unity", "System", "mscorlib", "netstandard",
            "nunit", "Newtonsoft", "Mono"
        };

        public override ILPostProcessor GetInstance() => this;

        /// <remarks>
        /// 빌드 종류는 여기서 묻지 않는다. 여기서 false 를 돌려주면 <see cref="Process"/> 가 불리지 않아 거절을 진단으로
        /// 보고할 수 없다.
        /// </remarks>
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return !IsSkipped(compiledAssembly.Name);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();

            InMemoryAssembly baked = null;

            try
            {
                baked = IsDiscoveryBuild(compiledAssembly)
                    ? Survey(compiledAssembly, diagnostics)
                    : Declined(compiledAssembly, diagnostics);
            }
            catch (Exception exception)
            {
                // 빌드를 무너뜨리지 않는다. 결과를 잃는 편이 게임 팀의 빌드를 망가뜨리는 것보다 낫다.
                Report(diagnostics, compiledAssembly.Name, "skipped, " + exception.Message);
                baked = null;
            }

            // null 은 컴파일러 출력을 그대로 쓴다는 뜻이다. 실패, 거절, 결과 없음이 모두 null 로 끝난다.
            return new ILPostProcessResult(baked, diagnostics);
        }

        /// <summary>
        /// 범위 안에 무엇이 있는지 세고 보고한다.
        /// </summary>
        /// <remarks>
        /// 결과가 없을 때도 개수를 보고한다. 말없이 null 을 돌려주면 커버리지 공백이 없는 것으로 잘못 읽힌다.
        /// </remarks>
        private static InMemoryAssembly Survey(
            ICompiledAssembly compiledAssembly,
            List<DiagnosticMessage> diagnostics)
        {
            // 프로퍼티가 가리키는 필드는 어셈블리마다 다르므로 이전 어셈블리의 캐시를 비운다.
            SimpleSetter.Forget();

            var carriedSymbols = HasSymbols(compiledAssembly);
            var readSymbols = carriedSymbols;

            using (var resolver = new CompiledAssemblyResolver(compiledAssembly))
            using (var assembly = ReadAssembly(compiledAssembly, resolver, ref readSymbols))
            {
                var flow = new FlowTally();
                var roots = new List<MethodDefinition>();
                var wirable = new HashSet<MethodDefinition>();
                var behaviours = 0;
                var unresolved = 0;
                var inspectorCallable = 0;
                var engineMessages = 0;
                var oversized = 0;
                var ignored = 0;

                // GetTypes 는 중첩 타입까지 돌려준다. coroutine 과 람다 본문이 거기 있다.
                foreach (var type in assembly.MainModule.GetTypes())
                {
                    var verdict = AnalysisScope.Inspect(type);

                    if (verdict == TypeVerdict.Unresolved)
                    {
                        unresolved++;
                        continue;
                    }

                    if (verdict != TypeVerdict.Behaviour)
                    {
                        continue;
                    }

                    behaviours++;

                    foreach (var method in type.Methods)
                    {
                        var scope = AnalysisScope.Classify(method);

                        switch (scope)
                        {
                            case MethodScope.InspectorCallable:
                                inspectorCallable++;
                                break;
                            case MethodScope.EngineMessage:
                                engineMessages++;
                                break;
                            default:
                                ignored++;
                                continue;
                        }

                        if (AnalysisScope.IsTooLarge(method))
                        {
                            oversized++;
                            continue;
                        }

                        roots.Add(method);

                        if (scope == MethodScope.InspectorCallable)
                        {
                            wirable.Add(method);
                        }
                    }
                }

                // 진입점은 시작점일 뿐이다. 입력과 조건은 대개 진입점이 부르는 private 헬퍼 안에 있다.
                var variants = new List<Variant>();
                var sites = new CallSiteConditions(assembly.MainModule);
                var reached = CallGraph.Close(roots, assembly.MainModule, out var truncated);
                var clock = Stopwatch.StartNew();

                foreach (var trace in reached)
                {
                    if (clock.ElapsedMilliseconds > BudgetMilliseconds)
                    {
                        flow.OutOfTime = true;
                        break;
                    }

                    flow.Reached++;

                    // 분기가 없는 메서드도 직접 효과와 호출 엣지를 가지므로 닿은 모든 메서드의 그래프를 만든다.
                    if (AnalysisScope.IsTooLarge(trace.Method))
                    {
                        continue;
                    }

                    var triggerKind = wirable.Contains(trace.Entry)
                        ? "unity-event"
                        : "lifecycle";

                    Graph(trace, ref flow, variants, triggerKind, sites);
                }

                flow.Milliseconds = clock.ElapsedMilliseconds;

                // gap 을 붙이기 전에 접는다. 그래야 같은 gap 을 갖게 될 두 경우가 같은 경우로 인식된다.
                flow.Folded = DuplicateVariants.Fold(variants);

                flow.Variants = variants.Count;
                flow.Roots = roots.Count;
                flow.Truncated = truncated;

                foreach (var variant in variants)
                {
                    if (truncated)
                    {
                        variant.AddGap("call-graph-limit");
                    }

                    if (flow.OutOfTime)
                    {
                        variant.AddGap("assembly-time-limit");
                    }

                    if (unresolved > 0)
                    {
                        variant.AddGap("unresolved-types-in-assembly");
                    }

                    if (oversized > 0)
                    {
                        variant.AddGap("oversized-entry-methods-skipped");
                    }
                }

                // 기반 타입을 해석하지 못한 채 behaviour 가 없다고 하면 믿을 수 없는 결과이므로 그 사실을 함께 말한다.
                var doubt = unresolved > 0
                    ? " " + unresolved + " types could not be traced to a base type — these are unaccounted for."
                    : string.Empty;

                if (behaviours == 0)
                {
                    Report(diagnostics, compiledAssembly.Name, "no MonoBehaviour, nothing to analyse." + doubt);
                    return null;
                }

                var message =
                    behaviours + " behaviours, " +
                    inspectorCallable + " inspector-callable and " + engineMessages +
                    " engine messages in scope, " + ignored + " methods ignored.";

                if (oversized > 0)
                {
                    message += " " + oversized + " over " + AnalysisScope.MaxInstructions +
                               " instructions, left alone.";
                }

                message += flow.Describe() + doubt;

                var written = AffordanceWriter.Write(
                    assembly.MainModule, compiledAssembly, resolver, variants);

                if (written.Refusal != null)
                {
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Nothing was baked: " + written.Refusal + ".");
                    return null;
                }

                if (written.Written == 0)
                {
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Nothing to bake, so the assembly is left as it was.");
                    return null;
                }

                message += " Baked " + written.Written + " onto types";
                message += written.Unattached > 0
                    ? "; " + written.Unattached + " belong to no component and were dropped."
                    : ".";
                message += " " + written.ResourceBytes + " bytes as a resource.";

                // 두 숫자를 함께 낸다. 목록 수만 내면 확인 가능한 값의 전부처럼 읽힌다.
                message += " " + written.Watched + " members to watch, " +
                           written.Unwatchable + " values with nowhere to read them.";

                if (written.Oversized > 0)
                {
                    message += " " + written.Oversized +
                               " evidence documents exceeded their serialization bound and were dropped.";
                }

                if (carriedSymbols && !readSymbols)
                {
                    // 심볼 없이 다시 쓰면 게임 팀이 스택 트레이스와 중단점을 잃으므로 원본을 둔다.
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Left as it was: the debug symbols could not be read back.");
                    return null;
                }

                var result = Rewrite(assembly, readSymbols);

                if (result == null)
                {
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Left as it was: writing the assembly produced nothing.");
                    return null;
                }

                Report(diagnostics, compiledAssembly.Name, message);
                return result;
            }
        }

        private static bool HasSymbols(ICompiledAssembly compiledAssembly)
        {
            var pdb = compiledAssembly.InMemoryAssembly.PdbData;
            return pdb != null && pdb.Length > 0;
        }

        /// <summary>
        /// 바뀐 어셈블리를 써낸다.
        /// </summary>
        /// <remarks>
        /// 새 스트림에 다 쓴 뒤에만 돌려준다. Cecil 이 중간에 실패하면 반쯤 쓴 버퍼는 버려지고 호출자가 원본을 쓴다.
        /// </remarks>
        private static InMemoryAssembly Rewrite(AssemblyDefinition assembly, bool symbols)
        {
            using (var pe = new MemoryStream())
            using (var pdb = new MemoryStream())
            {
                var parameters = new WriterParameters();

                if (symbols)
                {
                    parameters.WriteSymbols = true;
                    parameters.SymbolWriterProvider = new PortablePdbWriterProvider();
                    parameters.SymbolStream = pdb;
                }

                assembly.Write(pe, parameters);

                var image = pe.ToArray();

                if (image.Length == 0)
                {
                    return null;
                }

                return new InMemoryAssembly(image, symbols ? pdb.ToArray() : new byte[0]);
            }
        }

        private static AssemblyDefinition ReadAssembly(
            ICompiledAssembly compiledAssembly,
            IAssemblyResolver resolver,
            ref bool symbols)
        {
            if (symbols)
            {
                try
                {
                    return AssemblyDefinition.ReadAssembly(
                        new MemoryStream(compiledAssembly.InMemoryAssembly.PeData),
                        new ReaderParameters
                        {
                            AssemblyResolver = resolver,
                            ReadingMode = ReadingMode.Immediate,
                            InMemory = true,
                            ReadSymbols = true,
                            SymbolReaderProvider = new PortablePdbReaderProvider(),
                            SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData)
                        });
                }
                catch (Exception)
                {
                    // 심볼 없이 다시 읽는다. 분석에는 심볼이 필요 없고, 되쓸 수 없게 된 것은 호출자가 검사한다.
                    symbols = false;
                }
            }

            return AssemblyDefinition.ReadAssembly(
                new MemoryStream(compiledAssembly.InMemoryAssembly.PeData),
                new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Immediate,
                    InMemory = true
                });
        }

        /// <summary>제어 흐름 패스가 한 어셈블리에서 해낸 것.</summary>
        private struct FlowTally
        {
            internal int Roots;
            internal int Reached;
            internal int Methods;
            internal int Blocks;
            internal int Decisions;
            internal int Dependencies;
            internal int Variants;

            /// <summary>경로에 읽지 못한 부분이 있는 variant 들.</summary>
            internal int Incomplete;

            internal long Milliseconds;

            /// <summary>메서드가 남은 채로 시간 예산이 떨어졌을 때 참.</summary>
            internal bool OutOfTime;

            /// <summary>호출이 남았는데도 따라가기가 멈췄을 때 참.</summary>
            internal bool Truncated;

            /// <summary>exit 로 가는 경로가 없는 블록을 쥔 메서드들.</summary>
            internal int Stranded;

            /// <summary>한계에 닿아 답이 일부만 남은 메서드들.</summary>
            internal int Limited;

            /// <summary>그래프가 담을 수 있는 것보다 블록이 많은 메서드들.</summary>
            internal int Abandoned;

            /// <summary>이미 찾은 경우와 같아서 접힌 경우들.</summary>
            internal int Folded;

            /// <summary>호출이 아니라 delegate 로 건네져 닿은 경우들.</summary>
            internal int Handed;

            internal string Describe()
            {
                if (Methods == 0 && Abandoned == 0)
                {
                    return string.Empty;
                }

                var text = " Following calls from " + Roots + " entry points reached " + Reached +
                           " methods; graphed " + Methods + ": " + Blocks + " blocks, " +
                           Decisions + " decisions, " + Dependencies + " control dependencies.";

                if (Truncated)
                {
                    text += " The call walk hit its bound of " + CallGraph.MaxMethods +
                            " routes or " + CallGraph.MaxInstructionsScanned +
                            " instructions and did not finish.";
                }

                if (Folded > 0)
                {
                    text += " " + Folded + " were another way to a case already found.";
                }

                // 읽지 못한 수와 따로 말한다. delegate 로 닿은 경우는 불완전으로 세지만 읽기 실패가 아니므로 섞으면
                // 분석이 나빠진 것처럼 보인다.
                if (Handed > 0)
                {
                    text += " " + Handed + " were handed over rather than called.";
                }

                // 합계에 넣지 않고 따로 말한다. 해내지 못한 것을 조용히 빼면 완전한 답으로 읽힌다.
                if (Stranded > 0)
                {
                    text += " " + Stranded + " hold blocks with no path to the exit.";
                }

                if (Limited > 0)
                {
                    text += " " + Limited + " hit a bound and are incomplete.";
                }

                if (Abandoned > 0)
                {
                    text += " " + Abandoned + " exceeded " + ControlFlowGraph.MaxBlocks + " blocks.";
                }

                text += " Built " + Variants + " evidence cases in " + Milliseconds + "ms";

                text += Incomplete > 0
                    ? "; " + Incomplete + " have conditions or paths that could not be read."
                    : ".";

                if (OutOfTime)
                {
                    text += " Stopped after " + BudgetMilliseconds +
                            "ms with methods left unread.";
                }

                return text;
            }
        }

        /// <summary>
        /// 피호출자 쪽 binding 을 진입점 기준으로 옮긴 것. 옮길 수 없으면 null.
        /// </summary>
        /// <remarks>
        /// 인자는 진입점이 직접 한 호출에서만 본다. 더 깊은 호출의 인자는 번역을 한 번 더 거쳐야 한다.
        /// </remarks>
        private static Binding Bound(CallSiteConditions sites, CallGraph.Trace trace)
        {
            var receiver = sites.ReceivedAlong(trace.Path, out var standing);

            string[] args = null;
            string[] whose = null;

            if (trace.Path != null && trace.Path.Count == 2)
            {
                sites.PassedOn(trace.Path[0], trace.Path[1], out args, out whose);
            }

            var binding = Binding.Of(trace.Method, receiver, standing, args, whose);

            return binding.Anything ? binding : null;
        }

        private static void Graph(
            CallGraph.Trace trace,
            ref FlowTally tally,
            List<Variant> variants,
            string triggerKind,
            CallSiteConditions sites)
        {
            var method = trace.Method;
            var graph = ControlFlowGraph.Build(method.Body);

            if (graph == null)
            {
                return;
            }

            if (graph.Abandoned)
            {
                tally.Abandoned++;
                return;
            }

            tally.Methods++;
            tally.Blocks += graph.Blocks.Count;

            var dependence = ControlDependence.Compute(graph);
            tally.Decisions += dependence.DecisionCount;
            tally.Dependencies += dependence.DependenceCount;

            if (dependence.StrandedBlocks > 0)
            {
                tally.Stranded++;
            }

            if (dependence.HitLimit)
            {
                tally.Limited++;
            }

            var before = variants.Count;
            VariantBuilder.Collect(
                method,
                trace.Entry,
                trace.Path,
                trace.PathTruncated,
                graph,
                dependence,
                variants,
                triggerKind,
                // delegate 엣지도 덮어쓰지 않는다. 그 엣지에는 호출 지점이 없어 나를 조건이 없고, 건네진 본문 안의 조건은
                // 그대로 유효하다. 경로 전체를 Always 로 바꾸면 그 조건을 잃는다.
                sites.Along(trace.Path),
                sites.StaysOnThis(trace.Path),

                // 먼 쪽 메서드가 도는 객체를 진입점 기준으로 말한 것. 모든 걸음이 같은 객체에 머물렀거나 한 걸음이라도
                // 옮길 수 없으면 null 이다.
                Bound(sites, trace));

            for (var index = before; index < variants.Count; index++)
            {
                if (trace.ThroughDelegate)
                {
                    // 읽기 실패가 아니다. delegate 를 만든 자리는 그것을 실행하는 자리가 아니므로 나를 조건이 없다.
                    variants[index].AddGap("reached-through-delegate");
                    variants[index].HandedAt = trace.HandedAt;
                    variants[index].HandedIn = trace.HandedIn;
                    variants[index].HandedTo = trace.HandedTo;
                    tally.Handed++;
                }

                if (dependence.StrandedBlocks > 0)
                {
                    variants[index].AddGap("control-flow-does-not-reach-exit");
                }

                if (dependence.HitLimit)
                {
                    variants[index].AddGap("control-dependence-limit");
                }

                if (variants[index].Incomplete)
                {
                    tally.Incomplete++;
                }
            }
        }

        /// <summary>같은 메시지를 파일과 진단 두 채널로 보고한다.</summary>
        /// <remarks>
        /// 파일은 리로드 뒤 에디터 스크립트가 콘솔로 옮긴다. 파일 쓰기는 실패할 수 있으므로 에디터 로그에도 남긴다.
        /// </remarks>
        private static void Report(List<DiagnosticMessage> diagnostics, string assemblyName, string detail)
        {
            var message = assemblyName + ": " + detail;

            if (!ScopeReport.TryWrite(assemblyName, message))
            {
                message += " (could not write " + ScopeReport.ReportDirectory + ", editor log only)";
            }

            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Warning,
                MessageData = "[Unity Play MCP] " + message
            });
        }

        /// <summary>
        /// 에디터 또는 개발 빌드일 때 참.
        /// </summary>
        /// <remarks>
        /// 출시 빌드에는 discovery 흔적이 없어야 한다. 빌드 종류로 판정하면 출시 전에 끄는 것을 기억할 필요가 없다.
        ///
        /// 두 심볼은 <c>AffordanceBootstrap</c> 의 <c>#if</c> 와 같은 쌍이다. 전처리기는 상수를 읽을 수 없어 공유하지
        /// 못하므로 하나를 바꾸면 다른 쪽도 바꾼다.
        /// </remarks>
        private static bool IsDiscoveryBuild(ICompiledAssembly compiledAssembly)
        {
            var defines = compiledAssembly.Defines;

            if (defines == null)
            {
                return false;
            }

            foreach (var define in defines)
            {
                if (string.Equals(define, "UNITY_EDITOR", StringComparison.Ordinal) ||
                    string.Equals(define, "DEVELOPMENT_BUILD", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 어셈블리를 그대로 두고 그 이유와 판정에 쓴 define 을 보고한다.
        /// </summary>
        /// <remarks>
        /// 돌지 않았다고 말하지 않으면 아무것도 못 찾은 분석과 구별되지 않는다. 판정에 관련됐을 법한 define 을 함께
        /// 나열해 위 심볼 쌍이 틀렸을 때 첫 빌드에서 드러나게 한다.
        /// </remarks>
        private static InMemoryAssembly Declined(
            ICompiledAssembly compiledAssembly,
            List<DiagnosticMessage> diagnostics)
        {
            var defines = compiledAssembly.Defines ?? Array.Empty<string>();
            var related = new List<string>();

            foreach (var define in defines)
            {
                if (define != null &&
                    (define.IndexOf("BUILD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     define.IndexOf("DEBUG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     define.IndexOf("DEVELOP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     define.IndexOf("EDITOR", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    related.Add(define);
                }
            }

            Report(diagnostics, compiledAssembly.Name,
                "not an editor or development build, so nothing was baked and the assembly is " +
                "the compiler's own. " + defines.Length + " defines, of which these could have " +
                "said otherwise: " +
                (related.Count > 0 ? string.Join(", ", related) : "none") + ".");

            return null;
        }

        /// <summary>
        /// 이름이 엔진, 툴체인, 이 패키지의 것일 때 참.
        /// </summary>
        /// <remarks>
        /// 점으로 나뉜 마디 단위로 맞춘다. 단순 접두어 검사는 <c>Systems.Gameplay</c> 를 <c>System</c> 으로 보고 게임 코드를
        /// 떨어뜨린다.
        /// </remarks>
        private static bool IsSkipped(string assemblyName)
        {
            foreach (var prefix in SkippedPrefixes)
            {
                if (!assemblyName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (assemblyName.Length == prefix.Length || assemblyName[prefix.Length] == '.')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
