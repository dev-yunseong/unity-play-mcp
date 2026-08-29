using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 에디터 Game view의 렌더 통계. Game view Stats 창이 보여 주는 것과 같은 값이다.
    /// </summary>
    /// <remarks>
    /// **Standalone 수치와 같은 차트에 올리면 안 된다.** 에디터가 렌더한 값이라 Game view만이 아니라
    /// Scene view와 Inspector 프리뷰의 드로우 콜·삼각형이 함께 잡힌다. Scene view 창을 닫았는지에
    /// 따라 같은 씬에서도 값이 달라진다. 빌드에서 잰 렌더 비용과는 다른 계열의 지표로 다뤄야 한다.
    ///
    /// **구간 평균이 아니라 순간값이다.** <c>frameTimes</c>는 1초 창의 모든 프레임을 접은 분포지만,
    /// 이 블록은 보고를 만드는 그 프레임 하나를 읽은 스냅샷이다. 초당 한 번 뽑은 표본이라
    /// 튀는 프레임을 놓치며, 두 블록을 같은 창의 값으로 나란히 읽으면 해석이 어긋난다.
    ///
    /// 에디터에서만 채워진다. 플레이어 빌드에서는 <c>UnityEditor</c>를 참조할 수 없어 필드 자체가
    /// 빠진다. 0을 채워 보내면 "에디터가 아니라 못 쟀다"와 "아무것도 안 그렸다"가 구분되지 않는다.
    ///
    /// 시간 값의 단위는 밀리초다. <c>frameTimes</c>와 같은 눈금을 쓴다.
    /// </remarks>
    public sealed class EditorRenderStatsDto
    {
        [JsonProperty("drawCalls")]
        public int DrawCalls { get; set; }

        /// <summary>배칭 뒤 남은 배치 수. 드로우 콜과의 차이가 배칭이 실제로 먹은 양이다.</summary>
        [JsonProperty("batches")]
        public int Batches { get; set; }

        /// <summary>
        /// 셰이더 패스 전환 횟수. 드로우 콜 수보다 이쪽이 렌더 비용을 더 잘 설명하는 경우가 많다.
        /// </summary>
        [JsonProperty("setPassCalls")]
        public int SetPassCalls { get; set; }

        [JsonProperty("triangles")]
        public int Triangles { get; set; }

        [JsonProperty("vertices")]
        public int Vertices { get; set; }

        /// <summary>
        /// 메인 스레드 프레임 시간. Stats 창의 <c>CPU: main</c>에 해당한다.
        /// 최상위 <c>frameTimes</c>와 달리 평균이 아닌 한 프레임의 값이다.
        /// </summary>
        [JsonProperty("mainThreadMs")]
        public float MainThreadMs { get; set; }

        /// <summary>렌더 스레드 프레임 시간. Stats 창의 <c>render thread</c>에 해당한다.</summary>
        [JsonProperty("renderThreadMs")]
        public float RenderThreadMs { get; set; }
    }
}
