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
        /// 바꿀 call 하나. instruction 과 그 자리에 들어갈 proxy method 를 함께 든다.
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
        /// 순서가 이 method 의 전부다. `UnityPlayMcp.Runtime` 에 대한 IL assembly reference 는 weaving 의
        /// 전제가 아니라 결과다 — IL 은 type 을 실제로 쓰는 자리에만 reference 를 남기고, 그 자리를 만드는
        /// 일이 바로 weaving 이다. 그래서 바꿀 call 을 먼저 찾고, 바꿀 것이 있을 때만 reference 를 module 에
        /// 붙인 뒤 import 한다. 반대로 하면 UnityPlayMcp type 을 손으로 참조하지 않는 보통의 game assembly 는
        /// 조건이 영원히 성립하지 않아 한 건도 weaving 되지 않는다.
        /// </remarks>
        public bool Process()
        {
            var candidates = CollectUnityInputCalls();
            if (candidates.Count == 0)
            {
                // 바꿀 것이 없는 module 에는 reference 를 붙이지 않는다. 붙이면 project 의 모든
                // assembly 에 쓸모없는 `UnityPlayMcp.Runtime` reference 가 생긴다.
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
        /// `CompiledAssemblyResolver` 는 `compiledAssembly.References` 의 파일 이름만 보고 version 을
        /// 무시하므로 (`CompiledAssemblyResolver.cs:33`), 이름만 담은 임시 reference 로 resolve 가 된다.
        /// 이 임시 reference 는 묻기 위한 것이라 module 에 넣지 않는다.
        ///
        /// resolve 실패를 잡지 않는 이유: `WillProcess` 가 `compiledAssembly.References` 에 runtime dll 이
        /// 있을 때만 `Process` 를 부르고 (`InputCallILPostProcessor.cs:24`), 그 목록이 그대로 resolver 로
        /// 들어간다. 닿을 수 없는 경로다.
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
        /// Cecil 0.11.4 의 importer 도 `ImportReference` 안에서 같은 reference 를 만들어 넣는다. 그래도
        /// 여기서 명시적으로 붙이는 이유는 두 가지다. 첫째, "바꿀 것이 있을 때만 reference 가 생긴다" 는
        /// 이 weaver 의 규칙이 code 에 보인다 — importer 의 내부 동작에 기대면 보이지 않는다. 둘째,
        /// importer 가 그 동작을 바꾸면 weaving 이 조용히 깨지는데, 그것이 바로 이 결함의 모양이다.
        ///
        /// 중복은 생기지 않는다. importer 는 `FullName` 으로 기존 reference 를 찾아 재사용하고,
        /// `FullName` 을 이루는 `Name`/`Version`/`Culture`/`PublicKeyToken` 을 resolve 된 정의에서 그대로
        /// 베끼므로 importer 가 만들 값과 같다.
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
