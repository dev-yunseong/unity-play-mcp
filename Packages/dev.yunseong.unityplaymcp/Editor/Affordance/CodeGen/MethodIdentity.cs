using System.Text;
using Mono.Cecil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 어셈블리, 선언 타입, 이름, 시그니처로 메서드를 식별한다. 컴파일 시점 `evidence` 를 이름만으로
    /// 이어 붙이면 overload 가 섞인다.
    /// </summary>
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
