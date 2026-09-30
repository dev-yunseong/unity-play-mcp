using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp.Protocol.Mapping
{
    /// <summary>
    /// 프로세스 사용량을 전송용 DTO 로 옮기며 초를 밀리초로 바꾼다.
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
