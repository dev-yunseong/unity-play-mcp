using UnityPlayMcp.Affordances.Scan;
using UnityPlayMcp.Protocol;
using UnityPlayMcp.Protocol.Dto;
using UnityEngine;

namespace UnityPlayMcp.Diagnostics
{
    /// <summary>
    /// Unity 정적 API 에서 디바이스·런타임 컨텍스트를 읽는다.
    ///
    /// 정적 API 는 주입할 수 없어 테스트할 수 없으므로 읽기만 한다. 변환할 것이 없어 DTO 를 바로 돌려준다.
    ///
    /// <c>SystemInfo.deviceUniqueIdentifier</c> 는 개인정보라 읽지 않는다.
    /// </summary>
    internal static class RuntimeEnvironment
    {
        /// <summary>문자열을 못 읽은 플랫폼에서 null 대신 쓰는 값.</summary>
        private const string UnknownValue = "unknown";

#if ENABLE_IL2CPP
        private const string ScriptingBackend = "IL2CPP";
#elif ENABLE_MONO
        private const string ScriptingBackend = "Mono";
#else
        // 알려진 조합은 없다. 모르는 백엔드를 IL2CPP 로 단정하지 않는다.
        private const string ScriptingBackend = UnknownValue;
#endif

        /// <summary>세션당 한 번 부른다. 세션 중에 바뀌지 않는 값만 담는다.</summary>
        public static DeviceContextDto ReadDeviceContext()
        {
            var resolution = Screen.currentResolution;

            return new DeviceContextDto
            {
                DeviceModel = OrUnknown(SystemInfo.deviceModel),
                ProcessorType = OrUnknown(SystemInfo.processorType),
                ProcessorCount = SystemInfo.processorCount,
                SystemMemoryMb = SystemInfo.systemMemorySize,
                GraphicsDeviceName = OrUnknown(SystemInfo.graphicsDeviceName),
                GraphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                GraphicsMemoryMb = SystemInfo.graphicsMemorySize,
                OperatingSystem = OrUnknown(SystemInfo.operatingSystem),
                QualityLevel = QualitySettings.GetQualityLevel(),
                ResolutionWidth = resolution.width,
                ResolutionHeight = resolution.height,
                RefreshRateHz = ToReportableHz(resolution.refreshRateRatio.value),
                Dpi = ToReportableDpi(Screen.dpi),
                FullScreenMode = Screen.fullScreenMode.ToString(),
                TargetFrameRate = Application.targetFrameRate,
                VSyncCount = QualitySettings.vSyncCount,
                IsEditor = Application.isEditor,
                IsDebugBuild = Debug.isDebugBuild,
                ScriptingBackend = ScriptingBackend,
                SdkVersion = PackageVersion.Value,
                CollectedGroups = MetricGroupNames.Collected()
            };
        }

        /// <summary>보고마다 부른다. 세션 중에 바뀌는 값만 담는다.</summary>
        public static RuntimeStatusDto ReadStatus()
        {
            return new RuntimeStatusDto
            {
                IsFocused = Application.isFocused,
                BatteryStatus = SystemInfo.batteryStatus.ToString()
            };
        }

        /// <summary>
        /// <c>refreshRateRatio</c> 는 배치모드·가상 디스플레이에서 분모가 0 이라 NaN 이나 Infinity 가
        /// 된다. JSON 파싱이 깨지므로 모르는 값은 0 으로 보낸다.
        ///
        /// 정수 <c>refreshRate</c> 는 폐기됐고 59.94Hz 를 반올림하므로 쓰지 않는다.
        /// </summary>
        private static double ToReportableHz(double refreshRateHz)
        {
            var isFinite = !double.IsNaN(refreshRateHz) && !double.IsInfinity(refreshRateHz);
            return isFinite && refreshRateHz > 0d ? refreshRateHz : 0d;
        }

        /// <summary><c>Screen.dpi</c> 는 모르는 플랫폼에서 0 이나 NaN 을 준다.</summary>
        private static float ToReportableDpi(float dpi)
        {
            var isFinite = !float.IsNaN(dpi) && !float.IsInfinity(dpi);
            return isFinite && dpi > 0f ? dpi : 0f;
        }

        private static string OrUnknown(string value)
        {
            return string.IsNullOrEmpty(value) ? UnknownValue : value;
        }
    }
}
