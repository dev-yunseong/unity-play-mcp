using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Artel.CodeGen
{
    internal sealed class InputMethodWeaver
    {
        private const string RuntimeAssemblyName = "Artel.Runtime";
        private const string UnityInputTypeName = "UnityEngine.Input";
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

        private readonly ModuleDefinition module;
        private readonly Dictionary<string, MethodReference> proxyMethods;

        /// <summary>
        /// 대상 어셈블리가 런타임을 실제로 참조할 때만 위버를 만든다. 참조가 없으면 null.
        /// 이유는 <see cref="ActionMethodWeaver.TryCreate"/>와 같다 — 컴파일러 참조 목록과
        /// IL 메타데이터 참조 목록이 다르다.
        /// </summary>
        public static InputMethodWeaver TryCreate(ModuleDefinition module)
        {
            var runtimeReference = module.AssemblyReferences
                .FirstOrDefault(reference => reference.Name == RuntimeAssemblyName);

            return runtimeReference == null ? null : new InputMethodWeaver(module, runtimeReference);
        }

        private InputMethodWeaver(ModuleDefinition module, AssemblyNameReference runtimeReference)
        {
            this.module = module;
            var runtimeModule = module.AssemblyResolver.Resolve(runtimeReference).MainModule;
            var proxyType = runtimeModule.GetType("Artel.ArtelInput");

            proxyMethods = proxyType.Methods
                .Where(method => SupportedMethodNames.Contains(method.Name))
                .ToDictionary(GetSignature, method => module.ImportReference(method));
        }

        public bool Process()
        {
            var changed = false;
            foreach (var method in module.Types
                         .SelectMany(SelfAndNestedTypes)
                         .SelectMany(type => type.Methods)
                         .Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (!(instruction.Operand is MethodReference calledMethod) ||
                        calledMethod.DeclaringType.FullName != UnityInputTypeName ||
                        !proxyMethods.TryGetValue(GetSignature(calledMethod), out var proxyMethod))
                    {
                        continue;
                    }

                    instruction.Operand = proxyMethod;
                    changed = true;
                }
            }

            return changed;
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
