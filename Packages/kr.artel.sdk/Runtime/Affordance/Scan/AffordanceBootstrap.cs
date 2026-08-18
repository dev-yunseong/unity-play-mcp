using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 씬에 아무것도 놓지 않고 스캔을 시작한다.
    /// </summary>
    /// <remarks>
    /// 스스로 부팅하는 것이 패키지 설치가 통합의 전부라는 약속을 지키는 방법이다. 게임 팀에게 매니저 객체를 모든 씬에
    /// 떨어뜨리라고 청하는 일은 그들의 씬을 바꾸는 일이고, 그것이 이것이 요구해서는 안 되는 그것이다.
    ///
    /// 로드되는 모든 씬이 읽혀 리포트에 더해지므로, 그저 게임을 하는 것만으로 다녀온 모든 곳에 대한 진술이 쌓인다. 아무도
    /// 걸어가지 않은 화면에 닿는 일이 <see cref="WalkAllScenes"/> 의 몫이다.
    ///
    /// 언제나 컴파일되고, 아무것도 하지 않는 일은 컴파일러가 아니라 출시 빌드의 몫이다: 아래의 구독만이 이 전부를 돌게 만드는
    /// 것이고, 그것은 <c>UNITY_EDITOR</c> 나 <c>DEVELOPMENT_BUILD</c> 가 참인 자리에서만 쥐어진다.
    /// </remarks>
    public static class AffordanceBootstrap
    {
        private const string FileName = "artel-affordances.json";

        /// <summary>리포트가 쓰이는 자리.</summary>
        public static string ReportPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <remarks>
        /// 출시된 게임에서는 아무것도 구독하지 않는다. 런타임 검사가 아니라 <c>#if</c> 로 묻는 것은, 출시된 플레이어가 구독도,
        /// 콜백도, 이것을 로드했을 이유도 쥐지 않도록 하기 위해서다.
        ///
        /// 같은 심볼 쌍을 반대쪽 <c>AffordanceILPostProcessor.IsDiscoveryBuild</c> 에서 읽고, 그쪽이 이것이 읽는 근거가 애초에
        /// 구워졌는지를 결정한다. 하나를 바꾸면 다른 하나도 바꿔라: 이쪽은 그것과 상수를 공유할 수 없다. 전처리기 검사는 제
        /// 어셈블리가 컴파일되는 자리에서 평가되고 어디서도 값을 읽을 수 없기 때문이다.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SceneManager.sceneLoaded += (scene, mode) => Capture(scene);
            Debug.Log("[Artel] Discovery is on. The report is written to " + ReportPath);
#endif
        }

        private static void Capture(Scene scene)
        {
            // 에디터에 저장된 상태가 아니라 씬이 올라온 뒤에 읽는다. 저장된 값은 무엇이 돌기 전에 필드가 쥐고 있던 것이라,
            // 컴포넌트가 Awake 에서 채우는 텍스트는 여전히 자리표시자로 읽힌다.
            try
            {
                SceneEvidenceScan.Capture(scene);

                // 순회 중에는 쓰지 않는다. 순회는 끝에 한 번 저장하고, 씬 로드 열두 번마다 파일을 쓰는 것은 같은 답을 위해 열두 배의
                // 일을 하는 것이다.
                if (!SceneWalk.InProgress)
                {
                    Save();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Artel] Reading " + scene.name + " failed: " + exception.Message);
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
        /// 진행 중이던 실행을 버리므로, 하는 것이 아니라 청하는 것이다. 순회가 이미 돌고 있으면 false 를 돌려준다. 둘이
        /// 동시에 돌면 어느 씬이 로드될지를 두고 다투기 때문이다.
        /// </remarks>
        public static bool WalkAllScenes()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Artel] A walk needs play mode: scenes are loaded as the game loads them.");
                return false;
            }

            return SceneWalk.Begin();
        }

        /// <summary>
        /// 근거가 이름 댄 모든 것의 살아 있는 값을 보내기 시작한다.
        /// </summary>
        /// <remarks>
        /// 리포트는 무엇이 참이어야 하는지를 말하고, 이것은 지금 무엇이 참인지를 말한다. 명세를 읽는 것이 아니라 돌리려면
        /// 그것이 필요하다. 그것을 위해 게임에서 표시해야 할 것은 없다 — 분석이 조건과 효과를 읽으면서 그 뒤의 멤버를 이미
        /// 적어 두었고, 감시되는 것이 그 목록이다.
        ///
        /// 하는 것이 아니라 청하는 것이다. 필드 백 개를 초당 열 번 읽는 값은 그 채널을 원하는 쪽이 치를 것이고, 이 패키지를
        /// 설치하는 프로젝트 대부분은 리포트만 원한다. 이미 돌고 있으면 false 를 돌려준다.
        ///
        /// sink 가 없으면 판독은 리포트 옆의 파일로 간다. 그래야 아무것도 듣고 있지 않을 때에도 채널을 지켜볼 수 있다.
        /// </remarks>
        public static bool WatchLiveState(Live.IPulseSink sink = null)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Artel] Watching needs play mode: nothing holds a value until the game runs.");
                return false;
            }

            // 아무도 멈추지 않았는데 끝나 버린 감시 — 플레이 모드를 나갔거나, carrier 가 파괴됐거나, 아무도 파일을 닫으라고
            // 청하지 않았거나. 도메인 리로드가 켜져 있으면 static 이 사라져 이것은 이미 null 이고, 꺼져 있으면 그것들이 살아남아
            // 더는 존재하지 않는 감시가 핸들을 쥐고 있게 된다. 여기서 닫는 값은 없는 것이나 마찬가지이고, 그것이 채널이 시작하는
            // 것과 채널이 거절하는 것의 차이다.
            var stale = _ours;
            _ours = null;
            stale?.Dispose();

            var ours = sink == null ? Live.PulseFile.Open() : null;
            var destination = sink ?? ours;

            if (destination == null)
            {
                return false;
            }

            if (!Live.Pulse.Begin(destination))
            {
                (ours as System.IDisposable)?.Dispose();
                return false;
            }

            _ours = ours;

            Debug.Log("[Artel] Watching " + Live.WatchList.All().Count + " members named by the evidence" +
                      (sink == null ? ". Readings go to " + Live.PulseFile.Path : "."));
            return true;
        }

        /// <summary>라이브 채널이 돌고 있는지.</summary>
        /// <remarks>
        /// 여기서 말하는 것은 박자 자체가 이 어셈블리 내부의 것이고 채널을 내놓는 에디터 메뉴는 다른 어셈블리에 살기 때문이다.
        /// 바깥의 호출자는 물어볼 다른 방법이 없고, 물어볼 수 없는 쪽은 제 답을 따로 쥐고 있어야 하는데 — 그것이 어긋나는
        /// 짝이다.
        /// </remarks>
        public static bool Watching => Live.Pulse.InProgress;

        /// <summary>
        /// 건네받은 것이 아니라 여기서 연 것. 그것만 다시 닫도록.
        /// </summary>
        /// <remarks>
        /// 제 sink 를 들고 온 호출자는 그것을 계속 쥔다 — 이 패키지가 열지 않은 것을 닫는 일은 호출자가 청한 적 없는 결정이다.
        /// 기본 파일은 우리 것이고, 감시가 멈춘 뒤에도 그것을 열어 두는 것이 다음 시작을 실패하게 만든다: 파일이 여전히 잡혀
        /// 있고, 다시 여는 것은 공유 위반이며, 게임은 불평하는 채널이 아니라 아예 채널 없이 돈다. 에디터에서 감시를 껐다 켜
        /// 실측했다.
        /// </remarks>
        private static Live.PulseFile _ours;

        /// <summary>살아 있는 값 보내기를 멈춘다.</summary>
        public static void StopWatching()
        {
            Live.Pulse.Stop();

            // 박자가 사라진 뒤에 한다. 닫힌 파일로 보내는 중인 것이 없도록.
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
                // 리포트 때문에 게임을 무너뜨리는 일은 결코 없다. discovery 는 곁가지 활동이고, 읽기 전용이거나 꽉 찬 디스크는
                // 게임의 문제가 아니다.
                Debug.LogWarning("[Artel] Could not write " + ReportPath + ": " + exception.Message);
                return null;
            }
        }
    }
}
