using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Artel.Protocol.Dto;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// The pointer actions end to end, on a live manager. These cannot be edit-mode tests: the
    /// manager calls <c>DontDestroyOnLoad</c> in <c>Awake</c>, the cursor builds itself in its own
    /// <c>Awake</c>, and uGUI only registers its graphics for raycasting in <c>OnEnable</c>.
    /// </summary>
    public sealed class PointerActionTests
    {
        private static readonly Vector2 GrabPoint = new Vector2(100f, 100f);
        private static readonly Vector2 DropPoint = new Vector2(300f, 200f);

        private GameObject host;
        private GameObject eventSystemObject;
        private GameObject canvasObject;

        [SetUp]
        public void SetUp()
        {
            // A manager survives scene loads by design, so one left alive anywhere — by the project's
            // own scene, or by a test that ran before this one — makes the manager built below a
            // duplicate. Awake destroys duplicates, and a destroyed manager drives nothing.
            foreach (var stale in Object.FindObjectsOfType<ArtelManager>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // 가상 입력은 정적이라 테스트 사이를 넘어간다. 버튼을 쥔 채 끝난 테스트가 다음 테스트의
            // 첫 줄을 참으로 만들어 두면, 실패는 엉뚱한 곳에서 난다.
            ArtelInput.ReleaseAllVirtualInput();

            foreach (var alive in new[] { canvasObject, eventSystemObject, host })
            {
                if (alive != null)
                {
                    Object.DestroyImmediate(alive);
                }
            }
        }

        [UnityTest]
        public IEnumerator MoveMouse_ReportsEveryPositionItPutTheCursorAt()
        {
            var controller = new GameObject("cursor controller").AddComponent<CursorController>();
            host = controller.gameObject;
            var reported = new List<Vector2>();

            yield return controller.MoveTo(new Vector2(320f, 180f), reported.Add);

            var cursor = host.transform.Find("Artel Virtual Cursor Canvas/Artel Virtual Cursor");
            Assert.That(cursor, Is.Not.Null);
            Assert.That(cursor.position.x, Is.EqualTo(320f).Within(0.01f));
            Assert.That(cursor.position.y, Is.EqualTo(180f).Within(0.01f));

            // What gets reported is the cursor's own position, since that is what the virtual mouse
            // and the drag handlers are told about.
            Assert.That(reported, Is.EqualTo(new[] { new Vector2(320f, 180f) }));
        }

        [UnityTest]
        public IEnumerator KeyDown_HoldsTheKeyUntilKeyUp()
        {
            var manager = CreateManager(new RecordingTransport());

            yield return RunBatch(manager, NewAction(1, "key_down", Params("LeftShift")));
            yield return null;

            Assert.That(ArtelInput.GetKey(KeyCode.LeftShift), Is.True);

            yield return null;
            yield return null;

            // No duration was given, so only the release below can end it.
            Assert.That(ArtelInput.GetKey(KeyCode.LeftShift), Is.True);

            yield return RunBatch(manager, NewAction(2, "key_up", Params("LeftShift")));
            yield return null;

            // Only that the hold ended. Which frame reports GetKeyUp is pinned deterministically by
            // VirtualKeyboardStateTests; here the batch coroutine's own frames make it unknowable.
            Assert.That(ArtelInput.GetKey(KeyCode.LeftShift), Is.False);
        }

        [UnityTest]
        public IEnumerator OneBatch_DragsFromOneTargetToAnother()
        {
            // The whole reason drag and drop needs no action of its own: the queue runs these in
            // order, so a held button plus a move is already a drag.
            var manager = CreateManager(new RecordingTransport());
            // After a frame, so the screen the coordinates are measured against is the final one.
            yield return null;
            var source = CreateDragTarget("drag source", UnityPointOf(GrabPoint));
            var destination = CreateDragTarget("drop target", UnityPointOf(DropPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(manager, NewAction(1, "move_mouse", Coordinates(GrabPoint)));

            // Ahead of the drag, so a coordinate that landed nowhere reads as a coordinate problem
            // rather than as a drag that silently produced no events.
            Assert.That(
                (Vector2)ArtelInput.mousePosition,
                Is.EqualTo(UnityPointOf(GrabPoint)),
                "move_mouse did not land the pointer on the drag source");

            yield return RunBatch(
                manager,
                NewAction(2, "mouse_down", new List<object>()),
                NewAction(3, "move_mouse", Coordinates(DropPoint)),
                NewAction(4, "mouse_up", new List<object>()));

            // move_mouse glides, so the number of drag steps is a matter of frame rate. What has to
            // hold is the order, and that the up arrives before the end of the drag — where Unity's
            // own input module puts it.
            Assert.That(source.Events.First(), Is.EqualTo("down"));
            Assert.That(source.Events[1], Is.EqualTo("beginDrag"));
            Assert.That(source.Events, Has.Some.EqualTo("drag"));
            Assert.That(
                source.Events.Skip(2).Where(name => name != "drag"),
                Is.EqualTo(new[] { "up", "endDrag" }));
            Assert.That(destination.Events, Is.EqualTo(new[] { "drop" }));
        }

        [UnityTest]
        public IEnumerator MouseDown_HeldButtonIsReleasedWhenTheConnectionStops()
        {
            var manager = CreateManager(new RecordingTransport());
            yield return null;
            var source = CreateDragTarget("drag source", UnityPointOf(GrabPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Coordinates(GrabPoint)),
                NewAction(2, "mouse_down", new List<object>()),
                NewAction(3, "move_mouse", Coordinates(DropPoint)));
            yield return null;

            Assert.That(ArtelInput.GetMouseButton(0), Is.True);
            Assert.That(source.Events, Does.Contain("beginDrag"));

            manager.StopTransport();
            yield return null;

            // A run that ends mid-drag must not leave the game waiting for an end that never comes.
            Assert.That(source.Events, Does.Contain("endDrag"));
            Assert.That(ArtelInput.GetMouseButton(0), Is.False);
        }

        /// <summary>
        /// <c>KeyCode.Mouse0</c> 은 마우스 왼쪽 버튼 그 자체다. 키로 들어온 요청이 버튼으로 들어온
        /// 요청과 같은 곳에 닿아야, 클릭을 키코드로 읽는 게임이 에이전트를 본다.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyDownMouse0_PressesTheButtonAndFiresThePointerHandlers()
        {
            var manager = CreateManager(new RecordingTransport());
            yield return null;
            var target = CreateDragTarget("click target", UnityPointOf(GrabPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Coordinates(GrabPoint)),
                NewAction(2, "key_down", Params("Mouse0")));
            yield return null;

            Assert.That(ArtelInput.GetMouseButton(0), Is.True, "key_down did not press the button");
            Assert.That(ArtelInput.GetKey(KeyCode.Mouse0), Is.True);
            Assert.That(target.Events, Is.EqualTo(new[] { "down" }));

            yield return RunBatch(manager, NewAction(3, "key_up", Params("Mouse0")));
            yield return null;

            Assert.That(ArtelInput.GetMouseButton(0), Is.False);
            Assert.That(ArtelInput.GetKey(KeyCode.Mouse0), Is.False);
            Assert.That(target.Events, Is.EqualTo(new[] { "down", "up", "click" }));
        }

        [UnityTest]
        public IEnumerator MouseDown_IsVisibleToAGamePollingTheMouseKeyCode()
        {
            var manager = CreateManager(new RecordingTransport());

            yield return RunBatch(manager, NewAction(1, "mouse_down", Params(0d)));
            yield return null;

            // 반대 방향. 이것이 없으면 Input.GetKey(KeyCode.Mouse0) 으로 클릭을 읽는 게임은
            // mouse_down 으로 들어온 클릭을 보지 못한다.
            Assert.That(ArtelInput.GetKey(KeyCode.Mouse0), Is.True);

            yield return RunBatch(manager, NewAction(2, "mouse_up", Params(0d)));
            yield return null;

            Assert.That(ArtelInput.GetKey(KeyCode.Mouse0), Is.False);
        }

        [UnityTest]
        public IEnumerator KeyClickMouse0_LetsGoAtTheEndOfTheDurationWithBothEdgesDispatched()
        {
            var manager = CreateManager(new RecordingTransport());
            yield return null;
            var target = CreateDragTarget("click target", UnityPointOf(GrabPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Coordinates(GrabPoint)),
                NewAction(2, "key_click", Params("Mouse0", 0.05d)));
            yield return null;

            // 만료를 상태에 맡기면 놓이는 순간을 아무도 몰라 up 과 click 이 빠진다.
            Assert.That(target.Events, Is.EqualTo(new[] { "down", "up", "click" }));
            Assert.That(ArtelInput.GetMouseButton(0), Is.False);
        }

        /// <summary>
        /// 게임이 멈춰 있어도 놓여야 한다. scaled time 으로 재면 <c>pause_time</c> 이 걸린 게임에서
        /// 영영 끝나지 않고, 그 버튼은 실행이 끝날 때까지 눌린 채로 남는다.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyClickMouse0_LetsGoEvenWhileGameTimeIsFrozen()
        {
            var manager = CreateManager(new RecordingTransport());

            yield return RunBatch(
                manager,
                NewAction(1, "pause_time", new List<object>()),
                NewAction(2, "key_click", Params("Mouse0", 0.05d)),
                NewAction(3, "resume_time", new List<object>()));
            yield return null;

            Assert.That(ArtelInput.GetMouseButton(0), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator MouseDownThenKeyDownMouse0_ReachesTheHandlerOnce()
        {
            var manager = CreateManager(new RecordingTransport());
            yield return null;
            var target = CreateDragTarget("click target", UnityPointOf(GrabPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Coordinates(GrabPoint)),
                NewAction(2, "mouse_down", Params(0d)),
                NewAction(3, "key_down", Params("Mouse0")));
            yield return null;

            // 같은 버튼을 두 어휘로 눌렀을 뿐이다. 게임이 클릭을 두 번으로 세면 안 된다.
            Assert.That(target.Events, Is.EqualTo(new[] { "down" }));
        }

        [UnityTest]
        public IEnumerator KeyDownMouse1_DrivesTheRightButtonAndLeavesTheLeftAlone()
        {
            var manager = CreateManager(new RecordingTransport());

            yield return RunBatch(manager, NewAction(1, "key_down", Params("Mouse1")));
            yield return null;

            Assert.That(ArtelInput.GetMouseButton(1), Is.True);
            Assert.That(ArtelInput.GetKey(KeyCode.Mouse1), Is.True);
            Assert.That(ArtelInput.GetMouseButton(0), Is.False);
            Assert.That(ArtelInput.GetKey(KeyCode.Mouse0), Is.False);

            // Unity 의 anyKey 는 마우스 버튼도 센다.
            Assert.That(ArtelInput.anyKey, Is.True);
        }

        /// <summary>
        /// 마우스가 아닌 키는 아무것도 바뀌지 않았다. 라우팅이 너무 넓게 잡히면 여기서 걸린다.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyClick_OnANonMouseKeyStillExpiresOnItsOwn()
        {
            var manager = CreateManager(new RecordingTransport());

            yield return RunBatch(manager, NewAction(1, "key_click", Params("Space", 0.05d)));
            yield return null;

            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.True);
            Assert.That(ArtelInput.GetMouseButton(0), Is.False, "a keyboard key must not press a button");

            yield return new WaitForSecondsRealtime(0.1f);
            yield return null;

            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.False);
        }

        [UnityTest]
        public IEnumerator MoveMouse_RefusesCoordinatesItCannotRead()
        {
            var transport = new RecordingTransport();
            var manager = CreateManager(transport);

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Params(10d)),
                NewAction(2, "mouse_down", Params(9d)));

            // Not Sent[0]: a live manager also pushes GAME_STATE from its poller.
            var results = transport.FirstActionResult()["results"];
            Assert.That((bool)results[0]["success"], Is.False);
            Assert.That((string)results[0]["error"], Does.Contain("move_mouse requires params [x, y]."));
            Assert.That((bool)results[1]["success"], Is.False);
            Assert.That((string)results[1]["error"], Does.Contain("mouse_down requires params"));
        }

        /// <summary>
        /// The points in this fixture are written the way a scan reports them — pixels down from
        /// the top — because that is what move_mouse takes. The canvas the targets sit on counts up
        /// from the bottom, so placing a target means converting the other way.
        /// </summary>
        private static Vector2 UnityPointOf(Vector2 topLeftPosition)
        {
            return new Vector2(topLeftPosition.x, Screen.height - topLeftPosition.y);
        }

        private static List<object> Coordinates(Vector2 topLeftPosition)
        {
            return Params((double)topLeftPosition.x, (double)topLeftPosition.y);
        }

        private static List<object> Params(params object[] values)
        {
            return new List<object>(values);
        }

        private static ActionRequestDto NewAction(int id, string method, List<object> parameters)
        {
            return new ActionRequestDto { Id = id, Method = method, Parameters = parameters };
        }

        private static IEnumerator RunBatch(ArtelManager manager, params ActionRequestDto[] actions)
        {
            var request = new ArtelRequestDto
            {
                Type = "ACTION",
                Actions = new List<ActionRequestDto>(actions)
            };
            var routine = (IEnumerator)typeof(ArtelManager)
                .GetMethod("ExecuteActionRequest", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { request });

            yield return manager.StartCoroutine(routine);
        }

        private ArtelManager CreateManager(RecordingTransport transport)
        {
            host = new GameObject("Artel pointer action test");
            var manager = host.AddComponent<ArtelManager>();
            manager.SetWebSocketTransport(transport, false);
            return manager;
        }

        /// <summary>
        /// Leaves the fixture's canvas as the only one answering pointer rays. The onboarding canvas
        /// sits at sortingOrder short.MaxValue - 1 with a raycaster of its own, so while its panel is
        /// up it takes every ray before the game sees one. A QA run only happens once registration
        /// has dismissed it. It is built in the controller's Start, so this cannot run any earlier
        /// than the first frame.
        /// </summary>
        private void IsolateFixtureRaycaster()
        {
            foreach (var raycaster in Object.FindObjectsOfType<GraphicRaycaster>(true))
            {
                if (raycaster.transform.root != canvasObject.transform.root)
                {
                    raycaster.gameObject.SetActive(false);
                }
            }
        }

        private PointerFixtureBehaviour CreateDragTarget(string name, Vector2 screenPosition)
        {
            if (eventSystemObject == null)
            {
                eventSystemObject = new GameObject("event system", typeof(EventSystem));
                canvasObject = new GameObject(
                    "canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var targetObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(PointerFixtureBehaviour));
            targetObject.transform.SetParent(canvasObject.transform, false);

            var rectTransform = targetObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(80f, 80f);
            rectTransform.anchoredPosition = screenPosition;

            return targetObject.GetComponent<PointerFixtureBehaviour>();
        }

        private sealed class RecordingTransport : IArtelWebSocketTransport
        {
            public List<string> Sent { get; } = new List<string>();

            public bool IsConnected { get { return true; } }

            public JObject FirstActionResult()
            {
                foreach (var text in Sent)
                {
                    var message = JObject.Parse(text);
                    if ((string)message["type"] == "ACTION_RESULT")
                    {
                        return message;
                    }
                }

                throw new AssertionException("No ACTION_RESULT was sent.");
            }

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
