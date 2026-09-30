using System;
using UnityEditor;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 고른 <see cref="McpConfigScope"/> 를 Unity project 별로 기억한다.
    /// </summary>
    /// <remarks>
    /// <c>EditorPrefs</c> 는 한 기계의 모든 project 가 공유하므로 key 에 project 경로를 넣는다.
    /// key 의 <c>v1</c> 은 저장 형식을 바꿀 때 옛 값을 버리기 위한 것이다.
    /// </remarks>
    internal static class McpConfigScopePreference
    {
        private const string KeyPrefix = "dev.yunseong.unityplaymcp.v1.mcpConfigScope.";

        /// <summary>저장된 값이 없거나 알 수 없는 값일 때 쓰는 scope.</summary>
        internal const McpConfigScope Default = McpConfigScope.Project;

        /// <summary>
        /// 이 project 의 저장 key.
        /// </summary>
        /// <remarks>
        /// 같은 project 가 <c>/repo</c>, <c>/repo/</c>, <c>\repo</c> 로 들어와도 같은 key 가 되도록 정규화한다.
        /// </remarks>
        internal static string KeyFor(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("The Unity project directory is required.", nameof(projectRoot));
            }

            return KeyPrefix + projectRoot.Replace('\\', '/').TrimEnd('/');
        }

        internal static McpConfigScope Read(string projectRoot)
        {
            var stored = EditorPrefs.GetString(KeyFor(projectRoot), string.Empty);

            // enum 순서가 바뀌어도 뜻이 유지되도록 숫자가 아니라 이름으로 저장한다.
            return Enum.TryParse(stored, out McpConfigScope scope) && Enum.IsDefined(typeof(McpConfigScope), scope)
                ? scope
                : Default;
        }

        internal static void Write(string projectRoot, McpConfigScope scope)
        {
            EditorPrefs.SetString(KeyFor(projectRoot), scope.ToString());
        }
    }
}
