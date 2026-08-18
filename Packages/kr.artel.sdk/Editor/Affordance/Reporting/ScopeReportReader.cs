using System;
using System.IO;
using System.Text;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Artel.Affordances.Editor
{
    /// <summary>
    /// 분석이 무엇을 했는지를, 볼 수 있는 자리인 콘솔에 말한다.
    /// </summary>
    /// <remarks>
    /// 분석은 컴파일 도중 제 프로세스에서 돌고 거기서는 콘솔에 말을 걸 수 없다. 대신 어셈블리마다 파일 하나를 남긴다.
    /// 이것은 컴파일 뒤의 리로드가 에디터를 되돌려 놓으면 그 파일들을 한 번 읽고, 다음 컴파일이 낡은 답을 되풀이하는
    /// 대신 아무것도 없는 데서 시작하도록 그것들을 지운다.
    ///
    /// 여기서 소리 내어 말하는 일은 보이는 것보다 중요하다. 이 패키지의 이전 빌드에는 조용히 아무것도 하지 않는 분석이
    /// 있었고, 뒤이은 스캔은 커버리지 공백이 없다고 보고했다 — 그것은 깨끗한 결과로 읽혔지만 실은 결과라는 것이 아예
    /// 없었던 것이다.
    /// </remarks>
    internal static class ScopeReportReader
    {
        private const string ReportDirectory = "Library/ArtelScope";

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

            var summary = new StringBuilder("[Artel] Scope survey");

            foreach (var report in reports)
            {
                try
                {
                    summary.Append('\n').Append(File.ReadAllText(report).TrimEnd());
                    File.Delete(report);
                }
                catch (Exception)
                {
                    // 읽을 수 없는 리포트는 셈에 들지 못한 어셈블리 하나이지, 나머지를 버릴 이유가 아니다.
                    summary.Append('\n').Append(Path.GetFileNameWithoutExtension(report))
                        .Append(": report could not be read.");
                }
            }

            Debug.Log(summary.ToString());
        }
    }
}
