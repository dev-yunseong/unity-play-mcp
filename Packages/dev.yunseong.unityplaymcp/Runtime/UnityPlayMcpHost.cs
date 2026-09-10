using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Capture;
using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;
using UnityPlayMcp.Protocol.Mapping;
using UnityPlayMcp.Serialization;
using UnityEngine;

namespace UnityPlayMcp
{
    public sealed class UnityPlayMcpHost : MonoBehaviour, IReadingChannel
    {
        private const float PerformanceReportIntervalSeconds = 1f;
        private const string BindAddress = "127.0.0.1";
        private const int WebSocketPort = 17311;

        /// <summary>
        /// The one manager that survives scene loads. Static rather than looked up
        /// each time because the check runs in Awake, before anything else can
        /// register it.
        /// </summary>
        private static UnityPlayMcpHost instance;

        private IAgentTransport webSocketTransport;
        private ActionExecutor actionExecutor;
        private CursorController cursorController;
        private PointerEventDispatcher pointerEvents;
        private IJsonCodec jsonCodec;
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

        /// <summary>서버가 열린 동안 되돌려 줄 host game의 원래 설정.</summary>
        private bool hostRunInBackground;
        private long nextMessageId = 1;
        private readonly Queue<AgentRequestDto> actionRequests = new Queue<AgentRequestDto>();
        private bool processingActions;

        /// <summary>False on a duplicate that Awake destroyed before it built anything.</summary>
        /// <remarks>
        /// play 중 assembly reload 도 이 값을 false 로 되돌린다. serialize 되지 않는 field 라서 그렇고,
        /// 여기서는 그것이 원하는 바다: 같은 reload 에 함께 사라진 <see cref="frameTimeRecorder"/> 이하를
        /// 다시 만들라고 <see cref="OnEnable"/> 에게 말하는 것이 이 false 다.
        /// </remarks>
        private bool ownsRuntime;

        /// <summary>Separates the first connection, which is Start's, from a later re-enable.</summary>
        /// <remarks>
        /// reload 를 건너야 하는 값이 이 한 bit 뿐이라서 serialize 한다. Unity 는 play 중 assembly reload 에서
        /// <c>Awake</c> 를 다시 부르지 않고 <c>OnEnable</c> 만 부르며, 되돌려 주는 것은 serialize 된 field
        /// 뿐이다. 이 표시가 없으면 reload 뒤의 <c>OnEnable</c> 은 아직 <c>Start</c> 를 지나지 않은 host 와
        /// 구별되지 않아, 되살릴 자리인 줄 모르고 그냥 돌아간다.
        ///
        /// inspector 에 내놓을 값은 아니다. 사람이 켜고 끄는 설정이 아니라 host 가 제 이력을 적어 두는 자리다.
        /// </remarks>
        [SerializeField, HideInInspector] private bool hasStarted;

        public string GameVersion { get; private set; }
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
        /// <remarks>
        /// test 가 부를 수 있도록 <c>internal</c> 이다. hook 이 남긴 오브젝트를 나중에 관찰하는 test 는
        /// play mode 당 한 번만 도는 hook 때문에 다른 fixture 보다 먼저 돌아야 하고, 그 순서는 fixture
        /// 이름의 알파벳 순이라 이름을 바꾸는 것만으로 조용히 깨진다. 직접 부르면 순서와 무관해진다.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        internal static void SpawnInDevelopmentBuilds()
        {
            if (instance != null)
            {
                return;
            }

            new GameObject("Unity Play MCP").AddComponent<UnityPlayMcpHost>();
        }
#endif

        private void Awake()
        {
            if (!ClaimHostSlot())
            {
                return;
            }

            // The socket has to outlive the scene it was opened in. A QA run acts
            // on the game, and acting frequently loads another scene — which used
            // to destroy this object mid-run, closing the connection and failing
            // the run at exactly the moment the interesting part began.
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            EnsureRuntime();
        }

