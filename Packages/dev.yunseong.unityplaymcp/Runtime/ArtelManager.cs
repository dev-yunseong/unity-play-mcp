using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Capture;
using Artel.Diagnostics;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using UnityEngine;

namespace Artel
{
    public sealed class ArtelManager : MonoBehaviour, IReadingChannel
    {
        private const float PerformanceReportIntervalSeconds = 1f;
        private const string BindAddress = "127.0.0.1";
        private const int WebSocketPort = 17311;

        /// <summary>
        /// The one manager that survives scene loads. Static rather than looked up
        /// each time because the check runs in Awake, before anything else can
        /// register it.
        /// </summary>
        private static ArtelManager instance;

        private IArtelWebSocketTransport webSocketTransport;
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
        private readonly Queue<ArtelRequestDto> actionRequests = new Queue<ArtelRequestDto>();
        private bool processingActions;

        /// <summary>False on a duplicate that Awake destroyed before it built anything.</summary>
        private bool ownsRuntime;

        /// <summary>Separates the first connection, which is Start's, from a later re-enable.</summary>
        private bool hasStarted;

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

        private void OnEnable()
        {
            // Only a re-enable reaches this. The first connection is Start's, and until Start has
            // run there is nothing here to repeat.
            if (hasStarted)
            {
                StartTransport();
            }
        }

        /// <summary>Opens the WebSocket server after every component has enabled.</summary>
        private void Start()
        {
            hasStarted = true;
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
            using (ArtelProfilerMarkers.ManagerUpdate.Auto())
            {
                RecordFrameTime();

                ArtelInput.AdvanceFrame();

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

                using (ArtelProfilerMarkers.ManagerPerformanceReport.Auto())
                {
                    SendPerformanceReport();
                }
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
                webSocketTransport = new ArtelWebSocketServer(BindAddress, WebSocketPort);

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
            Debug.Log("[Artel] WebSocket server started at ws://127.0.0.1:17311/ws.");
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

                SendError(message, "Unsupported message. Use ACTION.");
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

        private void SendError(ArtelWebSocketMessage request, string error)
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
