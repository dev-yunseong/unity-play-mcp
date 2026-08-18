using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 게임의 컴파일된 코드를 읽어, 그 behaviour 들이 무엇을 듣고 무엇을 바꾸는지 기록한다.
    /// </summary>
    /// <remarks>
    /// Unity 컴파일 파이프라인 안에서 돈다. 곧 게임 팀에게 패키지 설치 말고는 아무것도 요구하지 않는다는 뜻이다 —
    /// attribute 도, 수정도, 그들 자신의 빌드 단계도 없다. IL2CPP 가 무엇을 변환하기 전에 돌기도 하므로 최종 빌드 형식이
    /// 무엇이든 결과는 거기 있다.
    ///
    /// 이것은 예전에 프로젝트가 설정해야 하는 스크립팅 define 뒤에 앉아 있었다. 이 분석이 쓰이는 동안 세 번에 걸쳐 그것이
    /// 에디터를 먹통으로 만들었고 그때마다 왜 그런지 알아보려고 그것을 열 수조차 없었다 — 그리고 제거하지 않고 끄는 스위치는
    /// 그것이 찾아낼 수 있는 무엇보다 값졌다.
    ///
    /// 그 스위치를 대신한 것은 그때는 없던 세 겹이다: 여기의 모든 루프가 유계이고, 어셈블리 하나에 10초가 주어진 뒤에는 닿은
    /// 만큼을 보고하고 나머지를 남기며, 어떤 throw 든 <see cref="Process"/> 에 떨어져 컴파일러 자신의 어셈블리를 되돌려준다.
    /// 마지막 것은 실패를 주입하고 빌드가 그것을 견디는 것을 지켜봐 확인했다.
    ///
    /// define 은 그 값을 하기를 그만두었다. 도구가 존재하려면 프로젝트가 옵트인돼 있어야 하므로 무언가가 그들을 대신해 옵트인해
    /// 주어야 했고, 그러면 그것이 막아 주던 상태 — 설치됐는데 꺼짐 — 는 손으로만 도달하는 곳이 됐다. 그러는 동안 아래쪽
    /// 전부가 무언가에 기대기 전에 분석이 존재하기는 하는지를 먼저 물어야 했다.
    ///
    /// 게임 어셈블리에 쓰이는 것은 attribute 하나와 압축된 리소스 둘이다. 메서드 본문은 건드리지 않고, 아무것도 이름을 바꾸지
    /// 않으며, 게임은 전과 똑같이 돈다.
    /// </remarks>
    public sealed class AffordanceILPostProcessor : ILPostProcessor
    {

        /// <summary>
        /// 나머지를 남겨 둔 채 끊기까지 한 어셈블리를 얼마나 오래 분석할 수 있는지.
        /// </summary>
        /// <remarks>
        /// 여기의 모든 루프는 유계이므로 분석을 끝나게 하는 것은 이것이 아니다. 이것은 분석을 *곧* 끝나게 한다:
        /// 아무도 예상 못 한 모양의 어셈블리는 완벽히 유한하면서도 느릴 수 있고, 몇 분씩 멈춰 있는 컴파일은 그것을
        /// 기다리는 사람에게 고장 난 빌드다. 그때까지 닿은 것은 일찍 멈췄다는 사실과 함께 보고된다.
        /// </remarks>
        private const long BudgetMilliseconds = 10000;

        /// <summary>
        /// 엔진, 툴체인, 또는 이 벤더에 속하는 어셈블리들.
        /// </summary>
        /// <remarks>
        /// 무엇을 취할지가 아니라 무엇을 건너뛸지로 이름 붙인다. 이 패키지에 대한 참조를 요구하는 편이 더 깔끔했겠지만,
        /// auto-reference 는 Unity 의 미리 정의된 어셈블리에만 닿는다 — 코드를 assembly definition 으로 쪼갠 게임은
        /// 통째로 지나쳐지고, 그런 프로젝트야말로 이것을 가장 원할 만한 곳이다.
        ///
        /// <c>Artel.Affordances</c> 가 아니라 <c>Artel</c> 이다: 그 아래의 모든 것이 이 벤더의 것이고, 프로젝트가 함께
        /// 설치했을 수 있는 형제 SDK 도 거기 든다. 샘플 프로젝트에서 실측하니 그 SDK 자신의 컴포넌트 둘이 2.5MB 짜리
        /// 리포트 중 2MB 를 차지했다 — 도구에 대한 근거인데, 그것은 아무도 명세를 원하는 대상이 아니다.
        ///
        /// 맞추기는 이름 경계에서 일어나므로 <c>Artel</c> 은 <c>Artel.Tracking</c> 을 덮고 그저 그 글자로 시작하기만
        /// 하는 것은 건드리지 않는다. 넓은 접두어의 대가는, 제 어셈블리를 그렇게 이름 지은 게임이 **조용히**
        /// 지나쳐진다는 것이다 — <see cref="WillProcess"/> 에서 결정된 거절은 스스로를 보고할 자리가 없고, 빌드
        /// 종류에 대한 물음을 이것보다 나중에 묻는 것도 같은 이유다.
        /// </remarks>
        private static readonly string[] SkippedPrefixes =
        {
            "Artel", "Unity.Artel",
            "UnityEngine", "UnityEditor", "Unity", "System", "mscorlib", "netstandard",
            "nunit", "Newtonsoft", "Mono"
        };

        public override ILPostProcessor GetInstance() => this;

        /// <remarks>
        /// 이 빌드가 어떤 종류인지는 어셈블리를 건드리지 않을 이유이고 그런 것들이 사는 자리가 여기인데도, 여기가 아니라
        /// <see cref="Process"/> 에서 묻는다. 여기서 아니오라고 답하면 <see cref="Process"/> 가 한 번도 불리지 않는데, 진단을
        /// 쥐고 있는 것이 <see cref="Process"/> 다 — 그래서 여기서 결정된 거절은 아무에게도 알려지지 않는 거절이다. 여기서
        /// 답하는 하나는 사람이 이미 답을 아는 것이다: 그들은 자기 어셈블리를 무엇이라 이름 지었는지 안다.
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
                // 빌드를 절대 무너뜨리지 않는다. 결과가 없으면 생성된 명세의 정확도를 잃지만, 어셈블리가 망가지면 게임 팀이
                // 제 빌드를 잃는다. 그리고 여기의 전제 전체가 남의 프로젝트에 넣어도 안전하다는 것이다.
                Report(diagnostics, compiledAssembly.Name, "skipped, " + exception.Message);
                baked = null;
            }

            // null 은 컴파일러 자신의 출력을 있는 그대로 쓴다는 뜻이다. 실패하거나, 거절하거나, 아무것도 찾지 못한 모든
            // 경로가 null 을 들고 여기 도착하므로, 전체가 다 되지 않는 한 원본이 선다.
            return new ILPostProcessResult(baked, diagnostics);
        }

        /// <summary>
        /// 범위 안에 무엇이 있는지 세고 그것을 말한다.
        /// </summary>
        /// <remarks>
        /// 이 단계의 산출물은 그 개수들이다. 이 패키지의 이전 빌드에는 생성에 실패해 null 을 돌려주는 writer 가 있었고,
        /// 뒤이은 스캔은 커버리지 공백이 없다고 보고했다 — 게임을 덮었기 때문이 아니라, 대조할 것이 하나도 없었기
        /// 때문이다. 침묵이 성공으로 읽혔다. 이 단계가 무엇을 해내든 해내지 못하든, 어느 쪽인지를 말한다.
        /// </remarks>
        private static InMemoryAssembly Survey(
            ICompiledAssembly compiledAssembly,
            List<DiagnosticMessage> diagnostics)
        {
            // 프로퍼티의 이름은 어셈블리마다 다른 필드를 뜻하므로, 하나에서 배운 것을 다음으로 나를 수 없다.
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

                // GetTypes 는 중첩 타입까지 닿고, 컴파일러가 코루틴이나 람다의 본문을 넣는 자리가 거기다 — behaviour 에서
                // 가장 놓치기 쉬운 부분이다.
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

                // 진입점은 어디서 시작할지이지 무엇을 읽을지가 아니다. Update 는 호출 셋에 결정은 하나도 없기 십상이고,
                // 키와 그것을 지키는 조건들은 그것이 부르는 private 헬퍼 안에 앉아 있다.
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

                    // 직선 메서드도 여전히 직접 효과와 호출 엣지를 나른다. 그것이 버튼 핸들러와 헬퍼의 평범한 모양이므로,
                    // 분기가 있는 메서드만이 아니라 닿은 모든 메서드가 유계 그래프를 받는다.
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

                // 아래의 공백들이 그것들에 매달리기 전에 한다. 결국 같은 공백을 나르게 될 두 경우가 여전히 같은 경우로
                // 인식되도록.
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

                // 무엇을 찾았든 못 찾았든 말하고, 아무것도 못 찾았을 때 먼저 말한다. 기반 타입이 해석되지 않는 채로
                // behaviour 가 없다고 보고된 어셈블리는 작은 결과가 아니라 믿을 수 없는 결과다.
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

                // 두 숫자를 함께 내거나 둘 다 내지 않는다. 목록만 있으면 게임이 도는 동안 확인할 수 있는 것의 전부처럼
                // 읽히는데, 거절이 작을 때만 그렇다.
                message += " " + written.Watched + " members to watch, " +
                           written.Unwatchable + " values with nowhere to read them.";

                if (written.Oversized > 0)
                {
                    message += " " + written.Oversized +
                               " evidence documents exceeded their serialization bound and were dropped.";
                }

                if (carriedSymbols && !readSymbols)
                {
                    // 심볼 없이 다시 쓴 어셈블리를 돌려주면 게임 팀이 제 코드에서 스택 트레이스와 중단점을 잃는다. 결과 하나만큼의
                    // 값도 없다.
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
        /// 새 스트림에 쓰고, 쓰기가 끝난 뒤에야 돌려준다. Cecil 이 중간에 실패하면 반쯤 쓰인 버퍼가 남는데 그것은 이
        /// 메서드를 결코 벗어나지 않고, 호출자는 대신 컴파일러가 만든 것을 돌려준다.
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
                    // 심볼 없이 다시 읽는다. 분석에는 심볼이 필요 없다. 달라지는 것은 결과를 되쓸 수 없게 된다는 점이고,
                    // 호출자가 그것을 검사한다.
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

            /// <summary>거기 오는 길에 읽을 수 없는 것이 있던 variant 들.</summary>
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

            /// <summary>이미 찾은 경우에 이르는 또 하나의 갈래로 밝혀진 경우들.</summary>
            internal int Folded;

            /// <summary>메서드가 불린 것이 아니라 건네진 경우들.</summary>
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

                // 읽지 못한 수와 따로 말한다. 그러지 않으면 그 수를 부풀린다. 델리게이트를 거쳐 닿은 경우는 그 엣지를 건너는
                // 조건이 없으므로 불완전으로 세는데 — 그것은 결정이지 실패한 읽기가 아니고, 그 둘이 합쳐져 분석이 나빠진
                // 것처럼 보이는 숫자 하나가 되어서는 안 된다.
                if (Handed > 0)
                {
                    text += " " + Handed + " were handed over rather than called.";
                }

                // 합계에 접어 넣지 않고 따로 말한다. 해내지 못한 것을 조용히 빼놓은 개수는 완전한 답으로 읽힌다.
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
        /// 피호출자의 말을 진입점이 선 자리에서 말한 것, 또는 그럴 수 없을 때 없음.
        /// </summary>
        /// <remarks>
        /// 진입점이 스스로 한 호출에 대해서만 인자를 본다. 두 걸음 아래에서 넘어간 것은 첫 피호출자의 말로 쓰여 있고,
        /// 그것을 되나르는 일은 수신자가 이미 필요로 하는 번역 위에 얹는 두 번째 번역이다.
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
                // 델리게이트 엣지에 대해서는 덮어쓰지 않는다. 경로를 따라 합성하는 것만으로 이미 옳은 답이 나온다: 메서드를
                // 건넨 엣지에는 호출 지점이 없으므로 그것을 건너 나르는 것이 없고, 건네진 본문 *안* 의 조건들은 진짜이며 그
                // 본문의 것이다. 경로 전체를 Always 로 갈아치우면 그것들을 버리고 어셈블리의 3분의 1이 읽지 못한 것으로
                // 읽히게 됐다.
                sites.Along(trace.Path),
                sites.StaysOnThis(trace.Path),

                // 먼 쪽 끝이 무엇 위에서 도는지를 진입점이 선 자리에서 말한 것. 모든 걸음이 같은 객체에 머물렀으면 null 이고,
                // 한 걸음이라도 나를 수 없는 순간 null 이다.
                Bound(sites, trace));

            for (var index = before; index < variants.Count; index++)
            {
                if (trace.ThroughDelegate)
                {
                    // 이 경로를 따라 나른 것이 없고, 그것은 읽기의 실패가 아니라 결정이다: 델리게이트를 만든 자리는 그것을 돌리는
                    // 자리가 아니다.
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

        /// <summary>같은 말을 두 번 한다. 어느 채널도 혼자서는 충분하지 않기 때문이다.</summary>
        /// <remarks>
        /// 콘솔에 닿는 것은 파일이고, 리로드 뒤에 그것을 읽는 에디터 스크립트를 거친다. 진단은 에디터 로그에만 닿는데,
        /// 거기는 파일을 쓰지 못했을 때 들여다볼 자리다 — 그리고 실패할 수 있는 쪽이 파일이므로 로그가 어느 쪽이든
        /// 기록을 지킨다.
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
                MessageData = "[Artel] " + message
            });
        }

        /// <summary>
        /// 이 컴파일이 사람이 개발하면서 대고 있는 것일 때 참.
        /// </summary>
        /// <remarks>
        /// discovery 는 게임을 만드는 동안 하는 일이고, 출시된 게임은 그 흔적을 하나도 나르지 않아야 한다. 빌드에게
        /// 어떤 종류의 빌드인지 물으면, 누구도 출시 전에 무언가를 꺼야 한다는 것을 기억하지 않고도 그 답이 나온다 —
        /// 그리고 기억하는 일이야말로 조용히, 한 번, 하필 나가는 그 빌드에서 실패하는 것이다.
        ///
        /// 두 심볼은 에디터 자신의 것과 플레이어의 개발 플래그다. 같은 쌍이 <c>AffordanceBootstrap</c> 에서
        /// <c>#if</c> 로 한 번 더 적히고, 그쪽이 런타임 쪽의 대응하는 물음을 결정한다. 둘은 상수를 공유할 수 없다:
        /// 그쪽은 전처리기 검사이고 제 어셈블리가 컴파일되는 자리에서 평가되며, 전처리기는 어디서도 값을 읽을 수 없다.
        /// 하나를 바꾸면 다른 하나도 바꿔라.
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
        /// 어셈블리를 그대로 두고 왜 그런지 말하며, 무엇을 근거로 그랬는지 나열한다.
        /// </summary>
        /// <remarks>
        /// 빌드의 종류로 결정하는 방식은 쓰는 데 값이 들지 않고 잊힐 수도 없지만, 사람에 *의해* 가 아니라 사람에
        /// *대해* 결정되므로 실패하는 길은 하나뿐이다: 침묵. 돌지 않으면서 그렇다고 말하지도 않는 분석은 아무것도 찾지
        /// 못한 분석과 똑같이 읽힌다. 그래서 나가는 길에 메시지를 남긴다.
        ///
        /// 그 답을 나르고 있었을 법한 define 들을 함께 나열한다. 위의 쌍이 언젠가 찾아볼 쌍으로 틀린 것으로 드러나면,
        /// 그렇다고 말해 주는 것이 이 줄이다. 그리고 뒤지고 난 뒤가 아니라 첫 빌드에서 말한다.
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
        /// 그 이름이 엔진, 툴체인, 또는 이 패키지의 것일 때 참.
        /// </summary>
        /// <remarks>
        /// 글자가 아니라 점으로 나뉜 마디 전체로 맞춘다. 단순한 접두어 검사는 <c>Systems.Gameplay</c> 를
        /// <c>System</c> 으로 읽고 게임 자신의 코드를 아무 말 없이 떨어뜨리는데, 게임플레이 코드를 <c>Systems</c>
        /// 어셈블리로 쪼개는 것은 평범한 일이다.
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
