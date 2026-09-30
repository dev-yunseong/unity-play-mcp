#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// <see cref="Pulse"/> 가 쓸 reading 간격을 Unity project 별로 기억한다.
    /// </summary>
    /// <remarks>
    /// <c>EditorPrefs</c> 는 editor 에만 있고 이 값을 읽는 <see cref="Scan.AffordanceBootstrap"/> 는 runtime
    /// assembly 라 editor assembly 를 참조할 수 없으므로, 파일 전체를 <c>UNITY_EDITOR</c> 로 감싼다. player build 에서는
    /// <see cref="Pulse.DefaultInterval"/> 이 쓰인다.
    ///
    /// <c>EditorPrefs</c> 는 기계의 모든 project 가 공유하므로 key 에 project 경로를 넣는다. <c>v1</c> 은 저장 형식이
    /// 바뀌면 옛 값을 버리기 위한 것이다.
    /// </remarks>
    public static class PulseIntervalPreference
    {
        private const string KeyPrefix = "dev.yunseong.unityplaymcp.v1.pulseInterval.";

        /// <summary>저장된 값이 없거나 숫자로 읽을 수 없을 때 쓰는 초.</summary>
        public const float Default = Pulse.DefaultInterval;

        /// <summary>고를 수 있는 가장 짧은 초.</summary>
        /// <remarks>
        /// 50fps 의 한 frame 이다. <c>WaitForSecondsRealtime</c> 은 frame 경계에서만 깨어나므로 더 짧게 해도 더 자주
        /// 읽히지 않는다.
        /// </remarks>
        public const float Minimum = 0.02f;

        /// <summary>고를 수 있는 가장 긴 초.</summary>
        /// <remarks>
        /// 더 길면 agent 가 조작 결과를 제때 보지 못하고, 실수로 넣은 큰 값에 채널이 멈춘 것처럼 보인다.
        /// </remarks>
        public const float Maximum = 10f;

        /// <summary>
        /// 이 project 의 저장 key.
        /// </summary>
        /// <remarks>
        /// <c>/repo</c>, <c>/repo/</c>, <c>\repo</c> 가 같은 key 가 되도록 구분자와 끝 슬래시를 맞춘다.
        /// </remarks>
        public static string KeyFor(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("The Unity project directory is required.", nameof(projectRoot));
            }

            return KeyPrefix + projectRoot.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>지금 열려 있는 project 에 저장된 간격.</summary>
        /// <remarks>
        /// project 경로를 못 구하면 예외 대신 기본값을 쓴다. 예외를 던지면 채널이 시작하지 못한다.
        /// </remarks>
        public static float Current()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;

            return string.IsNullOrEmpty(projectRoot) ? Default : Read(projectRoot);
        }

        /// <remarks>
        /// 숫자가 아닌 값은 기본값이 되고, 범위 밖 숫자는 가까운 끝으로 잘린다. 범위 밖 숫자는 원한 방향이 분명하기 때문이다.
        /// </remarks>
        public static float Read(string projectRoot)
        {
            var stored = EditorPrefs.GetString(KeyFor(projectRoot), string.Empty);

            // EditorPrefs.GetFloat 은 값이 없는 경우와 다른 타입으로 저장된 경우를 구분하지 못해 문자열로 저장한다.
            if (!float.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return Default;
            }

            return Sanitize(seconds);
        }

        /// <summary>범위 안으로 자른 값을 저장하고, 저장한 값을 돌려준다.</summary>
        /// <remarks>
        /// 입력란이 저장된 값을 보이게 하려고 자른 값을 돌려준다. 그러지 않으면 다음에 열 때 숫자가 바뀐 것처럼 보인다.
        /// </remarks>
        public static float Write(string projectRoot, float seconds)
        {
            var stored = Sanitize(seconds);

            EditorPrefs.SetString(KeyFor(projectRoot), stored.ToString("R", CultureInfo.InvariantCulture));
            return stored;
        }

        private static float Sanitize(float seconds)
        {
            // Mathf.Clamp 는 NaN 을 그대로 통과시키고, 그 값이 Pulse.Begin 에 닿으면 채널이 조용히 시작하지 않는다.
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return Default;
            }

            return Mathf.Clamp(seconds, Minimum, Maximum);
        }
    }
}
#endif
