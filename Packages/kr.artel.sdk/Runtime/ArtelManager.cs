using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Serialization;
using Artel.Tracking;
using UnityEngine;

namespace Artel
{
    public sealed class ArtelManager : MonoBehaviour
    {
        private const float SceneScanIntervalSeconds = 1f;

        [SerializeField] private bool connectOnEnable;
        [SerializeField] private Server server = new Server();

        private IArtelWebSocketTransport webSocketTransport;
        private bool ownsTransport = true;
        private SceneScanner scanner;
        private ActionExecutor actionExecutor;
        private CursorController cursorController;
        private IJsonCodec jsonCodec;
        private SceneStatePoller sceneStatePoller;
        private long nextMessageId = 1;
        private readonly Queue<ArtelRequestDto> actionRequests = new Queue<ArtelRequestDto>();
        private bool processingActions;

        public string SdkId { get; private set; }
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
            scanner = new SceneScanner();
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
            SdkId = ArtelSdkIdentity.LoadOrCreate();
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

        private void Update()
        {
            ArtelInput.AdvanceFrame();

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
                webSocketTransport = new ArtelWebSocketClient(server, SdkId);
                ownsTransport = true;
            }

            if (!ownsTransport)
            {
                return;
            }

            webSocketTransport.Start();
            sceneStatePoller.Reset(Time.unscaledTime);
            Debug.Log("[Artel] WebSocket transport started.");
        }

        public void StopTransport()
        {
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
                Results = results
            };

            if (webSocketTransport != null)
            {
                webSocketTransport.Send(jsonCodec.Serialize(response));
            }
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
