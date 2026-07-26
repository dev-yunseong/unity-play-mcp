using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Domain;
using Artel.Protocol.Dto;
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
        private IJsonCodec jsonCodec;
        private SceneStatePoller sceneStatePoller;
        private ArtelStreamHost streamHost;
        private Coroutine webRtcPump;
        private long nextMessageId = 1;
        private readonly Queue<ArtelRequestDto> actionRequests = new Queue<ArtelRequestDto>();
        private bool processingActions;

        public string SdkId { get; private set; }
        public string GameVersion { get; private set; }
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

            if (GetComponent<ArtelOnboardingController>() == null)
            {
                gameObject.AddComponent<ArtelOnboardingController>();
            }

            if (GetComponent<KeyboardStatusController>() == null)
            {
                gameObject.AddComponent<KeyboardStatusController>();
            }

            actionExecutor = new ActionExecutor(scanner, cursorController);
            jsonCodec = new NewtonsoftJsonCodec();
            sceneStatePoller = new SceneStatePoller(
                scanner,
                new SceneStateHashTracker(jsonCodec),
                SceneScanIntervalSeconds);

            var streamSignals = new WebSocketStreamSignalSender(jsonCodec, () => webSocketTransport);
            streamHost = new ArtelStreamHost(
                jsonCodec,
                streamSignals,
                new WebRtcStreamSessionFactory(this, streamSignals));

            SdkId = ArtelSdkIdentity.LoadOrCreate();
            GameVersion = Application.version;
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
        }

        public void StartTransport()
        {
            if (webSocketTransport == null)
            {
                if (!ArtelInstanceKey.TryLoad(out var instanceKey))
                {
                    Debug.LogWarning("[Artel] WebSocket transport needs a registered instance key.");
                    return;
                }

                webSocketTransport = new ArtelWebSocketClient(server, instanceKey);
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
            // Before the socket goes, so the closing STREAM_STATE still has somewhere to go and
            // capture never outlives the connection that asked for it.
            streamHost.Stop();

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