        /// <summary>
        /// 이 host 가 살아 있는 하나인지 정한다. 자리가 비어 있으면 차지하고, 다른 host 가 이미 들어 있으면
        /// 이 host 를 파괴하고 false 를 돌려준다.
        /// </summary>
        /// <remarks>
        /// scene 이 들고 온 host 가 다시 로드되면 두 번째 host 가 나타난다. 먼저 있던 쪽을 남기는 것이 살아
        /// 있는 연결을 지키는 길이다. 새로 온 쪽은 같은 port 에 두 번째 socket 을 열려다 거절당할 뿐이다.
        ///
        /// <c>Awake</c> 만이 아니라 <see cref="BeginHosting"/> 도 부른다. assembly reload 는 static 을 전부
        /// 초기값으로 되돌리므로 reload 를 건넌 host 는 자리를 잃은 채로 깨어나고, 그대로 두면 다음에 로드된
        /// scene 의 host 가 빈 자리를 차지해 두 host 가 같은 port 를 두고 다툰다.
        ///
        /// <c>instance != null</c> 은 Unity 의 비교라서 파괴된 host 를 쥔 자리는 비어 있는 것으로 읽힌다.
        /// domain reload 를 끈 project 에서 지난 play 세션의 host 가 static 에 남아 있어도 새 host 가 자리를
        /// 잡는 것은 그 덕이다.
        /// </remarks>
        private bool ClaimHostSlot()
        {
            if (instance == this)
            {
                return true;
            }

            if (instance != null)
            {
                // 끄는 것이 먼저다. Destroy 는 프레임 끝에야 처리되고 그때까지 이 host 의 Update 가 도는데,
                // 진 host 는 아무것도 만들지 않았으므로 RecordFrameTime 이 그 프레임에 바로 던진다.
                enabled = false;
                Destroy(gameObject);
                return false;
            }

            instance = this;
            return true;
        }

        /// <summary>
        /// Builds everything this manager owns, once.
        /// </summary>
        private void EnsureRuntime()
        {
            if (ownsRuntime)
            {
                return;
            }

            var targetLookup = new TargetLookup();
            cursorController = GetComponent<CursorController>();
            if (cursorController == null)
            {
                cursorController = gameObject.AddComponent<CursorController>();
            }

            if (GetComponent<KeyboardStatusController>() == null)
            {
                gameObject.AddComponent<KeyboardStatusController>();
            }

            pointerEvents = new PointerEventDispatcher();
            jsonCodec = new NewtonsoftJsonCodec();
            actionExecutor = new ActionExecutor(
                targetLookup,
                cursorController,
                pointerEvents,
                new ScreenCapturer(),
                this);
            frameTimeRecorder = new FrameTimeRecorder();
            frameTimingSampler = new FrameTimingSampler();

            // 읽을 수 없는 플랫폼이면 null이 온다. 그 경우 보고에서 process 항목을 통째로 뺀다.
            processResourceSampler = ProcessResourceSampler.CreateForCurrentPlatform();

            GameVersion = Application.version;
            ownsRuntime = true;
        }

        /// <summary>
        /// 재활성화와 assembly reload 가 함께 지나는 자리. 둘 다 host 가 쥐던 것을 여기서 다시 세운다.
        /// </summary>
        /// <remarks>
        /// play 중 assembly reload 에서 Unity 는 <c>OnDisable</c> → serialize → domain 교체 → deserialize →
        /// <c>OnEnable</c> 순으로 가고 <c>Awake</c> 는 다시 부르지 않는다. 정리는 그래서 이미 제자리에 있었다 —
        /// <see cref="OnDisable"/> 이 입력을 놓고 reading 을 끝내고 socket 을 닫는다. 없던 것은 그 반대편이다:
        /// 다시 만드는 일이 <c>Awake</c> 에만 있어서, reload 를 건넌 host 는 <see cref="frameTimeRecorder"/> 가
        /// null 인 채로 <see cref="Update"/> 만 돌았고 매 프레임 NullReferenceException 을 냈다 (issue #57).
        /// </remarks>
        private void OnEnable()
        {
            // 첫 연결은 Start 의 몫이다. Start 를 지나기 전에는 여기서 되살릴 것이 없다.
            if (!hasStarted)
            {
                return;
            }

            BeginHosting();
        }

