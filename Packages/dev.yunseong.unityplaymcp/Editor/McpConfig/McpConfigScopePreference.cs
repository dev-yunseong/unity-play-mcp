using System;
using UnityEditor;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 고른 <see cref="McpConfigScope"/> 를 Unity project 별로 기억한다.
    /// </summary>
    /// <remarks>
    /// <c>EditorPrefs</c> 는 한 기계의 모든 project 가 함께 쓰므로 key 에 project 경로를 넣는다. 넣지 않으면
    /// 한 project 에서 <c>User</c> 로 바꾼 것이 다른 project 의 화면에도 적용된다. key 앞의 <c>v1</c> 은 저장
    /// 형식을 바꿔야 할 때 옛 값을 읽지 않고 버리기 위한 것이다.
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
        /// 같은 project 를 <c>/repo</c> 로도 <c>/repo/</c> 로도 <c>\repo</c> 로도 받을 수 있다. 그대로 key 에
        /// 넣으면 같은 project 가 서로 다른 값을 갖는다.
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

            // 이름으로 저장한다. 숫자로 저장하면 나중에 enum 순서를 바꿀 때 저장된 값의 뜻이 조용히 달라진다.
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
