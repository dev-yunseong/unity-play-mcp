namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// 메인 스레드·렌더 스레드·GPU 중 가장 오래 걸린 구간.
    ///
    /// 수집된 값끼리만 비교하므로 GPU 타이밍이 없으면 GPU 가 병목이어도 CPU 를 가리킨다.
    /// 이 경우 보고에 <c>gpuMs</c> 가 빠져 있다.
    /// </summary>
    internal enum FrameTimingBottleneck
    {
        /// <summary>비교할 값이 하나도 양수로 오지 않았다.</summary>
        Unknown = 0,

        MainThread,
        RenderThread,
        Gpu,

        /// <summary>
        /// 1등과 2등의 차이가 작다. 단정하면 보고마다 병목이 뒤집힌다.
        /// </summary>
        Balanced
    }
}
