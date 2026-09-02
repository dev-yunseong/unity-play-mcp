using System;
using System.Collections.Generic;

namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// 설정 파일에 써 넣을 MCP server 하나를 어떻게 실행하는지.
    /// </summary>
    /// <remarks>
    /// 네 agent 의 설정 형식이 저마다 다르지만 담기는 값은 이 두 개뿐이다. VS Code 가 요구하는
    /// <c>type: stdio</c> 는 그 형식의 문제라 이 DTO 가 아니라 <see cref="JsonMcpConfigFormat"/> 이 붙인다.
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

        /// <summary>설정 창에 보여 줄 이 실행 방법을 고른 이유. 설정 파일에는 쓰지 않는다.</summary>
        internal string Reason { get; }
    }
}
