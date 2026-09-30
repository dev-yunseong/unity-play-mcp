using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 에디터 Game view의 렌더 통계. Game view Stats 창이 보여 주는 것과 같은 값이다.
    /// </summary>
    /// <remarks>
    /// Scene view·Inspector 프리뷰가 섞인 에디터 값이라 Standalone 수치와 비교하면 안 된다.
    ///
    /// 구간 집계가 아니라 보고를 만드는 프레임 하나의 순간값이다. <c>frameTimes</c> 와 같은 창으로 읽으면 안 된다.
    ///
    /// 플레이어 빌드에서는 필드째 빠진다. 시간 단위는 밀리초다.
    /// </remarks>
    public sealed class EditorRenderStatsDto
    {
        [JsonProperty("drawCalls")]
        public int DrawCalls { get; set; }

        /// <summary>배칭 뒤 남은 배치 수.</summary>
        [JsonProperty("batches")]
        public int Batches { get; set; }

        /// <summary>셰이더 패스 전환 횟수.</summary>
        [JsonProperty("setPassCalls")]
        public int SetPassCalls { get; set; }

        [JsonProperty("triangles")]
        public int Triangles { get; set; }

        [JsonProperty("vertices")]
        public int Vertices { get; set; }

        /// <summary>
        /// 메인 스레드 프레임 시간. Stats 창의 <c>CPU: main</c> 에 해당한다.
        /// </summary>
        [JsonProperty("mainThreadMs")]
        public float MainThreadMs { get; set; }

        /// <summary>렌더 스레드 프레임 시간. Stats 창의 <c>render thread</c>에 해당한다.</summary>
        [JsonProperty("renderThreadMs")]
        public float RenderThreadMs { get; set; }
    }
}
