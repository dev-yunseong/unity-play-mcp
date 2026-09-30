using Newtonsoft.Json;

namespace UnityPlayMcp.Protocol.Dto
{
    /// <summary>
    /// 성능 수치를 읽을 때 필요한 세션 고정 컨텍스트.
    /// </summary>
    /// <remarks>
    /// 같은 FPS 도 <c>targetFrameRate</c>·vSync·하드웨어에 따라 의도한 값인지 결함인지 달라진다.
    ///
    /// 세션 동안 변하지 않는 값만 담는다. 바뀌는 값은 <see cref="RuntimeStatusDto"/> 에 있다.
    ///
    /// 기기 식별자(<c>SystemInfo.deviceUniqueIdentifier</c>)는 개인정보라 담지 않는다.
    /// </remarks>
    public sealed class DeviceContextDto
    {
        [JsonProperty("deviceModel")]
        public string DeviceModel { get; set; }

        [JsonProperty("processorType")]
        public string ProcessorType { get; set; }

        /// <summary>논리 코어 수.</summary>
        [JsonProperty("processorCount")]
        public int ProcessorCount { get; set; }

        [JsonProperty("systemMemoryMb")]
        public int SystemMemoryMb { get; set; }

        [JsonProperty("graphicsDeviceName")]
        public string GraphicsDeviceName { get; set; }

        /// <summary>그래픽 API 이름. 같은 GPU 라도 API 에 따라 드로우 콜 비용이 다르다.</summary>
        [JsonProperty("graphicsDeviceType")]
        public string GraphicsDeviceType { get; set; }

        [JsonProperty("graphicsMemoryMb")]
        public int GraphicsMemoryMb { get; set; }

        [JsonProperty("operatingSystem")]
        public string OperatingSystem { get; set; }

        /// <summary>적용 중인 품질 레벨의 인덱스.</summary>
        [JsonProperty("qualityLevel")]
        public int QualityLevel { get; set; }

        /// <summary>
        /// 디스플레이 해상도의 가로 픽셀. 창 크기(<c>Screen.width</c>)가 아니라 창 모드에서는 렌더 타깃보다 클 수 있다.
        /// </summary>
        [JsonProperty("resolutionWidth")]
        public int ResolutionWidth { get; set; }

        /// <summary>디스플레이 해상도의 세로 픽셀. <see cref="ResolutionWidth"/>와 같은 기준이다.</summary>
        [JsonProperty("resolutionHeight")]
        public int ResolutionHeight { get; set; }

        /// <summary>
        /// 디스플레이 주사율. 프레임 예산의 해석 근거다. 읽지 못하면 0 이다.
        /// </summary>
        [JsonProperty("refreshRateHz")]
        public double RefreshRateHz { get; set; }

        /// <summary>화면 DPI. 알 수 없으면 0 이다.</summary>
        [JsonProperty("dpi")]
        public float Dpi { get; set; }

        [JsonProperty("fullScreenMode")]
        public string FullScreenMode { get; set; }

        /// <summary>프레임레이트 상한. 상한이 없으면 0 이 아니라 -1 이다.</summary>
        [JsonProperty("targetFrameRate")]
        public int TargetFrameRate { get; set; }

        /// <summary>vSync 간격. 0 이 아니면 프레임레이트가 주사율에 묶인다.</summary>
        [JsonProperty("vSyncCount")]
        public int VSyncCount { get; set; }

        /// <summary>
        /// 에디터 세션 여부. 에디터 수치는 Scene view·Inspector 비용이 얹혀 Standalone 과 비교할 수 없다.
        /// </summary>
        [JsonProperty("isEditor")]
        public bool IsEditor { get; set; }

        /// <summary>개발 빌드 여부. 개발 빌드는 릴리스보다 느리다.</summary>
        [JsonProperty("isDebugBuild")]
        public bool IsDebugBuild { get; set; }

        /// <summary>스크립팅 백엔드. 백엔드가 다른 세션끼리는 프레임 지표를 직접 비교할 수 없다.</summary>
        [JsonProperty("scriptingBackend")]
        public string ScriptingBackend { get; set; }

        /// <summary>보고를 만든 SDK 버전. 수집 방식이 바뀌면 지표의 의미도 바뀐다.</summary>
        [JsonProperty("sdkVersion")]
        public string SdkVersion { get; set; }

        /// <summary>
        /// 이 SDK 빌드가 수집을 시도하는 지표군 이름. 플랫폼과 무관하다.
        ///
        /// 목록에 있는데 값이 없으면 이 플랫폼·빌드에서 못 잰 것이고, 목록에 없으면 이 SDK 버전이
        /// 모르는 군이다. <see cref="MetricGroupNames.Collected"/> 참고.
        /// </summary>
        [JsonProperty("collectedGroups")]
        public string[] CollectedGroups { get; set; }
    }
}
