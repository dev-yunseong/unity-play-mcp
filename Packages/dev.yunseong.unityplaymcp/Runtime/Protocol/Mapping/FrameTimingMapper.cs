using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp.Protocol.Mapping
{
    /// <summary>
    /// CPU·GPU 분해를 전송용 DTO 로 옮긴다. 시간은 밀리초 그대로 두고 병목 분류만 문자열로 바꾼다.
    /// </summary>
    internal static class FrameTimingMapper
    {
        public static FrameTimingDto ToDto(FrameTimingBreakdown breakdown)
        {
            return new FrameTimingDto
            {
                FrameCount = breakdown.FrameCount,
                CpuMs = breakdown.CpuMs,
                CpuMainThreadMs = breakdown.CpuMainThreadMs,
                CpuRenderThreadMs = breakdown.CpuRenderThreadMs,
                GpuMs = breakdown.GpuMs,
                Bottleneck = ToWireValue(breakdown.Bottleneck)
            };
        }

        /// <summary>
        /// enum 이름을 그대로 쓰지 않는다. 표기가 다른 필드와 어긋나고, 이름을 바꾸면 전송 계약이 깨진다.
        /// </summary>
        private static string ToWireValue(FrameTimingBottleneck bottleneck)
        {
            switch (bottleneck)
            {
                case FrameTimingBottleneck.MainThread:
                    return "mainThread";
                case FrameTimingBottleneck.RenderThread:
                    return "renderThread";
                case FrameTimingBottleneck.Gpu:
                    return "gpu";
                case FrameTimingBottleneck.Balanced:
                    return "balanced";
                default:
                    return "unknown";
            }
        }
    }
}
