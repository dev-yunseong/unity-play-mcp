using System;
using System.Collections.Generic;
using System.Text;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// Codex 의 <c>~/.codex/config.toml</c>. server 하나가 <c>[mcp_servers.&lt;name&gt;]</c> table 하나다.
    /// </summary>
    /// <remarks>
    /// TOML parser 없이 줄 단위로 다룬다. parse 후 다시 쓰면 무관한 table 의 주석과 줄 순서가 바뀌므로
    /// server block 만 교체하고 나머지 줄은 그대로 둔다.
    /// </remarks>
    internal sealed class TomlMcpConfigFormat : IMcpConfigFormat
    {
        private const string TableRoot = "mcp_servers";

        public bool Contains(string text, string serverName)
        {
            return TryFindBlock(ReadLines(text), serverName, out _, out _);
        }

        public string Add(string text, string serverName, McpServerEntry entry)
        {
            // root level key 는 첫 table header 앞에만 올 수 있으므로 table 을 파일 끝에 붙여도 안전하다.
            var withoutExisting = Remove(text, serverName);
            var block = Describe(serverName, entry, NewlineOf(text));

            if (withoutExisting.Length == 0)
            {
                return block;
            }

            return withoutExisting + NewlineOf(text) + block;
        }

        public string Remove(string text, string serverName)
        {
            var lines = ReadLines(text);

            if (!TryFindBlock(lines, serverName, out var start, out var end))
            {
                return text;
            }

            // 빈 줄이 쌓이지 않도록 뒤따르는 빈 줄도 함께 지운다.
            while (end < lines.Count && IsBlank(lines[end]))
            {
                end++;
            }

            lines.RemoveRange(start, end - start);

            while (lines.Count > 0 && IsBlank(lines[lines.Count - 1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return Join(lines, NewlineOf(text));
        }

        /// <summary>
        /// header 줄부터 block 을 끝내는 줄 직전까지를 찾는다.
        /// </summary>
        /// <remarks>
        /// block 은 이 server 의 sub-table 이 아닌 <c>[</c> 줄에서 끝난다. <c>[mcp_servers.unity-play.env]</c> 같은
        /// sub-table 을 남기면 <c>command</c> 없는 깨진 server 정의가 된다. 접두사가 마침표로 끝나므로
        /// <c>[mcp_servers.unity-play-extra]</c> 는 block 을 끝낸다.
        /// </remarks>
        private static bool TryFindBlock(IReadOnlyList<string> lines, string serverName, out int start, out int end)
        {
            // bare key 와 quoted key 는 같은 이름이다. 한쪽만 찾으면 table 이 중복 정의되어 Codex 가 설정 전체를
            // 읽지 못한다.
            var headers = new[]
            {
                "[" + TableRoot + "." + serverName + "]",
                "[" + TableRoot + ".\"" + serverName + "\"]",
            };

            var subTablePrefixes = new[]
            {
                "[" + TableRoot + "." + serverName + ".",
                "[" + TableRoot + ".\"" + serverName + "\".",
            };

            start = -1;
            end = -1;

            for (var index = 0; index < lines.Count; index++)
            {
                if (Array.IndexOf(headers, lines[index].Trim()) >= 0)
                {
                    start = index;
                    break;
                }
            }

            if (start < 0)
            {
                return false;
            }

            end = lines.Count;

            for (var index = start + 1; index < lines.Count; index++)
            {
                var line = lines[index].TrimStart();

                if (line.StartsWith("[", StringComparison.Ordinal) && !StartsWithAny(line, subTablePrefixes))
                {
                    end = index;
                    break;
                }
            }

            // 다음 table 바로 위의 주석과 빈 줄은 그 table 의 것으로 보고 block 에서 뺀다.
            while (end > start + 1 && IsBlankOrComment(lines[end - 1]))
            {
                end--;
            }

            return true;
        }

        private static bool StartsWithAny(string line, IReadOnlyList<string> prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(string serverName, McpServerEntry entry, string newline)
        {
            var described = new StringBuilder();

            described.Append("[").Append(TableRoot).Append(".").Append(serverName).Append("]").Append(newline);
            described.Append("command = ").Append(Quote(entry.Command)).Append(newline);
            described.Append("args = [");

            for (var index = 0; index < entry.Arguments.Count; index++)
            {
                if (index > 0)
                {
                    described.Append(", ");
                }

                described.Append(Quote(entry.Arguments[index]));
            }

            described.Append("]").Append(newline);
            return described.ToString();
        }

        /// <summary>TOML basic string 으로 감싼다. Windows 경로의 backslash 는 escape 해야 한다.</summary>
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static List<string> ReadLines(string text)
        {
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));

            // 마지막 개행 뒤 빈 조각을 남기면 쓸 때마다 파일 끝에 빈 줄이 늘어난다.
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return lines;
        }

        private static string Join(IReadOnlyList<string> lines, string newline)
        {
            if (lines.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(newline, lines) + newline;
        }

        /// <summary>기존 개행을 유지해 손대지 않은 줄이 diff 에 나오지 않게 한다.</summary>
        private static string NewlineOf(string text)
        {
            return text.Contains("\r\n") ? "\r\n" : "\n";
        }

        private static bool IsBlank(string line)
        {
            return line.Trim().Length == 0;
        }

        private static bool IsBlankOrComment(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal);
        }
    }
}
