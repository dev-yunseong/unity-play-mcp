using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.CodeGen
{
    internal sealed class InputMethodWeaver
    {
        private const string RuntimeAssemblyName = "UnityPlayMcp.Runtime";
        private const string UnityInputTypeName = "UnityEngine.Input";
        private const string ProxyTypeName = "UnityPlayMcp.VirtualInput";
        private static readonly HashSet<string> SupportedMethodNames = new HashSet<string>
        {
            "GetKeyDown",
            "GetKey",
            "GetKeyUp",
            "get_anyKey",
            "get_anyKeyDown",
            "get_mousePosition",
            "GetMouseButton",
            "GetMouseButtonDown",
            "GetMouseButtonUp",
            "GetAxis",
            "GetAxisRaw",
            "GetButton",
            "GetButtonDown",
            "GetButtonUp"
        };

        /// <summary>
        /// 바꿀 call instruction 과 그 자리에 들어갈 proxy method.
        /// </summary>
        private readonly struct InputCallSite
        {
            public InputCallSite(Instruction instruction, MethodDefinition proxyMethod)
            {
                Instruction = instruction;
                ProxyMethod = proxyMethod;
            }

            public Instruction Instruction { get; }
            public MethodDefinition ProxyMethod { get; }
        }

        private readonly ModuleDefinition module;

        public InputMethodWeaver(ModuleDefinition module)
        {
            this.module = module;
        }

        /// <summary>
        /// `UnityEngine.Input` 호출을 `UnityPlayMcp.VirtualInput` 호출로 바꾼다. 하나라도 바꿨으면 true.
        /// </summary>
        /// <remarks>
        /// 바꿀 call 을 먼저 찾고, 있을 때만 `UnityPlayMcp.Runtime` reference 를 붙인 뒤 import 한다.
        /// IL reference 는 weaving 의 결과이므로, reference 를 전제로 삼으면 UnityPlayMcp type 을 직접
        /// 쓰지 않는 game assembly 는 한 건도 weaving 되지 않는다.
        /// </remarks>
        public bool Process()
        {
            var candidates = CollectUnityInputCalls();
            if (candidates.Count == 0)
            {
                // 모든 assembly 에 쓸모없는 `UnityPlayMcp.Runtime` reference 가 생기지 않게 한다.
                return false;
            }

            var runtimeAssembly = ResolveRuntimeAssembly();
            var proxyMethods = runtimeAssembly.MainModule
                .GetType(ProxyTypeName)
                .Methods
                .Where(method => SupportedMethodNames.Contains(method.Name))
                .ToDictionary(GetSignature, method => method);

            var callSites = new List<InputCallSite>();
            foreach (var candidate in candidates)
            {
                var calledMethod = (MethodReference)candidate.Operand;
                if (proxyMethods.TryGetValue(GetSignature(calledMethod), out var proxyMethod))
                {
                    callSites.Add(new InputCallSite(candidate, proxyMethod));
                }
            }

            if (callSites.Count == 0)
            {
                return false;
            }

            AddRuntimeReference(runtimeAssembly.Name);

            var imported = new Dictionary<MethodDefinition, MethodReference>();
            foreach (var callSite in callSites)
            {
                if (!imported.TryGetValue(callSite.ProxyMethod, out var proxyReference))
                {
                    proxyReference = module.ImportReference(callSite.ProxyMethod);
                    imported[callSite.ProxyMethod] = proxyReference;
                }

                callSite.Instruction.Operand = proxyReference;
            }

            return true;
        }

        private List<Instruction> CollectUnityInputCalls()
        {
            var candidates = new List<Instruction>();
            foreach (var method in module.Types
                         .SelectMany(SelfAndNestedTypes)
                         .SelectMany(type => type.Methods)
                         .Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference calledMethod &&
                        calledMethod.DeclaringType.FullName == UnityInputTypeName &&
                        SupportedMethodNames.Contains(calledMethod.Name))
                    {
                        candidates.Add(instruction);
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        /// runtime assembly 를 연다. IL 에 reference 가 없어도 열 수 있다.
        /// </summary>
        /// <remarks>
        /// <see cref="CompiledAssemblyResolver"/> 는 파일 이름만 보고 version 을 무시하므로 이름만 담은
        /// 임시 reference 로 resolve 된다. 임시 reference 는 module 에 넣지 않는다.
        /// `WillProcess` 가 runtime dll 이 references 에 있을 때만 통과시키므로 resolve 실패는 잡지 않는다.
        /// </remarks>
        private AssemblyDefinition ResolveRuntimeAssembly()
        {
            var existing = module.AssemblyReferences
                .FirstOrDefault(reference => reference.Name == RuntimeAssemblyName);

            return module.AssemblyResolver.Resolve(
                existing ?? new AssemblyNameReference(RuntimeAssemblyName, new Version(0, 0, 0, 0)));
        }

        /// <summary>
        /// `UnityPlayMcp.Runtime` 에 대한 assembly reference 를 module 에 붙인다. import 하기 전에 부른다.
        /// </summary>
        /// <remarks>
        /// Cecil 0.11.4 importer 도 같은 reference 를 넣지만, 그 내부 동작이 바뀌면 weaving 이 조용히
        /// 깨지므로 명시적으로 붙인다. importer 는 `FullName` 으로 기존 reference 를 재사용하고
        /// 여기서 resolve 된 정의의 `Name`/`Version`/`Culture`/`PublicKeyToken` 을 그대로 쓰므로 중복되지 않는다.
        /// </remarks>
        private void AddRuntimeReference(AssemblyNameDefinition runtimeName)
        {
            if (module.AssemblyReferences.Any(reference => reference.Name == RuntimeAssemblyName))
            {
                return;
            }

            module.AssemblyReferences.Add(new AssemblyNameReference(runtimeName.Name, runtimeName.Version)
            {
                Culture = runtimeName.Culture,
                PublicKeyToken = runtimeName.PublicKeyToken,
                HashAlgorithm = runtimeName.HashAlgorithm,
                IsRetargetable = runtimeName.IsRetargetable,
                IsWindowsRuntime = runtimeName.IsWindowsRuntime
            });
        }

        private static string GetSignature(MethodReference method)
        {
            return method.Name + "(" +
                   string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName)) +
                   ")";
        }

        private static IEnumerable<TypeDefinition> SelfAndNestedTypes(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes.SelectMany(SelfAndNestedTypes))
            {
                yield return nested;
            }
        }
    }
}
