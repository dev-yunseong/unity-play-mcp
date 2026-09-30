using System.IO;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 설정 파일을 읽고 쓴다.
    /// </summary>
    internal static class McpConfigFileStore
    {
        /// <summary>없는 파일은 빈 텍스트로 읽는다. 형식 변환은 빈 텍스트에서 새 설정을 만든다.</summary>
        internal static string Read(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        internal static void Write(string path, string text)
        {
            // .cursor 나 .vscode 는 아직 없을 수 있다.
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, text);
        }
    }
}
