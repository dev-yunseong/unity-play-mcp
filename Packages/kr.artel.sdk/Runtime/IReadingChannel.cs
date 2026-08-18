namespace Artel
{
    /// <summary>
    /// 실행을 모는 쪽을 위해 라이브 판독을 켜고 끄는 일.
    /// </summary>
    /// <remarks>
    /// 매니저 자체가 아니라 이음매다. executor 가 하는 일 전부는 게임에 무언가를 하는 것이고 이것은 SDK 에 하는 유일한
    /// 것이다 — 따로 이름 붙이는 것이 그것을 보이게 하고, 테스트가 채널 없이 executor 를 만들 수 있게 한다.
    /// </remarks>
    internal interface IReadingChannel
    {
        /// <summary>판독을 시작하거나, 왜 안 되는지 말한다. 그 뒤에 돌고 있으면 참.</summary>
        bool StartReadings();

        /// <summary>그것들을 끝낸다. 한 번도 시작하지 않았을 때도 안전하다.</summary>
        void StopReadings();
    }
}
