using System;
using System.Collections.Generic;
using System.Text;

namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// Codex 의 <c>~/.codex/config.toml</c>. server 하나가 <c>[mcp_servers.&lt;name&gt;]</c> table 하나다.
    /// </summary>
    /// <remarks>
    /// TOML parser 를 들이지 않고 줄 단위로 다룬다. 이 파일에는 trust level 이나 모델 설정처럼 우리와 무관한
    /// table 이 잔뜩 들어 있고, 그것들을 parse 했다가 다시 쓰면 주석과 줄 순서가 통째로 바뀐다. 우리 block 만
    /// 갈아 끼우고 나머지 줄은 손대지 않는다.
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
            // 있던 block 을 지우고 파일 끝에 다시 쓴다. TOML 은 root level key 가 첫 table header 앞에만 올 수
            // 있으므로, table 을 맨 뒤에 붙이는 것은 어떤 파일에서도 안전하다.
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

            // block 을 떼어 낸 자리에 빈 줄만 남지 않도록 뒤따르는 빈 줄까지 같이 가져간다.
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
        /// block 을 끝내는 줄은 <c>[</c> 로 시작하되 우리 sub-table 은 아닌 줄이다. Codex 는 환경 변수를
        /// <c>[mcp_servers.unity-play.env]</c> 라는 sub-table 로 적으라고 안내하는데, 그것을 다음 table 로 보고
        /// 끊으면 지운 뒤에 <c>command</c> 없는 <c>env</c> table 만 남아 server 정의가 깨진 채 살아남는다.
        ///
        /// 접두사에 마침표가 붙어 있으므로 이웃한 <c>[mcp_servers.unity-play-extra]</c> 는 그대로 block 을 끝낸다.
        /// </remarks>
        private static bool TryFindBlock(IReadOnlyList<string> lines, string serverName, out int start, out int end)
        {
            // TOML 은 bare key 와 따옴표 낀 key 를 같은 이름으로 본다. 한쪽만 알아보면 다른 쪽으로 적힌 파일에
            // table 을 하나 더 붙이게 되고, table 중복 정의는 parse error 라 Codex 가 설정 전체를 읽지 못한다.
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

            // 다음 table 바로 위의 주석과 빈 줄은 그 table 을 설명하려고 적은 것이다. 우리 block 에 넣어 두면
            // 지울 때 남의 주석까지 사라진다. 우리 것을 설명하던 주석이 고아로 남는 편이 낫다.
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

        /// <summary>TOML basic string 으로 감싼다. Windows 경로의 backslash 가 escape 없이 들어가면 뜻이 바뀐다.</summary>
        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static List<string> ReadLines(string text)
        {
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));

            // 마지막 개행 뒤의 빈 조각은 줄이 아니다. 그대로 두면 쓸 때마다 파일 끝에 빈 줄이 하나씩 늘어난다.
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

        /// <summary>Windows 에서 쓰인 파일을 LF 로 바꿔 놓으면 손대지 않은 줄까지 전부 바뀐 것으로 보인다.</summary>
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
