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
        /// The one manager that survives scene loads. Static because the check runs in Awake.
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

        /// <summary>Frame Timing Stats 경고를 한 번만 내기 위한 flag 다.</summary>
        private bool warnedFrameTimingUnavailable;
        private bool reportedDeviceContext;

        /// <summary>지난 프레임의 transport 연결 상태다.</summary>
        private bool transportWasConnected;

        /// <summary>지난 프레임까지 본 client 연결 수다. 새 client 가 붙은 프레임을 찾는 데 쓴다.</summary>
        private int clientsSeen;

        /// <summary>server 가 열린 동안 바꿔 두었다가 되돌릴 host game 의 원래 설정이다.</summary>
        private bool hostRunInBackground;
        private long nextMessageId = 1;
        private readonly Queue<AgentRequestDto> actionRequests = new Queue<AgentRequestDto>();
        private bool processingActions;

        /// <summary>False on a duplicate that Awake destroyed before it built anything.</summary>
        /// <remarks>
        /// 일부러 serialize 하지 않는다. assembly reload 뒤 false 가 되어 <see cref="OnEnable"/> 이 함께 사라진
        /// <see cref="frameTimeRecorder"/> 등을 다시 만든다.
        /// </remarks>
        private bool ownsRuntime;

        /// <summary>Separates the first connection, which is Start's, from a later re-enable.</summary>
        /// <remarks>
        /// play 중 assembly reload 는 <c>Awake</c> 없이 <c>OnEnable</c> 만 부르고 serialized field 만 복원한다.
        /// 이 값이 없으면 reload 뒤의 <c>OnEnable</c> 이 아직 <c>Start</c> 전인 host 와 구분되지 않으므로 serialize 한다.
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
        /// Editor and development builds get a manager even when no scene carries one, so a QA run can attach
        /// to any build. Compiled out of release builds. Runs after the first scene loads so a manager the
        /// scene carries keeps the spot.
        /// </summary>
        /// <remarks>
        /// play mode 당 한 번만 도는 hook 에 test 순서가 의존하지 않도록 test 가 직접 부를 수 있게 <c>internal</c> 이다.
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
            // Awake 는 새로 만들어지거나 로드된 host 에서만 불리고 reload 에서는 불리지 않는다. prefab 이나 scene 에
            // 저장된 true 가 남아 있으면 첫 OnEnable 이 Start 보다 먼저 server 를 열므로 지운다.
            hasStarted = false;

            if (!ClaimHostSlot())
            {
                return;
            }

            // The socket must outlive scene loads, which QA actions trigger often.
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            EnsureRuntime();
        }

        /// <summary>
        /// host slot 을 차지한다. 다른 host 가 이미 있으면 이 host 를 파괴하고 false 를 돌려준다.
        /// </summary>
        /// <remarks>
        /// 먼저 있던 host 를 남겨 살아 있는 연결을 지킨다.
        ///
        /// assembly reload 는 static 을 초기화하므로 <see cref="BeginHosting"/> 에서도 부른다. 그러지 않으면
        /// 다음 scene 의 host 가 빈 slot 을 차지해 두 host 가 같은 port 를 두고 다툰다.
        ///
        /// <c>instance != null</c> 은 Unity 비교라 파괴된 host 는 빈 slot 으로 읽힌다. domain reload 를 끈
        /// project 에서 지난 세션의 host 가 남아 있어도 새 host 가 slot 을 차지할 수 있다.
        /// </remarks>
        private bool ClaimHostSlot()
        {
            if (instance == this)
            {
                return true;
            }

            if (instance != null)
            {
                // Destroy 는 프레임 끝에 처리되므로 먼저 끈다. 이 host 는 아무것도 만들지 않아 Update 가 돌면 RecordFrameTime 이 던진다.
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

            // 지원하지 않는 플랫폼에서는 null 이고, 보고에서 process 항목을 뺀다.
            processResourceSampler = ProcessResourceSampler.CreateForCurrentPlatform();

            GameVersion = Application.version;
            ownsRuntime = true;
        }

        /// <summary>
        /// 재활성화와 assembly reload 뒤에 host 를 다시 세운다.
        /// </summary>
        /// <remarks>
        /// play 중 assembly reload 는 <c>Awake</c> 없이 <c>OnEnable</c> 만 부르므로 다시 만드는 일은 여기서 한다.
        /// 정리는 <see cref="OnDisable"/> 이 한다 (#57).
        /// </remarks>
        private void OnEnable()
        {
            // 첫 연결은 Start 가 연다.
            if (!hasStarted)
            {
                return;
            }

            BeginHosting();
        }

        /// <summary>Opens the WebSocket server after every component has enabled.</summary>
        /// <remarks>
        /// scene 이 host 를 <c>enabled = false</c> 로 들고 오면 <c>Awake</c> 와 <c>Start</c> 사이에 assembly reload 가
        /// 끼어 <c>Awake</c> 가 만든 것이 사라질 수 있다. 이때 <see cref="OnEnable"/> 은 <c>hasStarted</c> 가 false 라
        /// 아무것도 하지 않으므로 여기서 <see cref="BeginHosting"/> 을 부른다.
        /// </remarks>
        private void Start()
        {
            hasStarted = true;
            BeginHosting();
        }

        /// <summary>
        /// host slot 을 차지하고, runtime 을 만들고, socket 을 연다.
        /// </summary>
        /// <remarks>
        /// 세 단계 모두 멱등이라 <c>Start</c> 와 <see cref="OnEnable"/> 에서 모두 불러도 된다. reload 는 static slot,
        /// <see cref="ownsRuntime"/>, <see cref="webSocketTransport"/> 를 함께 지운다.
        ///
        /// <see cref="CursorController"/> 와 <see cref="KeyboardStatusController"/> 는 GameObject 와 함께 남으므로
        /// <see cref="EnsureRuntime"/> 의 <c>GetComponent</c> 로 찾아 중복되지 않는다.
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
            // Before the transport goes: a game frozen by pause_time can only be resumed through this SDK.
            if (actionExecutor != null)
            {
                actionExecutor.RestoreTimeScale();
            }

            StopTransport();
        }

        private void OnDestroy()
        {
            // Only the surviving manager clears the slot; a duplicate destroyed in Awake must not blank it.
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

                // 성능 수집은 마지막에 한다. 여기서 던져도 입력 처리와 요청 처리는 막히지 않는다 (#57).
                // 예외는 삼키지 않고 로그에 남긴다. 이번 프레임 샘플은 다음 보고 창에 실린다.
                RecordFrameTime();
            }
        }

        /// <summary>수신 메시지를 처리하고 이번 주기의 성능 보고를 보낸다.</summary>
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

        /// <summary>main thread 에서 transport 연결 상태와 새 client 연결을 확인한다.</summary>
        private void NoticeNewConnection()
        {
            var connected = webSocketTransport.IsConnected;

            transportWasConnected = connected;

            // socket 은 client 가 없어도 보내기에 실패하지 않으므로 Pulse 는 누락을 모른다. 새 client 에게
            // 전량 reading 을 요청하지 않으면 재연결한 MCP server 가 그 사이 변화를 받지 못한다 (#69).
            var opened = webSocketTransport.ClientsOpened;

            if (opened == clientsSeen)
            {
                return;
            }

            clientsSeen = opened;

            if (Reading)
            {
                Affordances.Scan.AffordanceBootstrap.RequestWholeReading();
            }
        }

        public void StartTransport()
        {
            if (webSocketTransport == null)
            {
                webSocketTransport = new AgentWebSocketServer(BindAddress, WebSocketPort);

                // runInBackground is the host game's own Player Setting, so it is changed only while this
                // server exists and restored in StopTransport. Without it, losing window focus stops Update,
                // including capture and message draining, and agents often act on an unfocused window.
                //
                // Saved only when a new transport is built: StopTransport restores the value when it nulls the
                // transport, so reading it elsewhere could save our own true.
                //
                // It has no effect on mobile, where the OS suspends the app.
                hostRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
            }

            webSocketTransport.Start();
            BeginDiscovery();
            Debug.Log("[Unity Play MCP] WebSocket server started at ws://127.0.0.1:17311/ws.");
        }

        /// <summary>
        /// 연결이 열렸으므로 게임 읽기를 시작한다.
        /// </summary>
        private void BeginDiscovery()
        {
            Affordances.Scan.AffordanceBootstrap.Follow();
        }

        /// <summary>연결이 사라지면 게임 읽기를 멈춘다.</summary>
        /// <remarks>
        /// 연결이 끊긴 세션은 <see cref="StopReadings"/> 를 부르지 못하므로 reading 도 여기서 멈춘다.
        /// 그러지 않으면 읽는 쪽이 없는 파일에 계속 쓴다.
        /// </remarks>
        private void EndDiscovery()
        {
            Affordances.Scan.AffordanceBootstrap.StopFollowing();
            StopReadings();
        }

        /// <summary>
        /// live reading 을 시작하고, 돌고 있는지 돌려준다.
        /// </summary>
        /// <remarks>
        /// reading 은 연결 시점이 아니라 세션 요청으로 시작한다. 연결 때 시작하는 모든 씬 순회 동안의 pulse 는
        /// 플레이어가 보지 않은 화면을 보고하고, pulse 에는 순회 중인지 표시가 없어 MCP server 가 걸러 낼 수 없다.
        ///
        /// 멱등이다. 이미 돌고 있으면 true 를 돌려준다.
        /// </remarks>
        public bool StartReadings()
        {
            if (Affordances.Scan.AffordanceBootstrap.Watching)
            {
                // 다시 요청하는 쪽은 상태가 낡았다고 의심하는 경우가 많으므로 전량 reading 을 요청한다 (#69).
                Affordances.Scan.AffordanceBootstrap.RequestWholeReading();
                return true;
            }

            // 연결이 없으면 sink 없이 파일로 쓴다. 연결이 없어도 reading 을 관찰할 수 있어야 한다.
            var sink = webSocketTransport == null
                ? null
                : new WebSocketPulseSink(() => webSocketTransport, () => nextMessageId++);

            return Affordances.Scan.AffordanceBootstrap.WatchLiveState(sink);
        }

        /// <summary>live reading 을 끝낸다. 시작한 적이 없어도 안전하다.</summary>
        public void StopReadings()
        {
            Affordances.Scan.AffordanceBootstrap.StopWatching();
        }

        /// <summary>live reading 이 돌고 있는지 여부다.</summary>
        internal bool Reading => Affordances.Scan.AffordanceBootstrap.Watching;

        /// <summary>server 가 있고 stop 되지 않았는지 여부다.</summary>
        /// <remarks>
        /// reload 뒤 server 가 다시 열렸는지 test 가 확인하는 데 쓴다. client 연결 여부는 알려 주지 않는다.
        /// <c>AgentWebSocketServer.IsConnected</c> 는 client 가 없어도 true 다.
        /// </remarks>
        internal bool TransportOpen => webSocketTransport != null && webSocketTransport.IsConnected;

        public void StopTransport()
        {
            // A manager that lost the duplicate race in Awake built nothing, but its OnDisable still lands here.
            if (!ownsRuntime)
            {
                return;
            }

            // Ahead of the ownership checks: a run ending mid-drag must not leave a button held.
            ReleaseAgentInput();

            // 연결 없이 reading 이 계속 돌면 씬 로드마다 scan 하고 파일이 커진다.
            EndDiscovery();

            if (webSocketTransport == null)
            {
                return;
            }

            webSocketTransport.Stop();
            webSocketTransport.Dispose();
            webSocketTransport = null;
            clientsSeen = 0;

            // The connection is gone, so the host game gets its setting back.
            Application.runInBackground = hostRunInBackground;

            Debug.Log("[Unity Play MCP] WebSocket transport stopped.");
        }

        /// <summary>
        /// Releases every key and button the agent held, ending any drag so the game's handler sees the end.
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
                // Echoed so the caller can match this to its ACTION; `Id` is this message's own sequence.
                RequestId = request.Id,
                // 여러 프레임에 걸친 action 이 있으므로 배치를 받은 프레임이 아니라 마지막 action 이 끝난 프레임을 보낸다.
                Frame = Time.frameCount,
                Results = results
            };

            if (webSocketTransport != null)
            {
                webSocketTransport.Send(jsonCodec.Serialize(response));
            }
        }

        /// <summary>
        /// 연결 상태와 무관하게 매 프레임 기록한다. 연결이 끊긴 구간의 성능도 남아야 한다.
        /// </summary>
        private void RecordFrameTime()
        {
            // pause_time 이 timeScale 을 바꾸므로 deltaTime 대신 실제 경과 시간을 쓴다.
            //
            // 백그라운드 throttling 도 기록한다. 포커스 여부는 status.isFocused 로 함께 보낸다.
            frameTimeRecorder.Record(Time.unscaledDeltaTime);

            // 여기서는 캡처만 한다. Unity 의 frame timing 이력은 매 프레임 캡처해야 채워지고, 읽기와 평균은 보고할 때 한다.
            //
            // 포커스 여부로 거르지 않는다. 건너뛰면 오래된 프레임이 남아 측정 구간이 불분명해진다.
            frameTimingSampler.Record();
        }

        /// <summary>
        /// 보고 주기가 집계 창이다. recorder 에 별도 타이머를 두면 주기가 어긋나 구간이 중복되거나 빠지므로
        /// 보낼 때 집계한다.
        /// </summary>
        private void SendPerformanceReport()
        {
            if (!webSocketTransport.IsConnected)
            {
                // 재연결한 서버는 이 세션의 device context 를 모르므로 다음 연결에서 다시 보낸다.
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

            // CPU 비율의 분모다. 샘플러를 부를 때마다 갱신해야 누적 CPU 시간과 같은 구간을 가리킨다.
            var elapsedSeconds = now - lastPerformanceSampleTime;
            lastPerformanceSampleTime = now;

            // 보고를 건너뛰더라도 먼저 샘플링한다. 미루면 CPU 시간이 두 구간 치가 실려 사용률이 부풀려진다.
            var processUsage = default(ProcessResourceUsage);
            var hasProcessUsage =
                processResourceSampler != null &&
                processResourceSampler.TrySample(elapsedSeconds, SystemInfo.processorCount, out processUsage);

            // 예산 계산은 Screen 과 QualitySettings 를 읽으므로 보낼 때만 부른다.
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

            // 순간값이라 보고할 때만 읽는다. 에디터 밖에서는 항상 false 다.
            if (EditorRenderStatsReader.TryRead(out var editorRenderStats))
            {
                report.EditorRender = EditorRenderStatsMapper.ToDto(editorRenderStats);
            }

            webSocketTransport.Send(jsonCodec.Serialize(report));
        }

        /// <summary>
        /// Frame Timing Stats 는 SDK 가 켤 수 없는 project 설정이다. 꺼져 있으면 해결 방법을 한 번만 알리고 보고에서 뺀다.
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
        /// 프레임 예산이다. 같은 33ms 라도 30fps 캡에서는 정상이고 144Hz 에서는 hitch 다.
        ///
        /// Unity 는 vSyncCount 가 0 보다 크면 targetFrameRate 를 무시하므로 vsync 를 먼저 본다.
        /// </summary>
        private static float ResolveFrameBudgetSeconds()
        {
            var vSyncCount = QualitySettings.vSyncCount;
            if (vSyncCount > 0)
            {
                // refreshRate(int)는 2022.2 에서 폐기됐다. 비율 형태는 60/1.001 같은 실제 주사율을 유지한다.
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
