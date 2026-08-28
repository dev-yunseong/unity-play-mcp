using Artel.Diagnostics;
using Artel.Protocol.Dto;

namespace Artel.Protocol.Mapping
{
    /// <summary>
    /// CPU·GPU 분해를 전송용 DTO로 옮긴다. 시간 값은 Unity가 준 밀리초 그대로 가고, 병목 분류를
    /// 전송 문자열로 바꾸는 지점만 여기 한 곳으로 모은다.
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
        /// enum 이름을 그대로 흘리지 않는다. C#의 대문자 시작 이름은 나머지 필드의 표기와 어긋나고,
        /// 열거자 이름을 바꾸는 순간 전송 계약이 조용히 깨진다.
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
