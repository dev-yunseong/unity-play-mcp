using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 씬에 아무것도 놓지 않고 스캔을 시작한다.
    /// </summary>
    /// <remarks>
    /// 게임 씬에 매니저 객체를 놓으라고 요구하지 않는다.
    /// <see cref="Follow"/> 이후 로드되는 모든 씬을 읽어 리포트에 더하고, 방문하지 않은 씬은 <see cref="WalkAllScenes"/> 가 읽는다.
    /// <see cref="Follow"/> 가 불리기 전에는 아무것도 읽지 않는다.
    /// </remarks>
    public static class AffordanceBootstrap
    {
        private const string FileName = "unity-play-mcp-affordances.json";

        /// <summary>리포트가 쓰이는 자리.</summary>
        public static string ReportPath => Path.Combine(Application.persistentDataPath, FileName);

        private static bool _following;

        /// <summary>씬 로드가 지금 읽히고 있는지.</summary>
        public static bool Following => _following;

        /// <summary>
        /// 게임이 씬을 로드하는 대로 읽기 시작하고, 시작됐는지를 말한다.
        /// </summary>
        /// <remarks>
        /// 인스턴스가 연결될 때 불린다. SDK 를 다른 용도로만 쓰는 게임이 스캔 비용을 치르거나 리포트 파일을 받지 않게 한다.
        /// 재연결이 있으므로 멱등이다.
        /// release 빌드는 <c>#if</c> 로 거절한다. 같은 심볼 조건을 <c>AffordanceILPostProcessor.IsDiscoveryBuild</c> 도 쓰므로
        /// 하나를 바꾸면 다른 하나도 바꿔야 한다. 전처리기 조건은 어셈블리 사이에 공유할 수 없다.
        /// </remarks>
        public static bool Follow()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_following)
            {
                return true;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            _following = true;

            // 이미 로드된 씬은 sceneLoaded 로 오지 않으므로 지금 읽는다.
            CaptureNow();

            Debug.Log("[Unity Play MCP] Discovery is following scene loads. The report is written to " + ReportPath);
            return true;
#else
            // 호출자가 결과가 없는 이유를 알 수 있게 로그를 남긴다.
            Debug.Log("[Unity Play MCP] Discovery does not run in a release build.");
            return false;
#endif
        }

        /// <summary>씬 읽기를 멈춘다. 한 번도 시작하지 않았을 때 불러도 안전하다.</summary>
        public static void StopFollowing()
        {
            if (!_following)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            _following = false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Capture(scene);
        }

        private static void Capture(Scene scene)
        {
            // 저장된 씬 값이 아니라 로드 후 값을 읽는다. Awake 에서 채우는 텍스트가 자리표시자로 읽히지 않게 한다.
            try
            {
                SceneEvidenceScan.Capture(scene);

                // 순회는 끝에서 한 번 저장하므로 순회 중에는 쓰지 않는다.
                if (!SceneWalk.InProgress)
                {
                    Save();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Unity Play MCP] Reading " + scene.name + " failed: " + exception.Message);
            }
        }

        /// <summary>지금 로드된 모든 씬을 읽고 리포트를 쓴다.</summary>
        public static string CaptureNow()
        {
            SceneEvidenceScan.CaptureLoaded();
            return Save();
        }

        /// <summary>
        /// 빌드 설정의 모든 씬을 방문해 하나하나 읽는다.
        /// </summary>
        /// <remarks>
        /// 진행 중이던 실행을 버리므로 명시적으로 요청할 때만 돈다. 두 순회가 씬 로드를 두고 다투므로 이미 돌고 있으면 false 다.
        /// </remarks>
        public static bool WalkAllScenes()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Unity Play MCP] A walk needs play mode: scenes are loaded as the game loads them.");
                return false;
            }

            return SceneWalk.Begin();
        }

        /// <summary>순회가 지금 돌고 있는지.</summary>
        /// <remarks>
        /// <c>SceneWalk</c> 가 internal 이라 원격 스캔 명령 쪽이 따로 상태를 들지 않고 여기서 묻는다.
        /// </remarks>
        public static bool Walking => SceneWalk.InProgress;

        /// <summary>
        /// `evidence` 가 이름 댄 멤버들의 현재 값을 보내기 시작한다.
        /// </summary>
        /// <remarks>
        /// 감시 대상은 분석이 조건과 효과에서 적어 둔 멤버 목록이다. 읽기 비용이 있으므로 요청할 때만 돌며, 이미 돌고 있으면 false 다.
        /// sink 가 없으면 <c>pulse</c> 는 리포트 옆 파일로 간다.
        /// </remarks>
        public static bool WatchLiveState(Live.IPulseSink sink = null)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Unity Play MCP] Watching needs play mode: nothing holds a value until the game runs.");
                return false;
            }

            // Stop 없이 끝난 감시(플레이 모드 종료, carrier 파괴)의 파일을 닫는다. 도메인 리로드가 꺼져 있으면
            // static 이 남아 파일을 쥐고 있고, 그대로 두면 다시 열 때 공유 위반으로 실패한다.
            var stale = _ours;
            _ours = null;
            stale?.Dispose();

            var ours = sink == null ? Live.PulseFile.Open() : null;
            var destination = sink ?? ours;

            if (destination == null)
            {
                return false;
            }

            if (!Live.Pulse.Begin(destination, ReadingInterval()))
            {
                (ours as System.IDisposable)?.Dispose();
                return false;
            }

            _ours = ours;

            Debug.Log("[Unity Play MCP] Watching " + Live.WatchList.All().Count + " members named by the evidence" +
                      (sink == null ? ". Readings go to " + Live.PulseFile.Path : "."));
            return true;
        }

        /// <summary>이 project 에 설정된 <c>pulse</c> 간격(초).</summary>
        /// <remarks>
        /// 값은 editor 전용 <c>EditorPrefs</c> 에 있어 <c>#if UNITY_EDITOR</c> 에서만 읽는다. 프로젝트에 설정 asset 을 만들지 않으려는 선택이다.
        /// 감시 시작 때 한 번 읽으므로 도중에 바꾼 값은 다음 시작부터 적용된다.
        /// </remarks>
        private static float ReadingInterval()
        {
#if UNITY_EDITOR
            return Live.PulseIntervalPreference.Current();
#else
            return Live.Pulse.DefaultInterval;
#endif
        }

        /// <summary>라이브 채널이 돌고 있는지.</summary>
        /// <remarks>
        /// <c>Pulse</c> 가 internal 이라 다른 어셈블리의 에디터 메뉴가 따로 상태를 들지 않고 여기서 묻는다.
        /// </remarks>
        public static bool Watching => Live.Pulse.InProgress;

        /// <summary>다음 reading 을 `whole` 로 보내게 한다. 돌고 있지 않으면 아무것도 하지 않는다.</summary>
        /// <remarks>
        /// 새로 붙거나 <c>start_readings</c> 를 다시 보낸 MCP server 가 쓴다. 차이만으로는 놓친 값을 되찾지 못한다.
        /// </remarks>
        public static void RequestWholeReading()
        {
            Live.Pulse.RequestWhole();
        }

        /// <summary>
        /// 여기서 연 기본 파일. 호출자가 건넨 sink 는 닫지 않는다.
        /// </summary>
        /// <remarks>
        /// 감시가 멈춘 뒤에도 열어 두면 다음 시작에서 공유 위반으로 파일을 열지 못한다.
        /// </remarks>
        private static Live.PulseFile _ours;

        /// <summary>살아 있는 값 보내기를 멈춘다.</summary>
        public static void StopWatching()
        {
            Live.Pulse.Stop();

            // 닫힌 파일로 보내지 않도록 `pulse` 를 멈춘 뒤에 닫는다.
            var ours = _ours;
            _ours = null;
            ours?.Dispose();
        }

        /// <summary>여태 모은 것을 전부 버린다.</summary>
        public static void Forget()
        {
            AffordanceReport.Forget();
        }

        /// <summary>리포트를 쓰고, 어디로 갔는지를 돌려준다. 쓸 수 없었으면 null.</summary>
        public static string Save()
        {
            try
            {
                File.WriteAllText(ReportPath, AffordanceReport.Compose());
                return ReportPath;
            }
            catch (Exception exception)
            {
                // 리포트 쓰기 실패로 게임을 멈추지 않는다.
                Debug.LogWarning("[Unity Play MCP] Could not write " + ReportPath + ": " + exception.Message);
                return null;
            }
        }
    }
}
