using System;
using System.IO;
using System.Text;
using UnityEditor.Callbacks;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Editor
{
    /// <summary>
    /// 컴파일 중 분석 결과를 console 에 출력한다.
    /// </summary>
    /// <remarks>
    /// 분석은 별도 process 에서 돌아 console 에 쓸 수 없으므로 assembly 마다 파일을 남긴다. reload 뒤 한 번 읽고
    /// 지워서 다음 컴파일이 오래된 결과를 되풀이하지 않게 한다. 출력이 없으면 분석이 돌지 않은 것과 공백 없는
    /// 결과를 구분할 수 없다.
    /// </remarks>
    internal static class ScopeReportReader
    {
        private const string ReportDirectory = "Library/UnityPlayMcpScope";

        [DidReloadScripts]
        private static void Surface()
        {
            string[] reports;

            try
            {
                var directory = Path.Combine(Directory.GetCurrentDirectory(), ReportDirectory);
                if (!Directory.Exists(directory))
                {
                    return;
                }

                reports = Directory.GetFiles(directory, "*.txt");
            }
            catch (Exception)
            {
                return;
            }

            if (reports.Length == 0)
            {
                return;
            }

            var summary = new StringBuilder("[Unity Play MCP] Scope survey");

            foreach (var report in reports)
            {
                try
                {
                    summary.Append('\n').Append(File.ReadAllText(report).TrimEnd());
                    File.Delete(report);
                }
                catch (Exception)
                {
                    // 읽지 못한 리포트가 있어도 나머지는 출력한다.
                    summary.Append('\n').Append(Path.GetFileNameWithoutExtension(report))
                        .Append(": report could not be read.");
                }
            }

            Debug.Log(summary.ToString());
        }
    }
}
