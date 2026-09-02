using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// server 목록을 object 하나에 담는 JSON 설정 파일. Claude Code, Cursor, VS Code 가 여기 해당한다.
    /// </summary>
    /// <remarks>
    /// 세 agent 의 차이는 두 가지뿐이라 클래스를 셋으로 나누지 않고 생성자로 받는다: 목록이 놓이는 root key
    /// (<c>mcpServers</c> 또는 <c>servers</c>) 와, entry 에 <c>type: stdio</c> 를 적는지 여부.
    /// </remarks>
    internal sealed class JsonMcpConfigFormat : IMcpConfigFormat
    {
        private readonly string _rootKey;
        private readonly bool _writesTransportType;

        internal JsonMcpConfigFormat(string rootKey, bool writesTransportType)
        {
            _rootKey = rootKey;
            _writesTransportType = writesTransportType;
        }

        public bool Contains(string text, string serverName)
        {
            // 쓰기와 같은 눈으로 읽는다. 여기서만 너그러우면 우리가 고칠 수 없는 파일이 "Not configured" 로
            // 보이고, 버튼을 눌러야 비로소 못 고친다는 것을 알게 된다.
            var servers = ServerList(Parse(text));
            return servers != null && servers[serverName] != null;
        }

        public string Add(string text, string serverName, McpServerEntry entry)
        {
            RefuseComments(text);
            var root = Parse(text);
            var servers = ServerList(root);

            if (servers == null)
            {
                servers = new JObject();
                root[_rootKey] = servers;
            }

            servers[serverName] = Describe(entry);
            return Serialize(root, NewlineOf(text));
        }

        public string Remove(string text, string serverName)
        {
            RefuseComments(text);
            var root = Parse(text);
            var servers = ServerList(root);

            if (servers != null)
            {
                servers.Remove(serverName);
            }

            return Serialize(root, NewlineOf(text));
        }

        private JObject Describe(McpServerEntry entry)
        {
            var described = new JObject();

            // VS Code 는 이 field 로 transport 를 고르고, 나머지 둘은 이 key 를 모른다.
            if (_writesTransportType)
            {
                described["type"] = "stdio";
            }

            described["command"] = entry.Command;

            var arguments = new JArray();
            foreach (var argument in entry.Arguments)
            {
                arguments.Add(argument);
            }

            described["args"] = arguments;
            return described;
        }

        /// <summary>
        /// server 목록. 아직 없으면 <c>null</c>.
        /// </summary>
        /// <remarks>
        /// 이 자리에 object 가 아닌 값이 앉아 있으면 우리가 아는 형식의 파일이 아니다. 조용히 갈아 끼우면
        /// 사용자가 적어 둔 값이 사라지므로, 주석을 만났을 때와 같이 멈춘다.
        /// </remarks>
        private JObject ServerList(JObject root)
        {
            var existing = root[_rootKey];

            if (existing == null || existing.Type == JTokenType.Null)
            {
                return null;
            }

            if (!(existing is JObject servers))
            {
                throw new InvalidOperationException(
                    "\"" + _rootKey + "\" in this file is not an object, so this is not a shape this page " +
                    "knows how to edit. Add or remove the unity-play server in this file by hand.");
            }

            return servers;
        }

        private static JObject Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new JObject();
            }

            return JObject.Parse(text);
        }

        /// <summary>
        /// 주석이 있는 파일은 건드리지 않는다.
        /// </summary>
        /// <remarks>
        /// VS Code 의 .vscode/mcp.json 은 주석을 허용하고 사람들이 실제로 쓴다. 그런데 Newtonsoft 의
        /// <c>JObject</c> 는 property 만 자식으로 받으므로, <c>CommentHandling.Load</c> 로 읽어도 object 안의
        /// 주석은 담기지 못하고 사라진다. 읽어서 다시 쓰면 사용자가 적어 둔 주석이 조용히 지워진다는 뜻이다.
        /// 그래서 여기서 멈추고, 손으로 고치라고 화면에 말한다.
        /// </remarks>
        private static void RefuseComments(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            using (var reader = new JsonTextReader(new StringReader(text)))
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.Comment)
                    {
                        throw new InvalidOperationException(
                            "This file has comments in it, and rewriting it would delete them. " +
                            "Add or remove the unity-play server in this file by hand.");
                    }
                }
            }
        }

        /// <remarks>
        /// <c>JObject.ToString</c> 은 <c>Environment.NewLine</c> 으로 줄을 바꾼다. Windows 의 Unity 에서 쓰면
        /// 본문은 CRLF 인데 끝에 붙인 개행만 LF 인 파일이 나온다. 파일이 쓰던 개행으로 통일한다.
        /// </remarks>
        private static string Serialize(JObject root, string newline)
        {
            var text = new StringWriter { NewLine = newline };

            using (var writer = new JsonTextWriter(text) { Formatting = Formatting.Indented })
            {
                root.WriteTo(writer);
            }

            return text.ToString() + newline;
        }

        private static string NewlineOf(string text)
        {
            return text.Contains("\r\n") ? "\r\n" : "\n";
        }
    }
}
