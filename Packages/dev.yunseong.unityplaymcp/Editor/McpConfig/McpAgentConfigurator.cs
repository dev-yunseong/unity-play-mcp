using System;

namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// 고른 agent 의 설정 파일 하나를 읽고, 형식에 맞게 고치고, 다시 쓴다.
    /// </summary>
    /// <remarks>
    /// 화면이 <see cref="IMcpConfigFormat"/> 과 <see cref="McpConfigFileStore"/> 를 직접 짝지으면, 어느 scope 의
    /// 파일을 건드리는지가 화면 코드에 흩어진다. 여기를 거치면 건드리는 파일은 언제나
    /// <see cref="McpAgent.ConfigPath"/> 하나뿐이고, 그 자리는 catalog 가 scope 를 보고 이미 정해 두었다.
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

            // 적혀 있지 않으면 쓰지 않는다. 그대로 쓰면 없던 파일이 새로 생기거나, 손대지 않아도 될 파일의
            // 서식이 형식 변환을 거치며 바뀐다.
            if (!agent.Format.Contains(text, serverName))
            {
                return;
            }

            McpConfigFileStore.Write(agent.ConfigPath, agent.Format.Remove(text, serverName));
        }
    }
}
