using System;
using System.Collections.Generic;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 설정 파일에 쓸 MCP server 실행 방법.
    /// </summary>
    /// <remarks>
    /// VS Code 가 요구하는 <c>type: stdio</c> 는 형식에 속하므로 <see cref="JsonMcpConfigFormat"/> 이 붙인다.
    /// </remarks>
    internal sealed class McpServerEntry
    {
        internal McpServerEntry(string command, IReadOnlyList<string> arguments, string reason = null)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
            Reason = reason;
        }

        internal string Command { get; }

        internal IReadOnlyList<string> Arguments { get; }

        /// <summary>설정 창에 보여 줄, 이 실행 방법을 고른 이유. 설정 파일에는 쓰지 않는다.</summary>
        internal string Reason { get; }
    }
}
