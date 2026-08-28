using System;
using System.IO;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 에디터가 집어 갈 수 있는 자리에 survey 를 남긴다.
    /// </summary>
    /// <remarks>
    /// 여기서 올린 <see cref="Unity.CompilationPipeline.Common.Diagnostics.DiagnosticType.Warning"/> 는
    /// 콘솔에 닿지 않는다. 그것은 post-processor 를 돌린 빌드 단계의 출력 안에 찍히는데, 성공한 단계는
    /// 출력이 접혀 버린다 — 드러나는 것은 단계를 실패시키고 빌드까지 데려가는 error 뿐이다. 그래서 컴파일
    /// 파이프라인 안에서 쓸 수 있는 유일한 채널이 아무도 읽지 않는 채널이다.
    ///
    /// 파일을 쓰고 리로드 뒤에 에디터 쪽 스크립트가 알리게 하는 것이 그 출구다. post-processor 는
    /// 프로젝트 루트를 작업 디렉터리로 삼아 제 프로세스에서 돌고, 그래서 아래의 상대 경로가 양쪽에서 같은
    /// 곳을 가리킨다.
    ///
    /// 어셈블리당 파일 하나다. 어셈블리들은 동시에 post-process 되므로 공유 파일에 덧붙이면 서로 엉킨다.
    /// </remarks>
    internal static class ScopeReport
    {
        internal const string ReportDirectory = "Library/ArtelScope";

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
                // diagnostic 자체는 여전히 에디터 로그로 간다. 읽을 수 있는 채널을 잃는 일은 소리 내어 말할 값은
                // 있어도 컴파일을 실패시킬 값은 없다.
                return false;
            }
        }
    }
}
