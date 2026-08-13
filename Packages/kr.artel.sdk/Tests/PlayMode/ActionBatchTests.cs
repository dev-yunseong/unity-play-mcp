using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Artel.Protocol.Dto;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// 배치 큐가 액션 사이사이의 스캔을 어떻게 끼워 넣는지 본다. 살아 있는 매니저가 필요해
    /// 플레이 모드에서만 돌 수 있다: <c>Awake</c>가 <c>DontDestroyOnLoad</c>를 부르는데
    /// 그건 에디터 스크립트에서 부를 수 없다.
    /// </summary>
    public sealed class ActionBatchTests
    {
        private GameObject host;
        private GameObject buttonObject;
        private GameObject labelObject;
        private ArtelManager displacedInstance;

        [SetUp]
        public void SetUp()
        {
            displacedInstance = ArtelManagerSlot.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(buttonObject);
            Object.DestroyImmediate(labelObject);
            Object.DestroyImmediate(host);

            // 이 픽스처가 만든 매니저는 방금 사라졌다. 훅이 띄운 매니저를 슬롯에 되돌려
            // 다음 픽스처가 플레이 모드 진입 직후와 같은 상태에서 시작하게 한다.
            ArtelManagerSlot.Restore(displacedInstance);
        }

        [Test]
        public void BatchScan_ObservesActionsThatRanBeforeIt()
        {
            var transport = new RecordingTransport();
            var manager = CreateManager(transport);
            var request = new ArtelRequestDto
            {
                Type = "ACTION",
                Actions = new List<ActionRequestDto>
                {
                    new ActionRequestDto { Id = 1, Method = "scan_scene" },
                    new ActionRequestDto
                    {
                        Id = 2,
                        Method = "button_click",
                        Parameters = new List<object> { buttonObject.GetInstanceID() }
                    },
                    new ActionRequestDto { Id = 3, Method = "scan_scene" }
                }
            };

            Drain(ExecuteActionRequest(manager, request));

            Assert.That(transport.Sent, Has.Count.EqualTo(3));
            Assert.That((string)JObject.Parse(transport.Sent[0])["type"], Is.EqualTo("GAME_STATE"));
            Assert.That((string)JObject.Parse(transport.Sent[1])["type"], Is.EqualTo("GAME_STATE"));
            Assert.That((string)JObject.Parse(transport.Sent[2])["type"], Is.EqualTo("ACTION_RESULT"));

            // The scan queued behind button_click sees the click; the one queued ahead of it does not.
            Assert.That(transport.Sent[0], Does.Not.Contain("\"content\":\"clicked\""));
            Assert.That(transport.Sent[1], Does.Contain("\"content\":\"clicked\""));
        }

        [Test]
        public void BatchScan_ReportsSuccessWithoutReachingActionExecutor()
        {
            var transport = new RecordingTransport();
            var manager = CreateManager(transport);
            var request = new ArtelRequestDto
            {
                Type = "ACTION",
                Actions = new List<ActionRequestDto>
                {
                    new ActionRequestDto { Id = 9, Method = "scan_scene" }
                }
            };

            Drain(ExecuteActionRequest(manager, request));

            var results = JObject.Parse(transport.Sent[1])["results"];
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That((int)results[0]["id"], Is.EqualTo(9));
            Assert.That((bool)results[0]["success"], Is.True);
            Assert.That((string)results[0]["error"], Is.Empty);
        }

        [Test]
        public void TopLevelScan_StillRepliesOnTheRequestingConnection()
        {
            var transport = new RecordingTransport();
            var manager = CreateManager(transport);
            string reply = null;
            var message = new ArtelWebSocketMessage(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"scan_scene\",\"params\":[]}",
                text => reply = text);

            typeof(ArtelManager)
                .GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { message });

            Assert.That((string)JObject.Parse(reply)["type"], Is.EqualTo("GAME_STATE"));
            Assert.That(transport.Sent, Is.Empty);
        }

        private ArtelManager CreateManager(RecordingTransport transport)
        {
            host = new GameObject("Artel action batch test");

            // 플레이 모드에서는 AddComponent가 Awake를 바로 부른다.
            var manager = host.AddComponent<ArtelManager>();
            manager.SetWebSocketTransport(transport, false);

            labelObject = new GameObject("status label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var label = labelObject.GetComponent<Text>();
            buttonObject = new GameObject(
                "click target",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.GetComponent<Button>().onClick.AddListener(() => label.text = "clicked");

            return manager;
        }

        private static IEnumerator ExecuteActionRequest(ArtelManager manager, ArtelRequestDto request)
        {
            return (IEnumerator)typeof(ArtelManager)
                .GetMethod("ExecuteActionRequest", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { request });
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }

        private sealed class RecordingTransport : IArtelWebSocketTransport
        {
            public List<string> Sent { get; } = new List<string>();

            public bool IsConnected { get { return true; } }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public bool TryDequeueMessage(out ArtelWebSocketMessage message)
            {
                message = null;
                return false;
            }

            public void Send(string text)
            {
                Sent.Add(text);
            }

            public void Dispose()
            {
            }
        }
    }
}
