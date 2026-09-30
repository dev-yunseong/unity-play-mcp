using System;
using System.IO;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 에디터가 읽을 수 있는 파일로 survey 를 남긴다.
    /// </summary>
    /// <remarks>
    /// <see cref="Unity.CompilationPipeline.Common.Diagnostics.DiagnosticType.Warning"/> 는 성공한 빌드 단계의
    /// 출력에 묻혀 콘솔에 보이지 않는다. 그래서 파일을 쓰고 리로드 뒤 에디터 스크립트가 알린다.
    /// post-processor 도 프로젝트 루트를 작업 디렉터리로 쓰므로 상대 경로가 양쪽에서 같다.
    ///
    /// 어셈블리는 동시에 post-process 되므로 공유 파일 대신 어셈블리당 파일 하나를 쓴다.
    /// </remarks>
    internal static class ScopeReport
    {
        internal const string ReportDirectory = "Library/UnityPlayMcpScope";

        internal static bool TryWrite(string assemblyName, string message)
        {
            try
            {
                Directory.CreateDirectory(ReportDirectory);
                File.WriteAllText(Path.Combine(ReportDirectory, assemblyName + ".txt"), message);
                return true;
            }
            catch (Exception)
            {
                // diagnostic 은 에디터 로그에 남으므로 파일 쓰기 실패로 컴파일을 실패시키지 않는다.
                return false;
            }
        }
    }
}