        /// <summary>Opens the WebSocket server after every component has enabled.</summary>
        /// <remarks>
        /// 여기서도 <see cref="BeginHosting"/> 을 통째로 부르는 이유는 <c>Awake</c> 와 이 자리 사이에
        /// assembly reload 가 끼는 경우가 있어서다. scene 이 host 를 <c>enabled = false</c> 로 들고 오면
        /// <c>Awake</c> 는 그때 돌지만 <c>Start</c> 는 게임이 켤 때까지 오지 않고, 그 사이의 reload 는
        /// <c>Awake</c> 가 만든 것을 지운다. 그때 <see cref="OnEnable"/> 은 <c>hasStarted</c> 가 아직
        /// false 라 그냥 돌아가므로, 다시 세울 자리가 여기 말고 없다.
        /// </remarks>
        private void Start()
        {
            hasStarted = true;
            BeginHosting();
        }

        /// <summary>
        /// 이 host 를 살아 있는 하나로 세우고, 그것이 쥐는 것을 만들고, socket 을 연다.
        /// </summary>
        /// <remarks>
        /// 셋 다 멱등이라 이미 서 있는 host 가 다시 불러도 아무것도 달라지지 않는다. 그것이 이 메서드가
        /// <c>Start</c> 와 <see cref="OnEnable"/> 양쪽에 있어도 되는 이유이고, reload 뒤에 필요한 것도
        /// 정확히 이 셋이다 — reload 는 static slot 과 <see cref="ownsRuntime"/> 과
        /// <see cref="webSocketTransport"/> 를 함께 지운다.
        ///
        /// <see cref="EnsureRuntime"/> 이 <c>GetComponent</c> 로 찾는 <see cref="CursorController"/> 와
        /// <see cref="KeyboardStatusController"/> 는 GameObject 와 함께 살아남으므로 component 가 두 벌이
        /// 되지 않는다.
        /// </remarks>
        private void BeginHosting()
        {
            if (!ClaimHostSlot())
            {
                return;
            }

            EnsureRuntime();
            StartTransport();
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
            using (ProfilerMarkers.HostUpdate.Auto())
            {
                VirtualInput.AdvanceFrame();

                PumpTransport();

                // 성능 수집은 맨 뒤다. 여기서 던지는 것이 같은 프레임의 입력 전진과 요청 처리를 통째로
                // 막았던 것이 issue #57 이고, 그 순서에는 그럴 값이 없다 — 지표 하나를 잃는 것과 원격
                // 제어 전체를 잃는 것은 값이 다르다. 예외를 삼키지는 않는다. 삼켰다면 그 결함이 로그에
                // 남지 않아 아무도 찾지 못했을 것이다.
                RecordFrameTime();
            }
        }

        /// <summary>연결에서 온 것을 받아 처리하고, 이번 주기의 성능 보고를 내보낸다.</summary>
        private void PumpTransport()
        {
            if (webSocketTransport == null)
            {
                transportWasConnected = false;
                return;
            }

            NoticeNewConnection();

            using (ProfilerMarkers.HostHandleMessage.Auto())
            {
                while (webSocketTransport.TryDequeueMessage(out var message))
                {
                    HandleMessage(message);
                }
            }

            using (ProfilerMarkers.HostPerformanceReport.Auto())
            {
                SendPerformanceReport();
            }
        }

        /// <summary>Unity main thread에서 transport 연결 상태의 상승 edge를 기록한다.</summary>
        private void NoticeNewConnection()
        {
            var connected = webSocketTransport.IsConnected;

            transportWasConnected = connected;
        }

