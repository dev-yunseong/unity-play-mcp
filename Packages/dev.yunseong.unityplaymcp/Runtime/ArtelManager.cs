using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Auth;
using Artel.Capture;
using Artel.Diagnostics;
using Artel.Domain;
using Artel.Evidence;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using Artel.Streaming;
using Artel.Tracking;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Serialization;

namespace Artel
{
    public sealed class ArtelManager : MonoBehaviour, IReadingChannel
    {
        private const float SceneScanIntervalSeconds = 1f;
        private const float PerformanceReportIntervalSeconds = 1f;

        /// <summary>
        /// The one manager that survives scene loads. Static rather than looked up
        /// each time because the check runs in Awake, before anything else can
        /// register it.
        /// </summary>
        private static ArtelManager instance;

        /// <summary>
        /// <c>GAME_STATE</c> 채널을 보내는가 (ARTEL-513). <b>기본은 끔이다.</b>
        /// </summary>
        /// <remarks>
        /// <b>임시 스위치다.</b> 실제로 지우는 것은 ARTEL-400 이고, 그때 이 속성도 함께 사라진다.
        ///
        /// 목적은 채널을 덜어내는 것이지 선택지를 만드는 것이 아니다. 그래서 기본이 끔이다 — 켜 두고 누군가 끄기를
        /// 기다리면 아무도 끄지 않고, 판독이 <c>GAME_STATE</c> 를 대신할 수 있는지는 영영 재지지 않는다. 둘이 함께
        /// 오는 동안에는 어느 쪽이 무엇을 하고 있는지 가릴 방법이 없다.
        ///
        /// 그럼에도 지우지 않고 스위치로 둔 것은 <b>되돌릴 수 있어야 하기 때문</b>이다. 판독이 못 덮는 것이 실제
        /// 게임에서 드러나면 코드를 되살리는 대신 이 값을 <c>true</c> 로 돌려 그 자리에서 복구한다.
        ///
        /// 프레임만 막지 않고 <see cref="sceneStatePoller"/> 앞에서 막는 것이 요점이다. ARTEL-400 이 지우려는 것은
        /// 전송이 아니라 <b>씬 순회</b>(<c>SceneScanner</c>·<c>SceneStatePoller</c>)이므로, 그것이 돌지 않는 상태를
        /// 재야 폐기 뒤를 예측할 수 있다.
        /// </remarks>
        public static bool SendsGameState { get; set; } = false;

        /// <summary>
        /// 첫 연결이 <c>Start</c> 에서 일어나는 이유는 <see cref="Start"/> 에 적었다. 이름이 그 자리를 말하도록
        /// 바뀌었고, 씬에 직렬화된 값은 <c>FormerlySerializedAs</c> 가 넘겨받는다.
        /// </summary>
        [FormerlySerializedAs("connectOnEnable")]
        [SerializeField] private bool connectOnStart;
        [SerializeField] private Server server = new Server();

        private IArtelWebSocketTransport webSocketTransport;
        private bool ownsTransport = true;
        private SceneScanner scanner;
        private AllSceneScanner allSceneScanner;
        private ActionExecutor actionExecutor;
        private CursorController cursorController;
        private PointerEventDispatcher pointerEvents;
        private IJsonCodec jsonCodec;
        private SceneStatePoller sceneStatePoller;
        private FrameTimeRecorder frameTimeRecorder;
        private FrameTimingSampler frameTimingSampler;
        private ProcessResourceSampler processResourceSampler;
        private float nextPerformanceReportTime;
        private float lastPerformanceSampleTime;

        /// <summary>Frame Timing Stats 경고를 한 번만 내기 위한 표시. 매 보고마다 찍으면 로그가 덮인다.</summary>
        private bool warnedFrameTimingUnavailable;
        private bool reportedDeviceContext;

        /// <summary>지난 프레임의 전송 연결 상태. 새 연결이 열린 프레임을 집어내는 데만 쓴다.</summary>
        private bool transportWasConnected;
        private ArtelStreamHost streamHost;
        private Coroutine webRtcPump;

        /// <summary>
        /// What the host game had <c>Application.runInBackground</c> set to before this manager
        /// opened its own connection. See <see cref="StartTransport"/>. It is written only where
        /// the manager builds its own transport, so a transport handed in with
        /// <see cref="SetWebSocketTransport"/> and ownership must never reach the restore in
        /// <see cref="StopTransport"/> — that would put this default back over a host game that
        /// had the setting on.
        /// </summary>
        private bool hostRunInBackground;
        private long nextMessageId = 1;
        private readonly Queue<ArtelRequestDto> actionRequests = new Queue<ArtelRequestDto>();
        private bool processingActions;

        /// <summary>False on a duplicate that Awake destroyed before it built anything.</summary>
        private bool ownsRuntime;

        /// <summary>Separates the first connection, which is Start's, from a later re-enable.</summary>
        private bool hasStarted;

        public string SdkId { get; private set; }
        public string GameVersion { get; private set; }

