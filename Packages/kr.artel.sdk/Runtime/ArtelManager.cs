using System;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using Artel.Tracking;
using UnityEngine;

namespace Artel
{
    public sealed class ArtelManager : MonoBehaviour
    {
        private const float SceneScanIntervalSeconds = 1f;

        [SerializeField] private bool startServerOnEnable = true;
        [SerializeField] private string bindAddress = "127.0.0.1";
        [SerializeField] private int port = 17311;

        private IArtelWebSocketServer server;
        private SceneScanner scanner;
        private ActionExecutor actionExecutor;
        private IJsonCodec jsonCodec;
        private readonly SceneStateHashTracker sceneStateHashTracker = new SceneStateHashTracker();
        private long nextMessageId = 1;
        private float nextSceneScanTime;

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

            PollSceneState();
        }

        public void StartServer()
        {
            if (server != null)
            {
                return;
            }

            server = ArtelWebSocketServerFactory.Create(bindAddress, port);
            server.Start();
            sceneStateHashTracker.Reset();
            nextSceneScanTime = Time.unscaledTime + SceneScanIntervalSeconds;
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
            sceneStateHashTracker.Reset();
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
            var sceneDto = SceneSnapshotMapper.ToDto(scene.Scene);
            sceneStateHashTracker.Observe(jsonCodec.Serialize(sceneDto));

            server.Send(connection, SerializeGameState(sceneDto));
            scene.CommitActions();
        }

        private void PollSceneState()
        {
            if (Time.unscaledTime < nextSceneScanTime)
            {
                return;
            }

            nextSceneScanTime = Time.unscaledTime + SceneScanIntervalSeconds;

            var scene = scanner.Scan();
            var sceneDto = SceneSnapshotMapper.ToDto(scene.Scene);
            if (!sceneStateHashTracker.Observe(jsonCodec.Serialize(sceneDto)))
            {
                return;
            }

            server.SendToAll(SerializeGameState(sceneDto));
            scene.CommitActions();
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
