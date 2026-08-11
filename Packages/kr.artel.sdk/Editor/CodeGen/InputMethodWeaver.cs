using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Artel.CodeGen
{
    internal sealed class InputMethodWeaver
    {
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

        public InputMethodWeaver(ModuleDefinition module)
        {
            this.module = module;
            var runtimeReference = module.AssemblyReferences
                .First(reference => reference.Name == "Artel.Runtime");
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
