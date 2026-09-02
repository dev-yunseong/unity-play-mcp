using System.IO;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// 설정 파일을 읽고 쓴다. 이 둘 말고 다른 파일 시스템 관심사를 여기 두지 않는다.
    /// </summary>
    internal static class McpConfigFileStore
    {
        /// <summary>없는 파일은 빈 텍스트다. 형식 변환이 빈 텍스트에서 새 설정을 만들 줄 안다.</summary>
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
