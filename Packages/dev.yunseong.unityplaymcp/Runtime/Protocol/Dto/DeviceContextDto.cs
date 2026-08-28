using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 성능 수치를 읽을 때 필요한 세션 고정 컨텍스트.
    /// </summary>
    /// <remarks>
    /// "FPS 30"만으로는 결함인지 아닌지 판단할 수 없다. <c>targetFrameRate</c>가 30에 묶여
    /// 있거나 vSync가 켜져 있으면 의도한 값이고, GPU가 약해서 나온 값이면 결함이다. 이 컨텍스트가
    /// 없으면 두 경우가 같은 숫자로 보인다.
    ///
    /// 세션 동안 변하지 않는 값만 담는다. 보고마다 바뀌는 값은 <see cref="RuntimeStatusDto"/>에 있다.
    ///
    /// 기기 식별자(<c>SystemInfo.deviceUniqueIdentifier</c>)는 담지 않는다. 성능 해석에 쓰이지
    /// 않는데 한 번 실리면 보고 전체가 개인정보가 된다. 추가하지 말 것.
    /// </remarks>
    public sealed class DeviceContextDto
    {
        [JsonProperty("deviceModel")]
        public string DeviceModel { get; set; }

        [JsonProperty("processorType")]
        public string ProcessorType { get; set; }

        /// <summary>
        /// 논리 코어 수. 코어가 적은 기기에서는 메인 스레드 밖으로 밀어낸 작업이 오히려
        /// 프레임을 잡아먹으므로, 같은 프레임타임이라도 원인 후보가 달라진다.
        /// </summary>
        [JsonProperty("processorCount")]
        public int ProcessorCount { get; set; }

        [JsonProperty("systemMemoryMb")]
        public int SystemMemoryMb { get; set; }

        [JsonProperty("graphicsDeviceName")]
        public string GraphicsDeviceName { get; set; }

        /// <summary>
        /// 그래픽 API 이름. 같은 GPU라도 Vulkan과 OpenGL의 드로우콜 비용이 달라, API를 모르면
        /// GPU 성능 문제와 API 선택 문제를 구분할 수 없다.
        /// </summary>
        [JsonProperty("graphicsDeviceType")]
        public string GraphicsDeviceType { get; set; }

        [JsonProperty("graphicsMemoryMb")]
        public int GraphicsMemoryMb { get; set; }

        [JsonProperty("operatingSystem")]
        public string OperatingSystem { get; set; }

        /// <summary>
        /// 적용 중인 품질 레벨의 인덱스. 같은 빌드·같은 기기에서 프레임이 갈리는 원인이
        /// 대개 여기라, 없으면 하드웨어 차이로 잘못 읽는다.
        /// </summary>
        [JsonProperty("qualityLevel")]
        public int QualityLevel { get; set; }

        /// <summary>
        /// 디스플레이 해상도의 가로 픽셀. 창 크기(<c>Screen.width</c>)가 아니라 화면 자체의
        /// 해상도이므로, 창 모드 세션에서는 실제 렌더 타깃보다 클 수 있다.
        /// </summary>
        [JsonProperty("resolutionWidth")]
        public int ResolutionWidth { get; set; }

        /// <summary>디스플레이 해상도의 세로 픽셀. <see cref="ResolutionWidth"/>와 같은 기준이다.</summary>
        [JsonProperty("resolutionHeight")]
        public int ResolutionHeight { get; set; }

        /// <summary>
        /// 디스플레이 주사율. 60Hz에서의 60fps는 상한에 붙은 것이고 144Hz에서의 60fps는 절반도
        /// 못 낸 것이라, 프레임 예산의 해석 근거가 된다. 값을 읽지 못한 환경에서는 0이다.
        /// </summary>
        [JsonProperty("refreshRateHz")]
        public double RefreshRateHz { get; set; }

        /// <summary>
        /// 화면 DPI. 같은 해상도라도 DPI가 높으면 UI 스케일이 커져 픽셀 채우기 비용이 달라진다.
        /// 알 수 없는 환경에서는 0이다.
        /// </summary>
        [JsonProperty("dpi")]
        public float Dpi { get; set; }

        [JsonProperty("fullScreenMode")]
        public string FullScreenMode { get; set; }

        /// <summary>
        /// 프레임레이트 상한. 상한이 없으면 -1이다. 0이 아니므로 "상한 없음"을 "0fps 목표"로
        /// 읽지 않도록 주의한다.
        /// </summary>
        [JsonProperty("targetFrameRate")]
        public int TargetFrameRate { get; set; }

        /// <summary>
        /// vSync 간격. 0이 아니면 프레임레이트가 주사율에 묶이므로, 이 값을 보지 않으면
        /// 수직동기로 눌린 프레임을 성능 한계로 오진한다.
        /// </summary>
        [JsonProperty("vSyncCount")]
        public int VSyncCount { get; set; }

        /// <summary>
        /// 에디터 세션 여부.
        ///
        /// 에디터 수치는 씬 뷰 렌더링과 인스펙터 갱신이 얹혀 부풀어 있어 Standalone과 같은 축에
        /// 놓을 수 없다. 이 플래그가 있어야 소비자가 에디터 세션을 통계에서 분리할 수 있고,
        /// 없으면 에디터 표본이 섞여 빌드 성능이 실제보다 나쁘게 집계된다.
        /// </summary>
        [JsonProperty("isEditor")]
        public bool IsEditor { get; set; }

        /// <summary>
        /// 개발 빌드 여부. 개발 빌드는 프로파일러 훅과 로깅이 켜져 있어 릴리스보다 느리다.
        /// </summary>
        [JsonProperty("isDebugBuild")]
        public bool IsDebugBuild { get; set; }

        /// <summary>
        /// 스크립팅 백엔드. IL2CPP와 Mono는 스크립트 실행 비용이 배 단위로 갈리므로,
        /// 백엔드가 다른 세션끼리는 프레임 지표를 직접 비교할 수 없다.
        /// </summary>
        [JsonProperty("scriptingBackend")]
        public string ScriptingBackend { get; set; }

        /// <summary>
        /// 보고를 만든 SDK 버전. 수집 방식이 바뀌면 지표의 의미도 바뀌므로, 버전 경계를
        /// 모르면 서로 다른 정의의 값을 한 시계열에 이어 붙이게 된다.
        /// </summary>
        [JsonProperty("sdkVersion")]
        public string SdkVersion { get; set; }

        /// <summary>
        /// 이 SDK 빌드가 수집을 시도하는 지표군 이름.
        ///
        /// 값이 없는 군을 두 가지로 갈라 읽게 하는 유일한 근거다. 목록에 있는데 값이 안 왔으면
        /// 재려 했으나 이 플랫폼·빌드에 카운터가 없었던 것이고, 목록에 아예 없으면 이 SDK 버전이
        /// 그 군을 모르는 것이다. 이 필드가 없으면 둘이 한 덩어리가 되어, 어제 있던 값이 오늘
        /// 없을 때 SDK를 올려서인지 게임이 그 경로를 안 타서인지 답할 수 없다.
        ///
        /// 플랫폼에 따라 달라지지 않는다. 자세한 것은 <see cref="MetricGroupNames.Collected"/>.
        /// </summary>
        [JsonProperty("collectedGroups")]
        public string[] CollectedGroups { get; set; }
    }
}
