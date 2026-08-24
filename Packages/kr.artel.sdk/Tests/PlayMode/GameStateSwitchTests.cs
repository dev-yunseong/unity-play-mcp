using System.Collections.Generic;
using System.Reflection;
using Artel.Protocol.Dto;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// <c>GAME_STATE</c> 채널을 끌 수 있다 (ARTEL-513).
    ///
    /// <b>임시 스위치의 테스트다.</b> 폐기는 ARTEL-400 이고 그때 이 파일도 함께 사라진다. 그때까지 지키는 것은
    /// "끄면 정말 안 나가고, 켜져 있으면 아무것도 달라지지 않는다" 둘이다 — 기본값이 무엇을 바꾸면 이 스위치가
    /// 재려던 것 대신 배포 사고를 만든다.
    ///
    /// 살아 있는 매니저가 필요해 플레이 모드에서만 돈다. <c>Awake</c> 가 <c>DontDestroyOnLoad</c> 를 부르는데
    /// 그것은 에디터 스크립트에서 부를 수 없다.
    /// </summary>
    public sealed class GameStateSwitchTests
    {
        private GameObject host;
        private ArtelManager displacedInstance;
        private bool wasSending;

        [SetUp]
        public void SetUp()
        {
            displacedInstance = ArtelManagerSlot.Clear();
            // 정적 스위치다. 되돌리지 않으면 다음 픽스처가 이 테스트의 선택을 물려받는다.
            wasSending = ArtelManager.SendsGameState;
        }

        [TearDown]
        public void TearDown()
        {
            ArtelManager.SendsGameState = wasSending;
            Object.DestroyImmediate(host);
            ArtelManagerSlot.Restore(displacedInstance);
        }

        /// <summary>
        /// 기본은 켜짐이다. 이 스위치가 배포의 부수 효과로 QA 를 바꾸면 안 된다 — 끄는 것은 재려는 사람의 선택이다.
        /// </summary>
        [Test]
        public void SendsGameState_DefaultsToOn()
        {
            Assert.That(wasSending, Is.True);
        }

        /// <summary>배치 안의 <c>scan_scene</c> 은 배치가 자기 몫으로 끼운 것이라 답을 기다리는 쪽이 없다. 조용히 건너뛴다.</summary>
        [Test]
        public void BatchScan_SendsNothing_WhenSwitchedOff()
        {
            var transport = new RecordingTransport();
            var manager = CreateManager(transport);
            ArtelManager.SendsGameState = false;

            RunBatch(manager, new ArtelRequestDto
            {
                Type = "ACTION",
                Actions = new List<ActionRequestDto>
                {
                    new ActionRequestDto { Id = 1, Method = "scan_scene" }
                }
            });

            Assert.That(FramesOfType(transport, "GAME_STATE"), Is.Empty);
        }

        /// <summary>같은 배치가 켜져 있을 때는 종전대로 화면을 낸다. 끄는 것이 무엇을 막는지 이 대조가 정한다.</summary>
        [Test]
        public void BatchScan_StillSends_WhenSwitchedOn()
        {
            var transport = new RecordingTransport();
            var manager = CreateManager(transport);
            ArtelManager.SendsGameState = true;

            RunBatch(manager, new ArtelRequestDto
            {
                Type = "ACTION",
                Actions = new List<ActionRequestDto>
                {
                    new ActionRequestDto { Id = 1, Method = "scan_scene" }
                }
            });

            Assert.That(FramesOfType(transport, "GAME_STATE"), Is.Not.Empty);
        }

        /// <summary>
        /// 최상위 <c>scan_scene</c> 은 물어본 것이므로 조용히 무동작하지 않는다.
        ///
        /// 답이 없으면 묻는 쪽은 <b>화면이 비어 있는 것</b>과 <b>채널이 꺼진 것</b>을 가릴 수 없고, 그 둘은 다음 수가
        /// 다르다. 에이전트가 부르는 경로라 여기서 죽으면 런이 깨진다.
        /// </summary>
        [Test]
        public void TopLevelScan_AnswersWithAnError_WhenSwitchedOff()
        {
            var transport = new RecordingTransport();
            var manager = CreateManager(transport);
            ArtelManager.SendsGameState = false;
            string reply = null;
            var message = new ArtelWebSocketMessage(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"scan_scene\",\"params\":[]}",
                text => reply = text);

            typeof(ArtelManager)
                .GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { message });

            Assert.That(reply, Is.Not.Null, "꺼져 있어도 답은 온다");
            var parsed = JObject.Parse(reply);
            Assert.That((string)parsed["type"], Is.EqualTo("ERROR"));
            Assert.That((string)parsed["error"], Does.Contain("pulse"),
                "무엇을 대신 읽어야 하는지 말한다");
        }

        private static IEnumerable<string> FramesOfType(RecordingTransport transport, string type)
        {
            var found = new List<string>();
            foreach (var frame in transport.Sent)
            {
                if ((string)JObject.Parse(frame)["type"] == type)
                {
                    found.Add(frame);
                }
            }

            return found;
        }

        private static void RunBatch(ArtelManager manager, ArtelRequestDto request)
        {
            var routine = (System.Collections.IEnumerator)typeof(ArtelManager)
                .GetMethod("ExecuteActionRequest", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { request });

            Drain(routine);
        }

        private static void Drain(System.Collections.IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is System.Collections.IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }

        private ArtelManager CreateManager(RecordingTransport transport)
        {
            host = new GameObject("Artel game state switch test");

            var manager = host.AddComponent<ArtelManager>();
            manager.SetWebSocketTransport(transport, false);
            return manager;
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
