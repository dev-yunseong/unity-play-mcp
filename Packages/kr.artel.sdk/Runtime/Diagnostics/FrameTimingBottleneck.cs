namespace Artel.Diagnostics
{
    /// <summary>
    /// 프레임을 가장 오래 붙잡은 구간. 메인 스레드·렌더 스레드·GPU 중 무엇이 가장 길었는지로 정한다.
    ///
    /// 수집된 값끼리만 비교한다. GPU 타이밍이 없는 환경에서는 CPU 쪽 두 값만 겨루므로, GPU가 진짜
    /// 병목이어도 이 분류는 CPU를 가리킨다. 소비자는 <c>gpuMs</c>가 빠진 보고에서 그 사실을 읽어야 한다.
    /// </summary>
    internal enum FrameTimingBottleneck
    {
        /// <summary>비교할 값이 하나도 양수로 오지 않았다.</summary>
        Unknown = 0,

        MainThread,
        RenderThread,
        Gpu,

        /// <summary>
        /// 1등이 2등을 의미 있는 차이로 앞서지 못했다. 16.0ms와 15.9ms를 두고 병목을 단정하면
        /// 보고마다 이름이 뒤집혀 아무 정보도 되지 않는다.
        /// </summary>
        Balanced
    }
}
