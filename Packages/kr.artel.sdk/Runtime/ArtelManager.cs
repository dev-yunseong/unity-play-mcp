using System;
using System.Collections.Generic;
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
        private long nextMessageId = 1;

        public string Url
        {
            get { return "ws://" + bindAddress + ":" + port + "/ws"; }
        }

        private void Awake()
        {
            scanner = new SceneScanner();
            actionExecutor = new ActionExecutor(scanner);
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
                var root = MiniJson.ParseObject(message.Text);
                var type = MiniJson.GetString(root, "type");
                var method = MiniJson.GetString(root, "method");

                if (type == "ACTION")
                {
                    HandleAction(root);
                    return;
                }

                if (method == "scan_scene" || type == "SCAN_SCENE" || type == "GET_GAME_STATE")
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

        private void HandleAction(Dictionary<string, object> root)
        {
            var actions = MiniJson.GetArray(root, "actions");
            var results = new List<ActionResultDto>();

            foreach (var item in actions)
            {
                if (!(item is Dictionary<string, object> action))
                {
                    results.Add(ActionResultDto.Failure(0, "Action item must be an object."));
                    continue;
                }

                var actionId = MiniJson.GetInt(action, "id", 0);
                var method = MiniJson.GetString(action, "method");
                var parameters = MiniJson.GetArray(action, "params");
                results.Add(actionExecutor.Execute(actionId, method, parameters));
            }

            var response = new ActionResultMessage
            {
                type = "ACTION_RESULT",
                id = nextMessageId++,
                results = results
            };

            server.SendToAll(JsonUtility.ToJson(response));
        }

        private void SendGameState(ArtelConnection connection)
        {
            var scene = scanner.Scan();
            var message = new GameStateMessage
            {
                type = "GAME_STATE",
                id = nextMessageId++,
                scene = scene
            };

            server.Send(connection, JsonUtility.ToJson(message));
        }

        private void SendError(ArtelConnection connection, string error)
        {
            var message = new ErrorMessage
            {
                type = "ERROR",
                id = nextMessageId++,
                error = error
            };

            server.Send(connection, JsonUtility.ToJson(message));
        }
    }
}
