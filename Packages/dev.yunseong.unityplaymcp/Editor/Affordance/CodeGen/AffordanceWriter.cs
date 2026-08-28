using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 찾아낸 것을 게임 자신의 타입 위에 굽는다.
    /// </summary>
    /// <remarks>
    /// 이 패키지 전체를 통틀어 게임 어셈블리에 가하는 유일한 변경이다: 타입에 커스텀 attribute 하나를
    /// 더한다. 메서드 본문은 건드리지 않고, 아무것도 이름을 바꾸지 않으며, 코드를 끼워 넣지 않는다.
    /// attribute 는 게임이 하는 일을 바꿀 수 없고, 그 성질이 이것을 남의 빌드에 넣어도 안전하게 만든다 —
    /// 그리고 분석이 계측이 아니라 메타데이터를 굽는 이유이기도 하다.
    ///
    /// 옆에 표를 따로 두지 않고 타입에 붙이는 것이 여정을 견디는 방법이다. 이것을 읽는 스캔은 이름이
    /// 난독화되고 IL2CPP 가 전부 다시 쓴 빌드된 플레이어를 상대로 돌아간다. attribute 는 그 타입이 무엇이
    /// 되었든 여전히 그 위에 있다.
    /// </remarks>
    internal static class AffordanceWriter
    {
        private const string RuntimeAssembly = "Artel.Affordances.Runtime";
        private const string AttributeType = "Artel.Affordances.AffordanceAttribute";

        private const int MaxPayloadCharacters = 32768;

        internal sealed class Result
        {
            internal int Written;
            internal int Unattached;
            internal int Oversized;
            internal string Refusal;
            internal int ResourceBytes;
            internal int Anchored;

            /// <summary>근거가 누군가에게 감시하라고 청하는 서로 다른 멤버의 수.</summary>
            internal int Watched;

            /// <summary>
            /// 읽을 자리가 없는 값을 부른 조건과 효과가 몇이었는가.
            /// </summary>
            /// <remarks>
            /// 목록 옆에 함께 적는다. 두 숫자는 함께여야만 뜻이 있기 때문이다. 큰 거절 수 옆의 짧은 목록은 논리가
            /// 호출을 통해 흐르는 게임이고, 목록만 본 감시자는 자기가 전부를 덮었다고 생각하게 된다.
            /// </remarks>
            internal int Unwatchable;
        }

        /// <summary>
        /// variant 마다 attribute 하나를 그것이 속한 타입에 더한다.
        /// </summary>
        /// <remarks>
        /// attribute 를 담은 어셈블리를 디스크에서 찾지 못하면 추측하지 않고 거절한다. 어셈블리 정체를 지어
        /// 내면 그 이름으로는 존재하지 않을 수 있는 무언가를 가리키는 게임 어셈블리가 나오고, 그 실패는 여기가
        /// 아니라 빌드된 플레이어의 타입 로드 오류로 도착한다. 파일을 찾아 그 정체를 읽는 것은 추측이 아니고,
        /// 그래서 <see cref="FindRuntimeAssembly"/> 는 컴파일러의 참조 목록 너머를 봐도 된다.
        /// </remarks>
        internal static Result Write(
            ModuleDefinition module,
            ICompiledAssembly compiledAssembly,
            IAssemblyResolver resolver,
            List<Variant> variants)
        {
            var result = new Result();

            var attributePath = FindRuntimeAssembly(compiledAssembly);

            if (attributePath == null)
            {
                result.Refusal = "the runtime assembly is not among this assembly's references";
                return result;
            }

            MethodReference constructor;

            using (var runtime = AssemblyDefinition.ReadAssembly(
                attributePath,
                new ReaderParameters(ReadingMode.Deferred) { AssemblyResolver = resolver }))
            {
                var attribute = runtime.MainModule.GetType(AttributeType);

                if (attribute == null)
                {
                    result.Refusal = "the runtime assembly does not define " + AttributeType;
                    return result;
                }

                MethodDefinition declared = null;

                foreach (var method in attribute.Methods)
                {
                    if (method.IsConstructor && method.Parameters.Count == 2 &&
                        method.Parameters[0].ParameterType.MetadataType == MetadataType.Int32 &&
                        method.Parameters[1].ParameterType.MetadataType == MetadataType.Int32)
                    {
                        declared = method;
                        break;
                    }
                }

                if (declared == null)
                {
                    result.Refusal = "the attribute has no constructor of the expected shape";
                    return result;
                }

                // Cecil 로 읽은 파일에서 가져온다. 로드하지는 않는다. 이것이 모듈에 더하는 어셈블리 참조는 실제로
                // 출시될 어셈블리의 진짜 정체다.
                constructor = module.ImportReference(declared);
            }

            var integers = module.TypeSystem.Int32;

            // 이미 구워져 있는 것은 먼저 지운다. 파이프라인은 갓 컴파일된 어셈블리를 건네주므로 여기서 하나도
            // 찾지 못해야 한다 — 다만 여기를 두 번 지난 어셈블리는 근거 두 세대를 한꺼번에 나르게 되고, 옛 절반은
            // 새 것과 구분되지 않으면서 조용히 그것과 어긋난 말을 한다.
            Clear(module, variants);
            EvidenceResource.Detach(module);

            // 타입으로 묶는다. 그렇게 물어보기 때문이다. 한 씬은 적은 수의 타입의 인스턴스를 많이 쥐고 있고,
            // 스캔은 타입마다 한 번씩 답을 찾는다.
            var byOwner = new List<TypeDefinition>();
            var payloadsByOwner = new Dictionary<TypeDefinition, List<string>>();

            var callers = Callers(module, variants);

            foreach (var variant in variants)
            {
                if (variant.Owner == null || variant.Owner.Module != module)
                {
                    // 게임이 닿기는 하지만 GameObject 에 매달지 않는 코드에서 찾은 것. 스캔은 자기가 찾은 컴포넌트에서
                    // 타입을 찾아 올라가는데, 이것은 찾아 올라갈 것이 없다.
                    result.Unattached++;
                    continue;
                }

                bool truncated;
                var payload = EvidenceJson.Write(variant, callers, out truncated);

                if (truncated)
                {
                    variant.AddGap("evidence-serialization-limit");
                    payload = EvidenceJson.Write(variant, out truncated);
                }

                if (payload.Length > MaxPayloadCharacters)
                {
                    result.Oversized++;
                    continue;
                }

                if (!payloadsByOwner.TryGetValue(variant.Owner, out var list))
                {
                    list = new List<string>();
                    payloadsByOwner[variant.Owner] = list;
                    byOwner.Add(variant.Owner);
                }

                list.Add(payload);
                result.Written++;
            }

            var blob = new StringBuilder(1024);

            for (var anchor = 0; anchor < byOwner.Count; anchor++)
            {
                var owner = byOwner[anchor];

                // anchor 는 이름 바꾸기를 견디고, 이름은 스트리핑을 견딘다. 둘 중 무엇이 남든 이것을 찾을 수 있도록
                // 둘 다 쓴다.
                var attribute = new CustomAttribute(constructor);
                attribute.ConstructorArguments.Add(
                    new CustomAttributeArgument(integers, EvidenceJson.SchemaVersion));
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(integers, anchor));
                owner.CustomAttributes.Add(attribute);

                blob.Append(anchor).Append('\t').Append(owner.FullName).Append('\t')
                    .Append('[').Append(string.Join(",", payloadsByOwner[owner].ToArray())).Append(']')
                    .Append('\n');
            }

            result.Anchored = byOwner.Count;
            result.ResourceBytes = EvidenceResource.Attach(module, blob.ToString());

            // anchor 를 타입에 붙이지 못한 것까지 포함해 모든 variant 에서 쓴다. 무엇을 감시할지는 어셈블리에 대한
            // 물음이고, 어떤 GameObject 도 나르지 않는 클래스의 static 필드야말로 화면을 결정하는 그런 종류다.
            var watch = WatchListJson.Write(variants);
            result.Watched = watch.Watched;
            result.Unwatchable = watch.Unwatchable;
            result.ResourceBytes += EvidenceResource.AttachWatch(module, watch.Document);

            return result;
        }

        /// <summary>이 어셈블리에 대한 앞선 패스의 근거를 걷어낸다.</summary>
        /// <summary>
        /// 기록이 시작하는 각 메서드를 누가 부르는가. 어셈블리 전체에서 읽는다.
        /// </summary>
        /// <remarks>
        /// 기록은 자기가 무엇을 부르는지 말한다. 무엇이 자기를 부르는지는 아무도 말하지 않았고, 그 둘은 같은
        /// 목록을 거꾸로 읽은 것이 아니다: 기록의 호출은 살아남은 블록 안의 것들이고, 제 기록이 없는 호출자는
        /// 아무 자취도 남기지 않는다. 샘플 게임의 기능 여섯이 그 뒤에 앉아 있다 — 카드는 behaviour 가 아닌 턴
        /// 상태에서 돌려지고 드롭 존은 코루틴 안에서 비워지는데, 두 호출자 어느 쪽도 문서 어디에도 없다.
        /// 기록이 아니라 어셈블리에서 읽으면 둘 다 있다.
        ///
        /// 기록이 시작하는 메서드에 대해서만 한다. 답하려는 물음이 그것이기 때문이다: 어떻게 닿는지 아무도 볼
        /// 수 없는 기록이 있을 때, 무엇이 거기 닿는가. 다른 모든 엣지는 이미 쓰이는 자리에 적혀 있다.
        ///
        /// 호출자의 이름을 대고 멈춘다. 그 호출자가 테스터가 할 수 있는 일인지는 독자의 물음이고,
        /// <c>MoveNext</c> 에서만 불리는 메서드는 그에 대한 정직한 답이다 — "아무것도 이것을 부르지 않는다" 와
        /// 같은 모양이던 침묵보다는 낫다.
        /// </remarks>
        private static Dictionary<string, List<string>> Callers(
            ModuleDefinition module, List<Variant> variants)
        {
            var wanted = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var variant in variants)
            {
                if (variant.EntryId != null)
                {
                    wanted.Add(variant.EntryId);
                }
            }

            var found = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

            if (wanted.Count == 0)
            {
                return found;
            }

            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    var from = MethodIdentity.Of(method);

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.Code != Code.Call &&
                            instruction.OpCode.Code != Code.Callvirt &&
                            instruction.OpCode.Code != Code.Newobj &&
                            instruction.OpCode.Code != Code.Ldftn)
                        {
                            continue;
                        }

                        var called = MethodIdentity.Of(instruction.Operand as MethodReference);

                        if (called == null || called == from || !wanted.Contains(called))
                        {
                            continue;
                        }

                        if (!found.TryGetValue(called, out var list))
                        {
                            list = new List<string>();
                            found[called] = list;
                        }

                        if (!list.Contains(from))
                        {
                            list.Add(from);
                        }
                    }
                }
            }

            return found;
        }

        private static void Clear(ModuleDefinition module, List<Variant> variants)
        {
            var seen = new HashSet<TypeDefinition>();

            foreach (var variant in variants)
            {
                var owner = variant.Owner;

                if (owner == null || owner.Module != module || !seen.Add(owner))
                {
                    continue;
                }

                for (var index = owner.CustomAttributes.Count - 1; index >= 0; index--)
                {
                    if (owner.CustomAttributes[index].AttributeType.FullName == AttributeType)
                    {
                        owner.CustomAttributes.RemoveAt(index);
                    }
                }
            }
        }

        /// <summary>
        /// attribute 를 담은 어셈블리가 디스크의 어디에 있는가.
        /// </summary>
        /// <remarks>
        /// 게임 자신의 코드는 <c>Assembly-CSharp</c> 으로 컴파일되고, 그것은 auto-reference 되는 모든 패키지를
        /// 참조하므로 이것도 목록에 있다. assembly definition 으로 쪼갠 코드는 자기가 선언한 것만 참조하는데,
        /// 게임 팀이 이것을 선언할 이유는 없다 — 약속 전체가 그들은 아무것도 바꾸지 않는다는 것이다. 예전에는
        /// 그런 어셈블리를 거절했고, 그 결과 이것을 가장 원할 만한 프로젝트가 가장 적게 받았다: 샘플
        /// 프로젝트에서 어셈블리 하나가 근거 324건을 만들고 그중 하나도 굽지 못했다.
        ///
        /// 그래서 참조 목록을 먼저 묻고, 그것이 부르는 디렉터리들을 그다음에 뒤진다. 게임 어셈블리에 쓰이는
        /// 정체는 찾아낸 파일에서 읽지 이름으로 지어내지 않으며, 앞선 거절이 막고 있던 것이 바로 그것이다 —
        /// 그 이름으로 존재하지 않는 무언가에 대한 참조는 여기가 아니라 빌드된 플레이어에서 타입 로드 오류로
        /// 실패한다.
        /// </remarks>
        private static string FindRuntimeAssembly(ICompiledAssembly compiledAssembly)
        {
            var references = compiledAssembly.References;

            if (references == null)
            {
                return null;
            }

            var folders = new List<string>();

            foreach (var reference in references)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileNameWithoutExtension(reference), RuntimeAssembly,
                        StringComparison.Ordinal) && File.Exists(reference))
                {
                    return reference;
                }

                var folder = Path.GetDirectoryName(reference);

                if (!string.IsNullOrEmpty(folder) && !folders.Contains(folder))
                {
                    folders.Add(folder);
                }
            }

            // 컴파일 산출물은 한자리에 모이므로, 아무것도 그것을 가리키지 않았을 때에도 이 패키지가 만드는
            // 어셈블리는 게임이 대고 컴파일된 것들 옆에 있다.
            foreach (var folder in folders)
            {
                var candidate = Path.Combine(folder, RuntimeAssembly + ".dll");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
