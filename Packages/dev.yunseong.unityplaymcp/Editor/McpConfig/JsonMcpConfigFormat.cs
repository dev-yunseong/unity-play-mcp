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
    /// agent 간 차이는 root key (<c>mcpServers</c> 또는 <c>servers</c>) 와 <c>type: stdio</c> 기록 여부뿐이라
    /// 생성자로 받는다.
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
            // 쓰기와 같은 기준으로 검사한다. 고칠 수 없는 파일이 "Not configured" 로 보이지 않게 한다.
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

            // VS Code 만 이 field 로 transport 를 고른다.
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
        /// object 가 아닌 값이 있으면 덮어써서 사용자 값을 잃지 않도록 예외를 던진다.
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
        /// VS Code 의 .vscode/mcp.json 은 주석을 허용한다. Newtonsoft <c>JObject</c> 는 <c>CommentHandling.Load</c>
        /// 로 읽어도 object 안의 주석을 보존하지 못하므로, 다시 쓰면 주석이 지워진다.
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
        /// <c>JObject.ToString</c> 은 <c>Environment.NewLine</c> 을 쓰므로 개행이 섞일 수 있다. 파일의 기존 개행으로 통일한다.
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