        /// <summary>대시보드에서 이 설치를 알아볼 첫 이름. 서버가 등록 때 한 번만 쓴다.</summary>
        public string InstanceName { get; private set; }

        public Server Server { get { return server; } }
        public bool SmoothCursorMovement
        {
            get { return cursorController != null && cursorController.SmoothMovement; }
            set
            {
                if (cursorController != null)
                {
                    cursorController.SmoothMovement = value;
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Editor and development builds get a manager even when no scene carries one:
        /// a QA run has to be able to attach to a build nobody prepared for it. The
        /// whole method is compiled out of release builds. Runs after the first scene
        /// loads so a manager the scene does carry — with its configured server —
        /// keeps the spot.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SpawnInDevelopmentBuilds()
        {
            if (instance != null)
            {
                return;
            }

            new GameObject("Artel").AddComponent<ArtelManager>();
        }
#endif

        private void Awake()
        {
            // The socket has to outlive the scene it was opened in. A QA run acts
            // on the game, and acting frequently loads another scene — which used
            // to destroy this object mid-run, closing the connection and failing
            // the run at exactly the moment the interesting part began.
            if (instance != null && instance != this)
            {
                // A second manager appears when a scene carrying one is loaded
                // again. Keeping the first preserves the live connection; the
                // newcomer would open a second and be rejected as a duplicate.
                Destroy(gameObject);
                return;
            }

            instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            EnsureRuntime();
        }

        /// <summary>
        /// Builds everything this manager owns, once.
        /// </summary>
        /// <remarks>
        /// Reachable from <see cref="SetWebSocketTransport"/> as well as <see cref="Awake"/>
        /// because Unity orders Awake and OnEnable between components arbitrarily.
        /// <c>ArtelTestPageManager</c> installs its transport from its own <c>OnEnable</c>, which
        /// can land before this manager's <c>Awake</c>; that used to throw a
        /// <c>NullReferenceException</c> on <see cref="sceneStatePoller"/> partway through
        /// installing the transport. The half-installed state was the damaging part: the field was
        /// already assigned, so this manager then refused to connect to orchestration — while the
        /// throw had skipped the test page's own server startup, leaving the game reachable from
        /// nowhere.
        /// </remarks>
        private void EnsureRuntime()
        {
            if (ownsRuntime)
            {
                return;
            }

            scanner = new SceneScanner();
            allSceneScanner = new AllSceneScanner(scanner);
            cursorController = GetComponent<CursorController>();
            if (cursorController == null)
            {
                cursorController = gameObject.AddComponent<CursorController>();
            }

            if (GetComponent<ArtelOverlayController>() == null)
            {
                gameObject.AddComponent<ArtelOverlayController>();
            }

            if (GetComponent<KeyboardStatusController>() == null)
            {
                gameObject.AddComponent<KeyboardStatusController>();
            }

            pointerEvents = new PointerEventDispatcher();
            jsonCodec = new NewtonsoftJsonCodec();
            actionExecutor = new ActionExecutor(
                scanner,
                cursorController,
                pointerEvents,
                new ScreenCapturer(),
                // The credentials are read at upload time, not now: onboarding may still be
                // waiting for the player to sign in, and a capture asked for before that should
                // say so rather than upload with a stale value.
                new CaptureUploader(
                    jsonCodec,
                    () => server,
                    ArtelSdkSession.LoadToken,
                    ArtelSdkSession.LoadInstanceId),
                this,
                // 순회가 씬을 하나씩 띄우는 그 자리에서 화면도 한 장씩 뜬다. 같은 capturer 를 쓰는 이유는 back buffer 를
                // 읽는 경로가 하나뿐이어야 `capture_screen` 이 보는 것과 근거에 실리는 것이 갈라지지 않기 때문이다.
                new WalkedEvidenceScan(new ScreenCapturer()),
                // 캡처와 축이 다르다. 근거 문서는 살아 있는 인스턴스가 아니라 빌드에 붙으므로 gameBuildId 를 읽는다.
                new EvidenceUploader(
                    jsonCodec,
                    () => server,
                    ArtelSdkSession.LoadToken,
                    ArtelSdkSession.LoadGameBuildId));
            sceneStatePoller = new SceneStatePoller(
                scanner,
                new SceneStateHashTracker(jsonCodec),
                SceneScanIntervalSeconds);
            frameTimeRecorder = new FrameTimeRecorder();
            frameTimingSampler = new FrameTimingSampler();

            // 읽을 수 없는 플랫폼이면 null이 온다. 그 경우 보고에서 process 항목을 통째로 뺀다.
            processResourceSampler = ProcessResourceSampler.CreateForCurrentPlatform();

            var streamSignals = new WebSocketStreamSignalSender(jsonCodec, () => webSocketTransport);
            streamHost = new ArtelStreamHost(
                jsonCodec,
                streamSignals,
                new WebRtcStreamSessionFactory(this, streamSignals));

            SdkId = ArtelSdkIdentity.LoadOrCreate();
            GameVersion = Application.version;
            InstanceName = SystemInfo.deviceName;
            ownsRuntime = true;
        }

        private void OnEnable()
        {
            // Only a re-enable reaches this. The first connection is Start's, and until Start has
            // run there is nothing here to repeat.
            if (hasStarted && connectOnStart)
            {
                StartTransport();
            }
        }

        /// <summary>
        /// Opens the first connection.
        /// </summary>
        /// <remarks>
        /// Not <c>OnEnable</c>: another component in the scene may install the transport this
        /// manager should use — <c>ArtelTestPageManager</c> does, to serve its local page — and it
        /// does so from its own <c>OnEnable</c>. Unity does not order those against each other, so
        /// connecting from <c>OnEnable</c> made the winner of that race decide where the game
        /// connected: win it and this manager opened its own socket to orchestration, after which
        /// the test page stood down and was never served. The one ordering Unity does guarantee is
        /// that every <c>OnEnable</c> precedes every <c>Start</c>, so this is the earliest point at
        /// which an injected transport is certain to have arrived.
        /// </remarks>
        private void Start()
        {
            hasStarted = true;
            if (connectOnStart)
            {
                StartTransport();
            }
        }

        private void OnDisable()
        {
            // Before the transport goes: a game left frozen by pause_time can only be resumed
            // through this SDK, so shutting down while paused would strand it.
            if (actionExecutor != null)
            {
                actionExecutor.RestoreTimeScale();
            }

            StopTransport();
        }

        private void OnDestroy()
        {
            // Only the surviving manager clears the slot. A duplicate destroying
            // itself in Awake must not blank the reference to the live one, or the
            // next scene load would let a third instance through.
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            using (ArtelProfilerMarkers.ManagerUpdate.Auto())
            {
                RecordFrameTime();

                ArtelInput.AdvanceFrame();

                // Ahead of the transport check on purpose: the lease is a dead-man timer, so it has to
                // keep running when the socket is the thing that died.
                using (ArtelProfilerMarkers.ManagerPumpStreaming.Auto())
                {
                    PumpStreaming();
                }

                if (webSocketTransport == null)
                {
                    transportWasConnected = false;
                    return;
                }

                NoticeNewConnection();

                using (ArtelProfilerMarkers.ManagerHandleMessage.Auto())
                {
                    while (webSocketTransport.TryDequeueMessage(out var message))
                    {
                        HandleMessage(message);
                    }
                }

                using (ArtelProfilerMarkers.ManagerPollSceneState.Auto())
                {
                    PollSceneState();
                }

                using (ArtelProfilerMarkers.ManagerPerformanceReport.Auto())
                {
                    SendPerformanceReport();
                }
            }
        }

        /// <summary>
        /// 연결이 새로 열린 프레임에 씬 해시를 비운다.
        /// </summary>
        /// <remarks>
        /// 재연결한 서버 세션은 이 SDK 가 무엇을 띄우고 있는지 모른다. SceneStatePoller 는 마지막으로
        /// 보낸 씬의 해시를 들고 있어서, 씬이 그대로면 GAME_STATE 를 다시 보내지 않는다. 그러면
        /// 소켓만 되살아나고 새 세션은 빈 채로 남아, 에이전트가 아무것도 보지 못한 채 액션을 고른다.
        ///
        /// 상승 edge 를 여기서 재는 이유는 전송 쪽 콜백이 Unity 메인 스레드가 아니기 때문이다.
        /// Update 에서 상태를 읽으면 그 판정과 Reset 이 모두 메인 스레드에 남는다.
        /// </remarks>
        private void NoticeNewConnection()
        {
            var connected = webSocketTransport.IsConnected;

            if (connected && !transportWasConnected)
            {
                sceneStatePoller.Reset(Time.unscaledTime);
            }

            transportWasConnected = connected;
        }

        public void StartTransport()
        {
            if (webSocketTransport == null)
            {
                if (!ArtelSdkSession.TryLoadToken(out var token) ||
                    !ArtelSdkSession.TryLoadInstanceId(out var instanceId))
                {
                    Debug.LogWarning(
                        "[Artel] WebSocket transport needs a signed-in session and a registered instance.");
                    return;
                }

                webSocketTransport = new ArtelWebSocketClient(server, token, instanceId);
                ownsTransport = true;

                // This is the host game's own Player Setting, and the SDK ships inside customer
                // builds — so it is held for exactly as long as this connection, and put back in
                // StopTransport. A build that never connects to Artel keeps whatever its Player
                // Settings say.
                //
                // Without it, losing window focus stops Update, and with it the WebRTC encode
                // pump, the screen capture loop, and the drain of the incoming message queue. The
                // QA run switching to a browser to watch the stream is precisely what would kill
                // it — and nothing would come back, because the messages that drive the run are
                // read in Update too.
                //
                // Saved here rather than beside the Start call below because this block is the
                // only part of StartTransport that a second call cannot re-enter: the overlay's
                // 연결 button reaches StartTransport while already connected, and reading the
                // value there would remember the true we ourselves just wrote.
                //
                // It does nothing on mobile, where the OS suspends the app outright. What covers
                // that is StreamLease refusing to charge a suspended stretch against the lease.
                hostRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
            }

            if (!ownsTransport)
            {
                // A transport was injected by something else in the scene — ArtelTestPageManager
                // does this to serve its own local page. Saying so beats returning in silence,
                // which reads exactly like a successful connection from every layer above.
                Debug.LogWarning(
                    "[Artel] WebSocket transport is owned by another component, " +
                    "so this game will not connect to the orchestration server. " +
                    "Remove ArtelTestPageManager from the scene to connect.");

                // discovery 는 이미 따라가고 있다: 그 전송을 설치한 쪽은 SetWebSocketTransport 를 거쳤고 그것이 시작시킨다. 여기서
                // 다시 시작하면 같은 연결에 대한 두 번째 주장이 되고, 둘 중 하나가 움직이는 순간 어긋난다.
                return;
            }

            webSocketTransport.Start();
            sceneStatePoller.Reset(Time.unscaledTime);
            BeginDiscovery();
            Debug.Log("[Artel] WebSocket transport started. GAME_STATE is "
                      + (SendsGameState ? "on (restored, ARTEL-513)." : "off; read the pulse channel."));
        }

        /// <summary>
        /// 이제 지켜볼 누군가가 연결됐으므로 게임을 읽기 시작한다.
        /// </summary>
        /// <remarks>
        /// 인스턴스를 연결하는 것이 동의다. 게임은 여러 이유로 이 SDK 를 나르고 그중 하나만이 QA 다 — 화면을 스트리밍하거나 프레임
        /// 시간을 재는 프로젝트는 씬 로드마다 스캔되기를 청한 적도, 플레이어의 디스크에 리포트가 나타나기를 청한 적도 없다. 그저
        /// 게임을 시작하는 것만으로 그 둘이 다 일어나곤 했다.
        ///
        /// 전송이 존재하게 되는 자리마다 불리고, 그것은 두 곳이다: <see cref="StartTransport"/> 가 제 것을 만들 때와,
        /// <see cref="SetWebSocketTransport"/> 가 하나를 건네받을 때. 두 번째가 로컬 테스트 페이지가 연결하는 방식이고 그쪽은
        /// StartTransport 에 아예 닿지 않는다 — 그래서 앞쪽에만 둔 시작은 테스트 페이지 경로 전체를 아무도 연결되지 않은 것으로
        /// 읽는다.
        ///
        /// 완료된 핸드셰이크가 아니라 전송을 가지는 것이 방아쇠다. 소켓은 비동기로 열리고 테스트 페이지의 서버는 어떤 브라우저가
        /// 붙기 전부터 듣고 있으므로, 반대쪽 끝을 기다리면 첫 스캔이 예측할 수 없는 순간에 도착하거나 아무도 열지 않는 페이지에
        /// 대해서는 영영 오지 않는다. 연결을 청하는 것이 동의이고, 핸드셰이크는 배관이다.
        /// </remarks>
        private void BeginDiscovery()
        {
            Affordances.Scan.AffordanceBootstrap.Follow();
        }

        /// <summary>연결이 사라지면 게임 읽기를 멈춘다.</summary>
        /// <remarks>
        /// 여기서 시작시킨 것이 없는데도 판독도 여기서 멈춘다. 연결이 끊겨 끝나는 세션은 <see cref="StopReadings"/> 를 부를
        /// 기회를 얻지 못하고, 돌게 남겨진 박자는 게임이 떠 있는 내내 아무도 읽지 않을 파일에 쓴다.
        /// </remarks>
        private void EndDiscovery()
        {
            Affordances.Scan.AffordanceBootstrap.StopFollowing();
            StopReadings();
        }

        /// <summary>
        /// 라이브 판독을 시작하고, 지금 돌고 있는지를 말한다.
        /// </summary>
        /// <remarks>
        /// 연결로 함의되는 것이 아니라 청해지는 것이고, 그 분리가 이 메서드의 전부다. 연결은 도구가 봐도 된다고 말하고, 세션은
        /// 실행이 시작됐다고 말하며, 그것이 언제인지는 실행을 모는 쪽만 안다.
        ///
        /// 그 값이 얼마인지 재기 전까지 둘은 같은 순간이었다. 모든 씬을 도는 순회도 연결에서 시작하고 그것은 아무도 걸어가지 않은
        /// 화면을 방문한다 — 그래서 그 곁에서 찍은 판독은 플레이어가 본 적 없는 화면에 게임이 있다고 보고한다. 샘플 게임에서
        /// 실측했다: 순회 동안 찍은 판독은 8초에 125,548 바이트였고 플레이어가 있은 적 없는 씬 셋을 서술했다. 순회 뒤에 시작한
        /// 같은 채널은 4,369 바이트짜리 판독 하나를 쓰고 14초 동안 아무것도 쓰지 않았다.
        ///
        /// 독자가 걸러 낼 수 있는 잡음도 아니다. 판독은 자기가 순회 중이라고 말하지 않으므로 걸러 낼 근거가 그 안에 없다.
        ///
        /// 멱등이다: 이미 읽고 있는 동안의 두 번째 호출은 참으로 답하고 아무것도 바꾸지 않는다.
        /// </remarks>
        public bool StartReadings()
        {
            if (Affordances.Scan.AffordanceBootstrap.Watching)
            {
                return true;
            }

            // 연결이 있으면 판독은 그 소켓으로 나간다. 없으면 sink 를 건네지 않아 예전대로
            // 파일로 떨어진다 — 아무도 듣고 있지 않을 때에도 채널을 지켜볼 수 있어야 한다는
            // 것이 이 채널을 만들 때의 규율이고, 연결이 없다는 것이 그것을 거둘 이유는 아니다.
            var sink = webSocketTransport == null
                ? null
                : new WebSocketPulseSink(() => webSocketTransport, () => nextMessageId++);

            return Affordances.Scan.AffordanceBootstrap.WatchLiveState(sink);
        }

        /// <summary>라이브 판독을 끝낸다. 한 번도 시작하지 않았을 때 불러도 안전하다.</summary>
        public void StopReadings()
        {
            Affordances.Scan.AffordanceBootstrap.StopWatching();
        }

        /// <summary>라이브 판독이 돌고 있는지.</summary>
        internal bool Reading => Affordances.Scan.AffordanceBootstrap.Watching;

        public void StopTransport()
        {
            // A manager that lost the duplicate race in Awake returned before building any of this,
            // and is then destroyed — which calls OnDisable, which lands here. It owns no socket,
            // no stream and no dispatcher, so there is nothing to stop and every field below is
            // null.
            if (!ownsRuntime)
            {
                return;
            }

            // Before the socket goes, so the closing STREAM_STATE still has somewhere to go and
            // capture never outlives the connection that asked for it.
            streamHost.Stop();

            // Ahead of the ownership checks: whoever owns the socket, a run that ends mid-drag must
            // not leave the game holding a button nobody will ever send the release for.
            ReleaseAgentInput();

            // 게임 읽기가 그것을 청한 연결보다 오래 사는 것이 이 짝짓기가 피하려고 존재하는 값이다 — 아무도 없는데 씬 로드마다
            // 스캔하고 파일이 자라는 것.
            EndDiscovery();

            if (webSocketTransport == null)
            {
                return;
            }

            if (!ownsTransport)
            {
                return;
            }

            webSocketTransport.Stop();
            webSocketTransport.Dispose();
            webSocketTransport = null;

            // The connection this was taken for is gone, so the host game gets its setting back.
            Application.runInBackground = hostRunInBackground;

            sceneStatePoller.Reset(Time.unscaledTime);
            Debug.Log("[Artel] WebSocket transport stopped.");
        }

        /// <summary>
        /// Lets go of every key and button the agent was holding, and ends any drag in progress on
        /// the game's own terms so its handler sees the end it was waiting for.
        /// </summary>
        private void ReleaseAgentInput()
        {
            pointerEvents.ReleaseAll();
            ArtelInput.ReleaseAllVirtualInput();
        }

        internal bool HasWebSocketTransport { get { return webSocketTransport != null; } }

        /// <summary>
        /// Releases a transport this manager does not own, so the component that installed one
        /// can hand the connection back when it is switched off.
        /// </summary>
        internal void ClearWebSocketTransport(IArtelWebSocketTransport transport)
        {
            if (ownsTransport || webSocketTransport != transport)
            {
                return;
            }

            ReleaseAgentInput();
            webSocketTransport = null;
            ownsTransport = true;

            // 읽기를 청한 연결이 사라졌으므로 읽기도 함께 간다 — 이 매니저가 스스로 만든 전송에 대해 StopTransport 가 지키는 것과
            // 같은 짝짓기다.
            EndDiscovery();
        }

        /// <summary>
        /// Sends captures somewhere other than orchestration, until <see cref="RestoreCaptureUploader"/>.
        /// </summary>
        /// <remarks>
        /// 전송을 건네받는 것과 같은 짝짓기다. 그리고 같은 컴포넌트가 쓴다 — 테스트 페이지는 오케스트레이션의
        /// 티켓 엔드포인트를 못 쓴다. 그쪽은 실행 중인 QA 가 없는 인스턴스를 거절하고, 테스트 페이지에서 찍는
        /// 캡처는 전부 그 경우다.
        /// </remarks>
        internal void SetCaptureUploader(ICaptureUploader uploader)
        {
            // 전송과 같은 이유로 여기서도 부른다: 설치하는 쪽이 이 매니저의 Awake 보다 먼저 돌 수 있다.
            EnsureRuntime();
            actionExecutor.SetCaptureUploader(uploader);
        }

        internal void RestoreCaptureUploader()
        {
            if (actionExecutor == null)
            {
                return;
            }

            actionExecutor.RestoreCaptureUploader();
        }

        internal void SetWebSocketTransport(IArtelWebSocketTransport transport, bool takeOwnership)
        {
            // The installer may run before this manager's own Awake, and what follows reads state
            // that Awake builds.
            EnsureRuntime();

            if (webSocketTransport != null)
            {
                throw new InvalidOperationException("WebSocket transport is already configured.");
            }

            webSocketTransport = transport ?? throw new ArgumentNullException(nameof(transport));
            ownsTransport = takeOwnership;
            sceneStatePoller.Reset(Time.unscaledTime);

            // 주입된 전송도 다른 것과 마찬가지로 하나의 연결이다. 로컬 테스트 페이지는 여기로만 도착하고 — StartTransport 를 결코
            // 부르지 않는다 — 그래서 그 실행이 게임을 읽겠다고 청하는 누군가로 인식될 수 있는 유일한 자리가 여기다.
            BeginDiscovery();
        }

        public void SetServer(Server configuredServer)
        {
            if (webSocketTransport != null)
            {
                throw new InvalidOperationException("Server cannot change after WebSocket transport is configured.");
            }

            server = configuredServer ?? throw new ArgumentNullException(nameof(configuredServer));
        }

        private void HandleMessage(ArtelWebSocketMessage message)
        {
            try
            {
                var request = jsonCodec.Deserialize<ArtelRequestDto>(message.Text);
                if (request == null)
                {
                    throw new InvalidOperationException("Message body is empty.");
                }

                if (request.Type == "ACTION")
                {
                    EnqueueAction(request);
                    return;
                }

                if (streamHost.TryHandleMessage(request.Type, message.Text, Time.unscaledTime))
                {
                    return;
                }

                if (request.Method == "scan_scene" || request.Type == "SCAN_SCENE" || request.Type == "GET_GAME_STATE")
                {
                    ReplyWithGameState(message);
                    return;
                }

                SendError(message, "Unsupported message. Use JSON-RPC method scan_scene or ACTION.");
            }
            catch (Exception exception)
            {
                SendError(message, "Invalid message: " + exception.Message);
            }
        }

        private void EnqueueAction(ArtelRequestDto request)
        {
            actionRequests.Enqueue(request);
            if (!processingActions)
            {
                StartCoroutine(ProcessActions());
            }
        }

        private IEnumerator ProcessActions()
        {
            processingActions = true;
            while (actionRequests.Count > 0)
            {
                yield return ExecuteActionRequest(actionRequests.Dequeue());
            }

            processingActions = false;
        }

        private IEnumerator ExecuteActionRequest(ArtelRequestDto request)
        {
            var results = new List<ActionResultDto>();

            foreach (var action in request.Actions ?? new List<ActionRequestDto>())
            {
                if (action == null)
                {
                    results.Add(ActionResultDto.Failure(0, "Action item must be an object."));
                    continue;
                }

                if (action.Method == "scan_scene")
                {
                    // Scanning from inside the batch is what orders a read against the writes
                    // before it. The top-level scan path answers straight out of HandleMessage,
                    // so it can report the scene while a preceding button_click is still moving
                    // the cursor — and it consumes the pending action snapshot that click has
                    // not produced yet.
                    SendGameState();
                    results.Add(ActionResultDto.Success(action.Id));
                    continue;
                }

                if (action.Method == "scan_all_scenes")
                {
                    if (!TryReadScanOptions(action.Parameters, out var scanOptions))
                    {
                        results.Add(ActionResultDto.Failure(
                            action.Id, "scan_all_scenes params must be [] or [\"full\"]."));
                        continue;
                    }

                    List<ScannedSceneDto> scenes = null;
                    yield return allSceneScanner.ScanAll(scanOptions, result => scenes = result);
                    SendAllScenes(scenes);
                    results.Add(ActionResultDto.Success(action.Id));
                    continue;
                }

                yield return actionExecutor.Execute(
                    action.Id,
                    action.Method,
                    action.Parameters,
                    result => results.Add(result));
            }

            var response = new ActionResultMessage
            {
                Type = "ACTION_RESULT",
                Id = nextMessageId++,
                // Echoed so the caller can tell which ACTION this answers. `Id`
                // cannot serve: it is this message's own number and shares no
                // sequence with the request's.
                RequestId = request.Id,
                // 여기서 읽는다. 배치를 받은 자리가 아니라 마지막 액션이 끝난 자리다 — 커서 활강처럼
                // 여러 프레임에 걸치는 액션이 있고, 그때 둘이 갈린다. 기다리는 쪽이 궁금한 것은 배치가
                // 끝난 뒤의 화면이므로 끝난 프레임이라야 답이 된다(ARTEL-620).
                Frame = Time.frameCount,
                Results = results
            };

            if (webSocketTransport != null)
            {
                webSocketTransport.Send(jsonCodec.Serialize(response));
            }
        }

        /// <summary>
        /// Reads the optional scan mode of <c>scan_all_scenes</c>. No parameter keeps the original
        /// behaviour, so callers written before the mode existed are unaffected.
        /// </summary>
        private static bool TryReadScanOptions(List<object> parameters, out SceneScanOptions options)
        {
            options = SceneScanOptions.Default;
            if (parameters == null || parameters.Count == 0)
            {
                return true;
            }

            if (parameters.Count > 1)
            {
                return false;
            }

            var mode = parameters[0] as string;
            if (mode == "default")
            {
                return true;
            }

            if (mode == "full")
            {
                options = SceneScanOptions.Full;
                return true;
            }

            return false;
        }

        private void ReplyWithGameState(ArtelWebSocketMessage request)
        {
            // 조용히 무동작하지 않는다. 이것은 물어본 것에 대한 답이고, 답이 없으면 묻는 쪽은 화면이 비어 있는 것과
            // 채널이 꺼진 것을 가릴 수 없다 — 그 둘은 다음 수가 다르다. 오류로 답하는 것은 SendGameState 와 다른데,
            // 그쪽은 배치가 자기 몫으로 끼운 스캔이라 답을 기다리는 쪽이 없기 때문이다.
            if (!SendsGameState)
            {
                SendError(request, "GAME_STATE is switched off on this build. Read the pulse channel instead.");
                return;
            }

            var poll = sceneStatePoller.ScanNow();

            request.Reply(SerializeGameState(poll.Scene));
            poll.ScanResult.CommitActions();
        }

        private void SendGameState()
        {
            if (!SendsGameState)
            {
                return;
            }

            if (webSocketTransport == null)
            {
                return;
            }

            var poll = sceneStatePoller.ScanNow();

            webSocketTransport.Send(SerializeGameState(poll.Scene));
            poll.ScanResult.CommitActions();
        }

        private void SendAllScenes(List<ScannedSceneDto> scenes)
        {
            if (webSocketTransport == null)
            {
                return;
            }

            webSocketTransport.Send(jsonCodec.Serialize(new AllScenesMessageDto
            {
                Type = "ALL_SCENES",
                Id = nextMessageId++,
                Scenes = scenes
            }));
        }

        private void PumpStreaming()
        {
            streamHost.Tick(Time.unscaledTime);

            if (streamHost.HasLiveSession == (webRtcPump != null))
            {
                return;
            }

            if (webRtcPump == null)
            {
                // WebRTC.Update drives the plugin's per-frame encode step. It runs only while a
                // session is live, so a game that merely installs the SDK never pays for it.
                webRtcPump = StartCoroutine(WebRTC.Update());
                return;
            }

            StopCoroutine(webRtcPump);
            webRtcPump = null;
        }

        /// <summary>
        /// 전송 상태와 무관하게 매 프레임 돈다. 소켓이 끊긴 동안의 성능도 남아야 QA 런에서
        /// 끊김 구간을 설명할 수 있다.
        /// </summary>
        private void RecordFrameTime()
        {
            // timeScale이 아니라 실제 경과 시간이 필요하다. pause_time 계열 액션이 timeScale을
            // 임의로 바꾸므로 deltaTime은 프레임 성능 지표가 되지 못한다.
            //
            // 백그라운드 throttling도 사용자가 실제로 겪는 실행 상태다. 포커스 여부는 보고의
            // status.isFocused로 함께 보내므로 소비자가 필요에 따라 구분할 수 있다.
            frameTimeRecorder.Record(Time.unscaledDeltaTime);

            // 캡처만 시키고 값은 읽지 않는다. Unity의 프레임 타이밍 이력은 매 프레임 캡처해야
            // 채워지고, 읽기와 평균은 전송 게이트가 열릴 때 한 번만 돈다.
            //
            // 포커스 여부로 거르지 않는다. 프레임을 건너뛰면 이력에 구멍이 생기는 것이 아니라
            // 그만큼 오래된 프레임이 남아, 어느 구간을 잰 값인지가 흐려진다.
            frameTimingSampler.Record();
        }

        /// <summary>
        /// 전송 주기가 곧 집계 창이다. 레코더에 따로 타이머를 두면 두 주기가 어긋나 같은 구간을
        /// 두 번 보내거나 통째로 버리게 되므로, 보낼 때 그 자리에서 접는다.
        /// </summary>
        private void SendPerformanceReport()
        {
            if (!webSocketTransport.IsConnected)
            {
                // 재연결한 서버 인스턴스는 이 세션의 컨텍스트를 모른다. 끊긴 것을 본 시점에
                // 표시를 내려 두어 다음 연결에서 다시 보내게 한다.
                reportedDeviceContext = false;
                return;
            }

            if (!reportedDeviceContext)
            {
                webSocketTransport.Send(jsonCodec.Serialize(new DeviceContextMessageDto
                {
                    Type = "DEVICE_CONTEXT",
                    Id = nextMessageId++,
                    Device = RuntimeEnvironment.ReadDeviceContext()
                }));
                reportedDeviceContext = true;
            }

            var now = Time.unscaledTime;
            if (now < nextPerformanceReportTime)
            {
                return;
            }

            nextPerformanceReportTime = now + PerformanceReportIntervalSeconds;

            // CPU 비율의 분모. 보고를 걸렀는지와 무관하게 샘플러를 부를 때마다 갱신해야
            // 누적 CPU 시간과 구간 길이가 같은 창을 가리킨다.
            var elapsedSeconds = now - lastPerformanceSampleTime;
            lastPerformanceSampleTime = now;

            // 프레임이 없어 보고를 건너뛰더라도 여기서 먼저 소비한다. 뒤로 미루면 다음 구간의
            // 분모만 짧아지고 CPU 시간은 두 구간 치가 실려 사용률이 부풀려진다.
            var processUsage = default(ProcessResourceUsage);
            var hasProcessUsage =
                processResourceSampler != null &&
                processResourceSampler.TrySample(elapsedSeconds, SystemInfo.processorCount, out processUsage);

            // 예산 해석은 Screen과 QualitySettings를 읽는다. 보내는 순간에만 부른다.
            if (!frameTimeRecorder.TrySummarize(ResolveFrameBudgetSeconds(), out var frameTimes))
            {
                return;
            }

            var report = new PerformanceMessageDto
            {
                Type = "PERFORMANCE",
                Id = nextMessageId++,
                FrameTimes = FrameTimesMapper.ToDto(frameTimes),
                Status = RuntimeEnvironment.ReadStatus()
            };

            if (hasProcessUsage)
            {
                report.Process = ProcessResourcesMapper.ToDto(processUsage);
            }

            if (frameTimingSampler.TrySummarize(out var frameTiming))
            {
                report.FrameTiming = FrameTimingMapper.ToDto(frameTiming);
            }
            else
            {
                WarnFrameTimingUnavailableOnce();
            }

            // 게이트가 열린 뒤에만 읽는다. 순간값이라 누적 상태가 없어 건너뛴 프레임이 다음 값을
            // 왜곡하지 않으므로, 매 프레임 읽을 이유가 없다. 에디터 밖에서는 항상 false다.
            if (EditorRenderStatsReader.TryRead(out var editorRenderStats))
            {
                report.EditorRender = EditorRenderStatsMapper.ToDto(editorRenderStats);
            }

            webSocketTransport.Send(jsonCodec.Serialize(report));
        }

        /// <summary>
        /// Frame Timing Stats는 프로젝트 설정이라 SDK가 켤 수 없다. 꺼진 프로젝트에서는 매 초
        /// 미수집이 되므로, 고칠 방법을 한 번만 알리고 이후로는 조용히 보고에서 뺀다.
        /// </summary>
        private void WarnFrameTimingUnavailableOnce()
        {
            if (warnedFrameTimingUnavailable)
            {
                return;
            }

            warnedFrameTimingUnavailable = true;
            Debug.LogWarning(
                "[Artel] Frame timing data is unavailable, so CPU/GPU breakdown is left out of the " +
                "performance report. Enable Project Settings > Player > Frame Timing Stats to collect it.");
        }

        /// <summary>
        /// 프레임 예산. 같은 33ms라도 30fps 캡이 걸린 빌드에서는 정상이고 144Hz에서는 hitch다.
        ///
        /// vsync를 먼저 본다. Unity는 vSyncCount가 0보다 크면 targetFrameRate를 무시하므로,
        /// 반대 순서로 보면 실제로 적용되지 않는 캡을 예산으로 삼게 된다.
        /// </summary>
        private static float ResolveFrameBudgetSeconds()
        {
            var vSyncCount = QualitySettings.vSyncCount;
            if (vSyncCount > 0)
            {
                // refreshRate(int)는 2022.2에서 폐기됐다. 비율 형태가 60/1.001 같은 실제 주사율을 잃지 않는다.
                var refreshRate = Screen.currentResolution.refreshRateRatio.value;
                if (refreshRate > 0d)
                {
                    return (float)(vSyncCount / refreshRate);
                }
            }

            var targetFrameRate = Application.targetFrameRate;
            if (targetFrameRate > 0)
            {
                return 1f / targetFrameRate;
            }

            return 1f / 60f;
        }

        private void PollSceneState()
        {
            // 순회 앞에서 막는다. 여기서 나가는 것만 막으면 스캔 비용은 그대로 치르고, 그러면 이 스위치가
            // 재려는 것을 재지 못한다.
            if (!SendsGameState)
            {
                return;
            }

            if (!webSocketTransport.IsConnected)
            {
                return;
            }

            if (!sceneStatePoller.TryPoll(Time.unscaledTime, out var poll))
            {
                return;
            }

            webSocketTransport.Send(SerializeGameState(poll.Scene));
            poll.ScanResult.CommitActions();
        }

        private string SerializeGameState(SceneDto scene)
        {
            return jsonCodec.Serialize(new GameStateMessageDto
            {
                Type = "GAME_STATE",
                Id = nextMessageId++,
                Scene = scene
            });
        }

        private void SendError(ArtelWebSocketMessage request, string error)
        {
            var message = new ErrorMessage
            {
                Type = "ERROR",
                Id = nextMessageId++,
                Error = error
            };

            request.Reply(jsonCodec.Serialize(message));
        }
    }
}
