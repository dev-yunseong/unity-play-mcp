using System;
using System.Text.RegularExpressions;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>생성기가 쓰는 exact npm pin만 자동으로 변경한다. wrapper와 사용자 인자는 추측하지 않는다.</summary>
    internal static class McpCliVersion
    {
        private const string Prefix = "unity-play-mcp@";
        private static readonly Regex Version = new Regex(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$");

        internal static bool IsExact(string version) => version != null && Version.IsMatch(version);

        internal static string ConfiguredVersion(McpServerEntry entry)
        {
            if (entry == null || entry.Command != "npx" || entry.Arguments.Count != 2
                || entry.Arguments[0] != "-y" || !entry.Arguments[1].StartsWith(Prefix, StringComparison.Ordinal))
                return null;
            var version = entry.Arguments[1].Substring(Prefix.Length);
            return IsExact(version) ? version : null;
        }

        internal static McpServerEntry Pinned(string version)
        {
            if (!IsExact(version)) throw new InvalidOperationException("The SDK compatible CLI version is invalid.");
            return new McpServerEntry("npx", new[] { "-y", Prefix + version });
        }
    }
}
