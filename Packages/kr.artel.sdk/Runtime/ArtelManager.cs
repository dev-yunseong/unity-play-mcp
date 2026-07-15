using System;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using UnityEngine;

namespace Artel
{
    public sealed class ArtelManager : MonoBehaviour
    {
        [SerializeField] private bool startServerOnEnable = true;
        [SerializeField] private string bindAddress = "127.0.0.1";
        [SerializeField] private int port = 17311;

        private IArtelWebSocketServer server;
        private SceneScanner scanner;
        private ActionExecutor actionExecutor;
        private IJsonCodec jsonCodec;
        private long nextMessageId = 1;

        public string Url
        {
            get { return "ws://" + bindAddress + ":" + port + "/ws"; }
        }

        private void Awake()
        {
            scanner = new SceneScanner();
            actionExecutor = new ActionExecutor(scanner);
            jsonCodec = new NewtonsoftJsonCodec();
        }

        private void OnEnable()
        {
            if (startServerOnEnable)
            {
                StartServer();
            }
        }

        private void OnDisable()
        {
            StopServer();
        }

        private void Update()
        {
            if (server == null)
            {
                return;
            }

            while (server.TryDequeueMessage(out var message))
            {
                HandleMessage(message);
            }
        }

        public void StartServer()
        {
            if (server != null)
            {
                return;
            }

            server = ArtelWebSocketServerFactory.Create(bindAddress, port);
            server.Start();
            Debug.Log("[Artel] WebSocket server started at " + Url);
        }

        public void StopServer()
        {
            if (server == null)
            {
                return;
            }

            server.Dispose();
            server = null;
            Debug.Log("[Artel] WebSocket server stopped.");
        }

        private void HandleMessage(ArtelClientMessage message)
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
                    SendGameState(message.Connection);
                    return;
                }

                SendError(message.Connection, "Unsupported message. Use JSON-RPC method scan_scene or ACTION.");
            }
            catch (Exception exception)
            {
                SendError(message.Connection, "Invalid message: " + exception.Message);
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

            server.SendToAll(jsonCodec.Serialize(response));
        }

        private void SendGameState(ArtelConnection connection)
        {
            var scene = scanner.Scan();
            var message = new GameStateMessageDto
            {
                Type = "GAME_STATE",
                Id = nextMessageId++,
                Scene = SceneSnapshotMapper.ToDto(scene.Scene)
            };

            server.Send(connection, jsonCodec.Serialize(message));
            scene.CommitActions();
        }

        private void SendError(ArtelConnection connection, string error)
        {
            var message = new ErrorMessage
            {
                Type = "ERROR",
                Id = nextMessageId++,
                Error = error
            };

            server.Send(connection, jsonCodec.Serialize(message));
        }
    }
}
