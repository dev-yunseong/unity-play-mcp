using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 찾아낸 것을 게임 자신의 타입 위에 굽는다.
    /// </summary>
    /// <remarks>
    /// 게임 어셈블리에 가하는 유일한 변경은 타입에 attribute 를 더하는 것이다. attribute 는 게임 동작을 바꿀 수
    /// 없으므로 남의 빌드에 넣어도 안전하다.
    ///
    /// 별도 표가 아니라 타입에 붙이므로 난독화와 IL2CPP 변환 뒤에도 그 타입 위에 남는다.
    /// </remarks>
    internal static class AffordanceWriter
    {
        private const string RuntimeAssembly = "UnityPlayMcp.Affordances.Runtime";
        private const string AttributeType = "UnityPlayMcp.Affordances.AffordanceAttribute";

        private const int MaxPayloadCharacters = 32768;

        internal sealed class Result
        {
            internal int Written;
            internal int Unattached;
            internal int Oversized;
            internal string Refusal;
            internal int ResourceBytes;
            internal int Anchored;

            /// <summary>`evidence` 가 감시를 요청하는 서로 다른 멤버의 수.</summary>
            internal int Watched;

            /// <summary>
            /// 읽을 자리가 없는 값을 참조한 조건과 효과의 수.
            /// </summary>
            /// <remarks>
            /// <see cref="Watched"/> 와 함께 봐야 뜻이 있다. 이 수가 크면 감시 목록이 게임을 다 덮지 못한다.
            /// </remarks>
            internal int Unwatchable;
        }

        /// <summary>
        /// variant 마다 그것이 속한 타입에 attribute 를 더한다.
        /// </summary>
        /// <remarks>
        /// runtime 어셈블리를 디스크에서 찾지 못하면 거절한다. 어셈블리 정체를 이름으로 지어내면 존재하지 않는 대상을
        /// 참조하게 되고, 그 실패는 빌드된 플레이어의 타입 로드 오류로 나타난다.
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

                // 로드하지 않고 Cecil 로 읽은 파일에서 가져오므로 모듈에 더해지는 참조는 실제 출시될 어셈블리의 정체다.
                constructor = module.ImportReference(declared);
            }

            var integers = module.TypeSystem.Int32;

            // 이미 구워진 것을 먼저 지운다. 보통은 없지만, 두 번 처리된 어셈블리가 서로 어긋난 두 세대의 `evidence` 를
            // 나르지 않게 한다.
            Clear(module, variants);
            EvidenceResource.Detach(module);

            // 스캔이 타입마다 한 번씩 조회하므로 타입으로 묶는다.
            var byOwner = new List<TypeDefinition>();
            var payloadsByOwner = new Dictionary<TypeDefinition, List<string>>();

            var callers = Callers(module, variants);

            foreach (var variant in variants)
            {
                if (variant.Owner == null || variant.Owner.Module != module)
                {
                    // GameObject 에 붙지 않는 타입에서 찾은 것이다. 스캔은 컴포넌트에서 타입을 찾아가므로 닿을 수 없다.
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

                // anchor 는 이름 바꾸기를, 이름은 스트리핑을 견딘다. 어느 쪽이 남든 찾을 수 있게 둘 다 쓴다.
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

            // 타입에 붙이지 못한 variant 까지 모두 쓴다. GameObject 에 붙지 않는 클래스의 static 필드도 화면을 결정할 수
            // 있다.
            var watch = WatchListJson.Write(variants);
            result.Watched = watch.Watched;
            result.Unwatchable = watch.Unwatchable;
            result.ResourceBytes += EvidenceResource.AttachWatch(module, watch.Document);

            return result;
        }

        /// <summary>
        /// `evidence` 가 시작하는 각 메서드의 호출자. 어셈블리 전체에서 읽는다.
        /// </summary>
        /// <remarks>
        /// `evidence` 의 호출 목록을 거꾸로 읽으면 안 된다. 그 목록은 살아남은 블록의 호출만 담고, 자기 `evidence` 가
        /// 없는 호출자(behaviour 가 아닌 상태 객체, coroutine)는 빠진다.
        ///
        /// 호출자 이름만 적는다. 그것이 테스터가 할 수 있는 일인지는 읽는 쪽이 판단한다.
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

        /// <summary>이 어셈블리에 앞선 패스가 붙인 attribute 를 걷어낸다.</summary>
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
        /// runtime 어셈블리의 디스크 경로.
        /// </summary>
        /// <remarks>
        /// assembly definition 으로 쪼갠 게임 코드는 이 패키지를 참조하지 않을 수 있다. 그래서 참조 목록을 먼저 보고,
        /// 없으면 참조들이 있는 디렉터리를 뒤진다. 정체는 찾은 파일에서 읽고 이름으로 지어내지 않는다.
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

            // 컴파일 산출물은 한 디렉터리에 모이므로 참조되지 않았어도 runtime 어셈블리가 거기 있다.
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
