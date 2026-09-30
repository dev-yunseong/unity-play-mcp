namespace UnityPlayMcp
{
    /// <summary>
    /// executor 가 live reading 을 켜고 끄는 interface 다.
    /// </summary>
    /// <remarks>
    /// 테스트가 channel 없이 executor 를 만들 수 있도록 manager 와 분리한다.
    /// </remarks>
    internal interface IReadingChannel
    {
        /// <summary>reading 을 시작한다. 실패하면 이유를 보고한다. 호출 뒤 돌고 있으면 true 다.</summary>
        bool StartReadings();

        /// <summary>reading 을 끝낸다. 시작한 적이 없어도 안전하다.</summary>
        void StopReadings();
    }
}
