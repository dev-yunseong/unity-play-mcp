using System.Text;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>맨 메서드 이름에 기대지 않고 컴파일 시점 근거를 이어 붙일 만큼은 안정적이다.</summary>
    internal static class MethodIdentity
    {
        internal static string Of(MethodReference method)
        {
            if (method == null)
            {
                return null;
            }

            var text = new StringBuilder();
            text.Append(AssemblyName(method)).Append('|');
            text.Append(method.DeclaringType?.FullName).Append('|');
            text.Append(method.Name).Append('|');
            text.Append(method.ReturnType?.FullName).Append('(');

            for (var index = 0; index < method.Parameters.Count; index++)
            {
                if (index > 0) text.Append(',');
                text.Append(method.Parameters[index].ParameterType.FullName);
            }

            return text.Append(')').ToString();
        }

        private static string AssemblyName(MethodReference method)
        {
            var definition = method as MethodDefinition;
            if (definition?.Module?.Assembly?.Name != null)
            {
                return definition.Module.Assembly.Name.Name;
            }

            var scope = method.DeclaringType?.Scope;
            if (scope is AssemblyNameReference assembly)
            {
                return assembly.Name;
            }

            if (scope is ModuleDefinition module && module.Assembly?.Name != null)
            {
                return module.Assembly.Name.Name;
            }

            return scope?.Name ?? "(unknown-assembly)";
        }
    }
}
