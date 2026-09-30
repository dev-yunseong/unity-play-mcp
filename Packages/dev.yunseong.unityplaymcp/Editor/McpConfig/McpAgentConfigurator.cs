using System;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// agent 의 설정 파일을 읽고, 형식에 맞게 고치고, 다시 쓴다.
    /// </summary>
    /// <remarks>
    /// 화면 코드는 <see cref="IMcpConfigFormat"/> 과 <see cref="McpConfigFileStore"/> 를 직접 쓰지 않는다.
    /// 여기서 건드리는 파일은 catalog 가 scope 로 정한 <see cref="McpAgent.ConfigPath"/> 하나뿐이다.
    /// </remarks>
    internal static class McpAgentConfigurator
    {
        /// <summary>이 이름의 server 가 이 agent 의 설정에 이미 적혀 있는지.</summary>
        internal static bool IsConfigured(McpAgent agent, string serverName)
        {
            if (agent == null)
            {
                throw new ArgumentNullException(nameof(agent));
            }

            return agent.Format.Contains(McpConfigFileStore.Read(agent.ConfigPath), serverName);
        }

        internal static void Add(McpAgent agent, string serverName, McpServerEntry entry)
        {
            if (agent == null)
            {
                throw new ArgumentNullException(nameof(agent));
            }

            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var text = McpConfigFileStore.Read(agent.ConfigPath);

            McpConfigFileStore.Write(agent.ConfigPath, agent.Format.Add(text, serverName, entry));
        }

        internal static void Remove(McpAgent agent, string serverName)
        {
            if (agent == null)
            {
                throw new ArgumentNullException(nameof(agent));
            }

            var text = McpConfigFileStore.Read(agent.ConfigPath);

            // 없던 파일이 생기거나 형식 변환으로 서식이 바뀌지 않도록, 적혀 있지 않으면 쓰지 않는다.
            if (!agent.Format.Contains(text, serverName))
            {
                return;
            }

            McpConfigFileStore.Write(agent.ConfigPath, agent.Format.Remove(text, serverName));
        }
    }
}