        public void StartTransport()
        {
            if (webSocketTransport == null)
            {
                webSocketTransport = new AgentWebSocketServer(BindAddress, WebSocketPort);

                // This is the host game's own Player Setting, and the package ships inside the
                // game build — so it is held for exactly as long as this server, and put back in
                // StopTransport. A build that never opens the server keeps whatever its Player
                // Settings say.
                //
                // Without it, losing window focus stops Update, including screen capture and the
                // drain of the incoming message queue. A coding agent often acts while the game
                // window is not focused, so that would strand the connection.
                //
                // Saved here rather than beside the Start call below because only a freshly built
                // transport should remember the host's value. StopTransport nulls the transport
                // and restores the setting together, so a re-enable arrives here with the host's
                // value back in place; reading it below instead would remember the true we
                // ourselves just wrote.
                //
                // It does nothing on mobile, where the OS suspends the app outright.
                hostRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
            }

            webSocketTransport.Start();
            BeginDiscovery();
            Debug.Log("[Unity Play MCP] WebSocket server started at ws://127.0.0.1:17311/ws.");
        }

        /// <summary>
        /// 이제 지켜볼 누군가가 연결됐으므로 게임을 읽기 시작한다.
        /// </summary>
        private void BeginDiscovery()
        {
            Affordances.Scan.AffordanceBootstrap.Follow();
        }

        /// <summary>연결이 사라지면 게임 읽기를 멈춘다.</summary>
        /// <remarks>
        /// 여기서 시작시킨 것이 없는데도 reading 도 여기서 멈춘다. 연결이 끊겨 끝나는 세션은 <see cref="StopReadings"/> 를 부를
        /// 기회를 얻지 못하고, 돌게 남겨진 박자는 게임이 떠 있는 내내 아무도 읽지 않을 파일에 쓴다.
        /// </remarks>
        private void EndDiscovery()
        {
            Affordances.Scan.AffordanceBootstrap.StopFollowing();
            StopReadings();
        }

        /// <summary>
        /// 라이브 reading 을 시작하고, 지금 돌고 있는지를 말한다.
        /// </summary>
        /// <remarks>
        /// 연결로 함의되는 것이 아니라 청해지는 것이고, 그 분리가 이 메서드의 전부다. 연결은 도구가 봐도 된다고 말하고, 세션은
        /// 실행이 시작됐다고 말하며, 그것이 언제인지는 실행을 모는 쪽만 안다.
        ///
        /// 그 값이 얼마인지 재기 전까지 둘은 같은 순간이었다. 모든 씬을 도는 순회도 연결에서 시작하고 그것은 아무도 걸어가지 않은
        /// 화면을 방문한다 — 그래서 그 곁에서 찍은 pulse 는 플레이어가 본 적 없는 화면에 게임이 있다고 보고한다. 샘플 게임에서
        /// 실측했다: 순회 동안 찍은 pulse 는 8초에 125,548 바이트였고 플레이어가 있은 적 없는 씬 셋을 서술했다. 순회 뒤에 시작한
        /// 같은 채널은 4,369 바이트짜리 pulse 하나를 쓰고 14초 동안 아무것도 쓰지 않았다.
        ///
        /// 독자가 걸러 낼 수 있는 잡음도 아니다. pulse 는 자기가 순회 중이라고 말하지 않으므로 걸러 낼 근거가 그 안에 없다.
        ///
        /// 멱등이다: 이미 읽고 있는 동안의 두 번째 호출은 참으로 답하고 아무것도 바꾸지 않는다.
        /// </remarks>
        public bool StartReadings()
        {
            if (Affordances.Scan.AffordanceBootstrap.Watching)
            {
                return true;
            }

            // 연결이 있으면 pulse 는 그 소켓으로 나간다. 없으면 sink 를 건네지 않아 예전대로
            // 파일로 떨어진다 — 아무도 듣고 있지 않을 때에도 채널을 지켜볼 수 있어야 한다는
            // 것이 이 채널을 만들 때의 규율이고, 연결이 없다는 것이 그것을 거둘 이유는 아니다.
            var sink = webSocketTransport == null
                ? null
                : new WebSocketPulseSink(() => webSocketTransport, () => nextMessageId++);

            return Affordances.Scan.AffordanceBootstrap.WatchLiveState(sink);
        }

