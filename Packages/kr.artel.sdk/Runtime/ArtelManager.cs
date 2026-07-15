using System;
using System.Collections.Generic;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using UnityEngine;

namespace Artel
{
    public sealed class ArtelManager : MonoBehaviour
    {
        [SerializeField] private bool connectOnEnable;
        [SerializeField] private Server server = new Server();

        private IArtelWebSocketTransport webSocketTransport;
        private bool ownsTransport = true;
        private SceneScanner scanner;
        private ActionExecutor actionExecutor;
        private IJsonCodec jsonCodec;
        private long nextMessageId = 1;

        public string SdkId { get; private set; }
        public Server Server { get { return server; } }

        private void Awake()
        {
            scanner = new SceneScanner();
            actionExecutor = new ActionExecutor(scanner);
            jsonCodec = new NewtonsoftJsonCodec();
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
            if (webSocketTransport == null)
            {
                return;
            }

            while (webSocketTransport.TryDequeueMessage(out var message))
            {
                HandleMessage(message);
            }
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
                    HandleAction(request);
                    return;
                }

                if (request.Method == "scan_scene" || request.Type == "SCAN_SCENE" || request.Type == "GET_GAME_STATE")
                {
                    SendGameState(message);
                    return;
                }

                SendError(message, "Unsupported message. Use JSON-RPC method scan_scene or ACTION.");
            }
            catch (Exception exception)
            {
                SendError(message, "Invalid message: " + exception.Message);
            }
        }

        private void HandleAction(ArtelRequestDto request)
        {
            var results = new List<ActionResultDto>();

            foreach (var action in request.Actions ?? new List<ActionRequestDto>())
            {
                if (action == null)
                {
                    results.Add(ActionResultDto.Failure(0, "Action item must be an object."));
                    continue;
                }

                results.Add(actionExecutor.Execute(action.Id, action.Method, action.Parameters));
            }

            var response = new ActionResultMessage
            {
                Type = "ACTION_RESULT",
                Id = nextMessageId++,
                Results = results
            };

            webSocketTransport.Send(jsonCodec.Serialize(response));
        }

        private void SendGameState(ArtelWebSocketMessage request)
        {
            var scene = scanner.Scan();
            var message = new GameStateMessageDto
            {
                Type = "GAME_STATE",
                Id = nextMessageId++,
                Scene = SceneSnapshotMapper.ToDto(scene.Scene)
            };

            request.Reply(jsonCodec.Serialize(message));
            scene.CommitActions();
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
