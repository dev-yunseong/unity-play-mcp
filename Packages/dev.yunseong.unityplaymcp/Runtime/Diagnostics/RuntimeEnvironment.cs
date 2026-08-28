using Artel.Protocol;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel.Diagnostics
{
    /// <summary>
    /// Unity 정적 API에서 디바이스·런타임 컨텍스트를 읽는다.
    ///
    /// <c>SystemInfo</c>·<c>Screen</c>·<c>QualitySettings</c>는 주입할 수 없어 이 경계 안쪽은
    /// 테스트가 닿지 않는다. 그래서 읽기만 하고 계산은 하지 않는다. 판단할 것이 없어야
    /// 검증하지 못하는 코드가 최소로 남는다.
    ///
    /// 도메인 구조체를 거치지 않고 DTO를 바로 돌려준다. <c>FrameTimeStatistics</c>와 달리
    /// 단위 변환도 파생 계산도 없어서, 중간 타입과 매퍼를 두면 필드를 그대로 옮겨 적는 파일만
    /// 두 개 늘어난다.
    ///
    /// <c>SystemInfo.deviceUniqueIdentifier</c>는 읽지 않는다. 성능 해석에 쓰이지 않는데 한 번
    /// 실리면 보고 전체가 개인정보가 된다. 추가하지 말 것.
    /// </summary>
    internal static class RuntimeEnvironment
    {
        /// <summary>문자열을 못 읽은 플랫폼에서 쓰는 값. null을 그대로 실으면 소비자가 갈린다.</summary>
        private const string UnknownValue = "unknown";

        /// <summary>
        /// package.json의 <c>version</c>과 손으로 맞춘다.
        ///
        /// 런타임에는 package.json을 읽을 수 없고, <c>UnityEditor.PackageManager</c>로 읽으려면
        /// 런타임 어셈블리가 에디터 전용 API에 걸려 Standalone 빌드가 깨진다. 상수 쪽이 싸고,
        /// 어긋나면 <c>RuntimeEnvironmentTests</c>가 잡는다.
        /// </summary>
        private const string SdkVersion = "0.1.0";

#if ENABLE_IL2CPP
        private const string ScriptingBackend = "IL2CPP";
#elif ENABLE_MONO
        private const string ScriptingBackend = "Mono";
#else
        // 두 심볼 모두 없는 조합은 알려진 게 없지만, 이름 없는 백엔드를 IL2CPP로 단정하면
        // 비교할 수 없는 세션이 조용히 섞인다.
        private const string ScriptingBackend = UnknownValue;
#endif

        /// <summary>세션당 한 번만 부른다. 여기 담긴 값은 세션 중에 바뀌지 않는다.</summary>
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
                SdkVersion = SdkVersion,
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
        /// 주사율을 보고 가능한 값으로 정리한다.
        ///
        /// <c>refreshRateRatio</c>는 분모가 0으로 오는 환경(배치모드, 가상 디스플레이)이 있어
        /// NaN이나 Infinity가 나온다. 그대로 실으면 JSON에 숫자가 아닌 토큰이 들어가 소비자가
        /// 파싱에서 터지므로, 미상은 0으로 눌러 보낸다.
        ///
        /// 정수 <c>refreshRate</c>는 Unity 2022.2부터 폐기됐다. 60이 아니라 59.94Hz인 화면을
        /// 반올림해 버려 예산 계산이 어긋나기 때문에, 비율 쪽을 쓴다.
        /// </summary>
        private static double ToReportableHz(double refreshRateHz)
        {
            var isFinite = !double.IsNaN(refreshRateHz) && !double.IsInfinity(refreshRateHz);
            return isFinite && refreshRateHz > 0d ? refreshRateHz : 0d;
        }

        /// <summary><c>Screen.dpi</c>는 알 수 없는 플랫폼에서 0을 주고, NaN이 관측된 사례도 있다.</summary>
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
