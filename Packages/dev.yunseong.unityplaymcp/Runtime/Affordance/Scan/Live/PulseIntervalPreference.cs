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
    /// 파일 전체가 <c>UNITY_EDITOR</c> 안에 있다. <c>EditorPrefs</c> 는 editor 에만 있고, 이 값을 읽는
    /// <see cref="Scan.AffordanceBootstrap"/> 는 runtime assembly 라 editor assembly 를 참조할 수 없다.
    /// 값을 나르려고 asset 이나 <c>Resources</c> 를 만드는 대신, 값을 읽는 코드를 editor 로 컴파일될 때에만
    /// 존재하게 한다. player build 에서는 이 타입이 없고 <see cref="Pulse.DefaultInterval"/> 이 그대로 쓰인다.
    ///
    /// <c>EditorPrefs</c> 는 한 기계의 모든 project 가 함께 쓰므로 key 에 project 경로를 넣는다. 넣지 않으면
    /// 한 project 에서 줄인 간격이 다른 project 에도 적용된다. key 앞의 <c>v1</c> 은 저장 형식을 바꿔야 할 때
    /// 옛 값을 읽지 않고 버리기 위한 것이다.
    /// </remarks>
    public static class PulseIntervalPreference
    {
        private const string KeyPrefix = "dev.yunseong.unityplaymcp.v1.pulseInterval.";

        /// <summary>저장된 값이 없거나 숫자로 읽을 수 없을 때 쓰는 초.</summary>
        public const float Default = Pulse.DefaultInterval;

        /// <summary>고를 수 있는 가장 짧은 초.</summary>
        /// <remarks>
        /// 0.02초는 50fps 게임의 한 frame 이다. 그보다 촘촘히 청해도 <c>WaitForSecondsRealtime</c> 은 frame
        /// 경계에서만 깨어나므로 더 자주 읽히지 않고, 읽기가 매 frame 게임과 함께 도는 값만 남는다.
        /// </remarks>
        public const float Minimum = 0.02f;

        /// <summary>고를 수 있는 가장 긴 초.</summary>
        /// <remarks>
        /// 10초. 그보다 길면 agent 가 방금 한 조작의 결과를 같은 대화 안에서 보지 못해, 채널이 있으나 없으나
        /// 같아진다. 아주 긴 값을 실수로 넣어 채널이 죽은 것처럼 보이는 일도 여기서 막힌다.
        /// </remarks>
        public const float Maximum = 10f;

        /// <summary>
        /// 이 project 의 저장 key.
        /// </summary>
        /// <remarks>
        /// 같은 project 를 <c>/repo</c> 로도 <c>/repo/</c> 로도 <c>\repo</c> 로도 받을 수 있다. 그대로 key 에
        /// 넣으면 같은 project 가 서로 다른 값을 갖는다.
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
        /// project 경로를 못 구하면 기본값으로 떨어진다. 여기서 예외를 던지면 채널이 아예 시작하지 못하는데,
        /// 간격 하나를 못 읽은 것은 감시를 포기할 이유가 아니다.
        /// </remarks>
        public static float Current()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;

            return string.IsNullOrEmpty(projectRoot) ? Default : Read(projectRoot);
        }

        /// <remarks>
        /// 숫자로 읽히지 않는 값은 기본값이 되고, 읽히지만 범위 밖인 값은 가까운 끝으로 잘린다. 두 경우를 다르게
        /// 다루는 것은 뜻이 다르기 때문이다. 읽히지 않는 값은 누가 무엇을 원했는지 알 수 없지만, 0.001 은 "훨씬
        /// 촘촘하게" 라는 뜻이 분명해서 그 방향을 지키는 것이 기본값으로 되돌리는 것보다 사람이 청한 바에 가깝다.
        /// </remarks>
        public static float Read(string projectRoot)
        {
            var stored = EditorPrefs.GetString(KeyFor(projectRoot), string.Empty);

            // 문자열로 저장한다. EditorPrefs.GetFloat 은 값이 없는 것과 다른 타입으로 저장된 것을 구분해 주지 않아,
            // 그 자리에 남은 남의 값을 사람이 고른 간격으로 읽어 버린다.
            if (!float.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return Default;
            }

            return Sanitize(seconds);
        }

        /// <summary>범위 안으로 자른 값을 저장하고, 저장한 값을 돌려준다.</summary>
        /// <remarks>
        /// 자른 값을 돌려주는 것은 화면이 방금 저장한 것을 그대로 보여야 하기 때문이다. 넣은 값과 저장된 값이 다른데
        /// 입력란이 넣은 값을 계속 들고 있으면, 다음에 화면을 열 때 숫자가 저 혼자 바뀐 것처럼 보인다.
        /// </remarks>
        public static float Write(string projectRoot, float seconds)
        {
            var stored = Sanitize(seconds);

            EditorPrefs.SetString(KeyFor(projectRoot), stored.ToString("R", CultureInfo.InvariantCulture));
            return stored;
        }

        private static float Sanitize(float seconds)
        {
            // NaN 과 무한대는 어느 끝으로도 자를 수 없다. 비교가 전부 false 라 Mathf.Clamp 는 그것들을 그대로 통과시키고,
            // 그 값이 Pulse.Begin 에 닿으면 채널이 조용히 시작하지 않는다.
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return Default;
            }

            return Mathf.Clamp(seconds, Minimum, Maximum);
        }
    }
}
#endif
