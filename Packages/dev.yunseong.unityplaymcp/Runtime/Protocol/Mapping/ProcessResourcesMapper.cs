using Artel.Diagnostics;
using Artel.Protocol.Dto;

namespace Artel.Protocol.Mapping
{
    /// <summary>
    /// 프로세스 사용량을 전송용 DTO로 옮긴다. 바이트와 비율은 그대로 가고, 초를 밀리초로 바꾸는
    /// 지점만 여기 한 곳으로 모은다.
    /// </summary>
    internal static class ProcessResourcesMapper
    {
        private const float MillisecondsPerSecond = 1000f;

        public static ProcessResourcesDto ToDto(ProcessResourceUsage usage)
        {
            return new ProcessResourcesDto
            {
                CpuPercent = usage.CpuPercent,
                WorkingSetBytes = usage.WorkingSetBytes,
                PrivateBytes = usage.PrivateBytes,
                ManagedHeapBytes = usage.ManagedHeapBytes,
                Gen0Collections = usage.Gen0Collections,
                Gen1Collections = usage.Gen1Collections,
                Gen2Collections = usage.Gen2Collections,
                SampledMs = usage.SampledSeconds * MillisecondsPerSecond
            };
        }
    }
}