        /// <summary>라이브 reading 을 끝낸다. 한 번도 시작하지 않았을 때 불러도 안전하다.</summary>
        public void StopReadings()
        {
            Affordances.Scan.AffordanceBootstrap.StopWatching();
        }

        /// <summary>라이브 reading 이 돌고 있는지.</summary>
        internal bool Reading => Affordances.Scan.AffordanceBootstrap.Watching;

        /// <summary>server 를 쥐고 있고 그것이 stop 되지 않았는지.</summary>
        /// <remarks>
        /// <see cref="Reading"/> 과 같은 이유로 여기에 있다. transport 는 이 class 의 private field 이고,
        /// 그것이 서 있는지를 바깥에서 물어볼 다른 방법이 없다. reload 뒤 server 가 다시 섰는지를 test 가
        /// 확인하는 자리다 — 그것을 묻지 못하면 test 는 예외가 없다는 것까지만 말할 수 있다.
        ///
        /// 누가 붙었는지는 말하지 않는다. <c>AgentWebSocketServer.IsConnected</c> 가 뜻하는 것은 server
        /// 객체가 서 있다는 것뿐이고, client 하나 없는 server 도 참으로 답한다.
        /// </remarks>
        internal bool TransportOpen => webSocketTransport != null && webSocketTransport.IsConnected;

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

            webSocketTransport.Stop();
            webSocketTransport.Dispose();
            webSocketTransport = null;

            // The connection this was taken for is gone, so the host game gets its setting back.
            Application.runInBackground = hostRunInBackground;

            Debug.Log("[Unity Play MCP] WebSocket transport stopped.");
        }

        /// <summary>
        /// Lets go of every key and button the agent was holding, and ends any drag in progress on
        /// the game's own terms so its handler sees the end it was waiting for.
        /// </summary>
        private void ReleaseAgentInput()
        {
            pointerEvents.ReleaseAll();
            VirtualInput.ReleaseAllVirtualInput();
        }

        private void HandleMessage(AgentMessage message)
        {
            try
            {
                var request = jsonCodec.Deserialize<AgentRequestDto>(message.Text);
                if (request == null)
                {
                    throw new InvalidOperationException("Message body is empty.");
                }

                if (request.Type == "ACTION")
                {
                    EnqueueAction(request);
                    return;
                }

                SendError(message, "Unsupported message. Use ACTION.");
            }
            catch (Exception exception)
            {
                SendError(message, "Invalid message: " + exception.Message);
            }
        }

        private void EnqueueAction(AgentRequestDto request)
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

        private IEnumerator ExecuteActionRequest(AgentRequestDto request)
        {
            var results = new List<ActionResultDto>();

            foreach (var action in request.Actions ?? new List<ActionRequestDto>())
            {
                if (action == null)
                {
                    results.Add(ActionResultDto.Failure(0, "Action item must be an object."));
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
                // 끝난 뒤의 화면이므로 끝난 프레임이라야 답이 된다.
                Frame = Time.frameCount,
                Results = results
            };

            if (webSocketTransport != null)
            {
                webSocketTransport.Send(jsonCodec.Serialize(response));
            }
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
                "[Unity Play MCP] Frame timing data is unavailable, so CPU/GPU breakdown is left out of the " +
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

        private void SendError(AgentMessage request, string error)
        {
            var message = new ErrorMessage
            {
                Type = "ERROR",
                Id = nextMessageId++,
                Message = error
            };

            request.Reply(jsonCodec.Serialize(message));
        }
    }
}
