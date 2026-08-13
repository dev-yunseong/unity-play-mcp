using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Auth;
using Artel.Capture;
using Artel.Diagnostics;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using Artel.Streaming;
using Artel.Tracking;
using Unity.WebRTC;
using UnityEngine;

namespace Artel
{
    public sealed class ArtelManager : MonoBehaviour
    {
        private const float SceneScanIntervalSeconds = 1f;
        private const float PerformanceReportIntervalSeconds = 1f;

        /// <summary>
        /// The one manager that survives scene loads. Static rather than looked up
        /// each time because the check runs in Awake, before anything else can
        /// register it.
        /// </summary>
        private static ArtelManager instance;

        [SerializeField] private bool connectOnEnable;
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
        private ArtelStreamHost streamHost;
        private Coroutine webRtcPump;
        private long nextMessageId = 1;
        private readonly Queue<ArtelRequestDto> actionRequests = new Queue<ArtelRequestDto>();
        private bool processingActions;

        /// <summary>False on a duplicate that Awake destroyed before it built anything.</summary>
        private bool ownsRuntime;

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
                    ArtelSdkSession.LoadInstanceId));
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
            if (connectOnEnable)
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
            RecordFrameTime();

            ArtelInput.AdvanceFrame();

            // Ahead of the transport check on purpose: the lease is a dead-man timer, so it has to
            // keep running when the socket is the thing that died.
            PumpStreaming();

            if (webSocketTransport == null)
            {
                return;
            }

            while (webSocketTransport.TryDequeueMessage(out var message))
            {
                HandleMessage(message);
            }

            PollSceneState();
            SendPerformanceReport();
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
                return;
            }

            webSocketTransport.Start();
            sceneStatePoller.Reset(Time.unscaledTime);
            Debug.Log("[Artel] WebSocket transport started.");
        }

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
        }

        internal void SetWebSocketTransport(IArtelWebSocketTransport transport, bool takeOwnership)
        {
            if (webSocketTransport != null)
            {
                throw new InvalidOperationException("WebSocket transport is already configured.");
            }

            webSocketTransport = transport ?? throw new ArgumentNullException(nameof(transport));
            ownsTransport = takeOwnership;
            sceneStatePoller.Reset(Time.unscaledTime);
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
            var poll = sceneStatePoller.ScanNow();

            request.Reply(SerializeGameState(poll.Scene));
            poll.ScanResult.CommitActions();
        }

        private void SendGameState()
        {
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
            // isFocused는 에디터에서 에디터 애플리케이션의 포커스를 뜻한다. Game view가 아니라
            // 창 기준이라 작업 중에는 대체로 true이고, 다른 앱으로 넘어간 동안만 빠진다.
            frameTimeRecorder.Record(Time.unscaledDeltaTime, Application.isFocused);

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
