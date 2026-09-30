using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityPlayMcp.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// Pointer actions end to end on a live host. Play mode only: the host calls
    /// <c>DontDestroyOnLoad</c> in <c>Awake</c>, the cursor builds itself in <c>Awake</c>, and uGUI
    /// registers graphics for raycasting in <c>OnEnable</c>.
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
            // A leftover host makes the one built below a duplicate, and Awake destroys duplicates.
            foreach (var stale in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // 가상 입력은 static 이라, 버튼을 쥔 채 끝난 test 가 다음 test 를 실패시킨다.
            VirtualInput.ReleaseAllVirtualInput();

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

            var cursor = host.transform.Find("Unity Play MCP Virtual Cursor Canvas/Unity Play MCP Virtual Cursor");
            Assert.That(cursor, Is.Not.Null);
            Assert.That(cursor.position.x, Is.EqualTo(320f).Within(0.01f));
            Assert.That(cursor.position.y, Is.EqualTo(180f).Within(0.01f));

            // The cursor's own position is reported, since that is what the virtual mouse and drag
            // handlers see.
            Assert.That(reported, Is.EqualTo(new[] { new Vector2(320f, 180f) }));
        }

        [UnityTest]
        public IEnumerator KeyDown_HoldsTheKeyUntilKeyUp()
        {
            var manager = CreateManager();

            yield return RunBatch(manager, NewAction(1, "key_down", Params("LeftShift")));
            yield return null;

            Assert.That(VirtualInput.GetKey(KeyCode.LeftShift), Is.True);

            yield return null;
            yield return null;

            // No duration was given, so only the release below can end it.
            Assert.That(VirtualInput.GetKey(KeyCode.LeftShift), Is.True);

            yield return RunBatch(manager, NewAction(2, "key_up", Params("LeftShift")));
            yield return null;

            // Only that the hold ended; the exact GetKeyUp frame is covered by VirtualKeyboardStateTests.
            Assert.That(VirtualInput.GetKey(KeyCode.LeftShift), Is.False);
        }

        [UnityTest]
        public IEnumerator OneBatch_DragsFromOneTargetToAnother()
        {
            // The queue runs actions in order, so a held button plus a move is already a drag.
            var manager = CreateManager();
            // Wait a frame so the screen size used for coordinates is final.
            yield return null;
            var source = CreateDragTarget("drag source", UnityPointOf(GrabPoint));
            var destination = CreateDragTarget("drop target", UnityPointOf(DropPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(manager, NewAction(1, "move_mouse", Coordinates(GrabPoint)));

            // Checked before the drag so a bad coordinate does not look like a drag with no events.
            Assert.That(
                (Vector2)VirtualInput.mousePosition,
                Is.EqualTo(UnityPointOf(GrabPoint)),
                "move_mouse did not land the pointer on the drag source");

            yield return RunBatch(
                manager,
                NewAction(2, "mouse_down", new List<object>()),
                NewAction(3, "move_mouse", Coordinates(DropPoint)),
                NewAction(4, "mouse_up", new List<object>()));

            // The number of drag steps depends on frame rate. Only the order is fixed, with up before
            // endDrag as in Unity's own input module.
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
            var manager = CreateManager();
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

            Assert.That(VirtualInput.GetMouseButton(0), Is.True);
            Assert.That(source.Events, Does.Contain("beginDrag"));

            manager.StopTransport();
            yield return null;

            // A run that ends mid-drag must not leave the game waiting for endDrag.
            Assert.That(source.Events, Does.Contain("endDrag"));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False);
        }

        /// <summary>
        /// <c>KeyCode.Mouse0</c> 은 마우스 왼쪽 버튼이다. 키로 누른 요청도 버튼과 같은 경로로 가야
        /// 클릭을 키코드로 읽는 게임이 입력을 본다.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyDownMouse0_PressesTheButtonAndFiresThePointerHandlers()
        {
            var manager = CreateManager();
            yield return null;
            var target = CreateDragTarget("click target", UnityPointOf(GrabPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Coordinates(GrabPoint)),
                NewAction(2, "key_down", Params("Mouse0")));
            yield return null;

            Assert.That(VirtualInput.GetMouseButton(0), Is.True, "key_down did not press the button");
            Assert.That(VirtualInput.GetKey(KeyCode.Mouse0), Is.True);
            Assert.That(target.Events, Is.EqualTo(new[] { "down" }));

            yield return RunBatch(manager, NewAction(3, "key_up", Params("Mouse0")));
            yield return null;

            Assert.That(VirtualInput.GetMouseButton(0), Is.False);
            Assert.That(VirtualInput.GetKey(KeyCode.Mouse0), Is.False);
            Assert.That(target.Events, Is.EqualTo(new[] { "down", "up", "click" }));
        }

        [UnityTest]
        public IEnumerator MouseDown_IsVisibleToAGamePollingTheMouseKeyCode()
        {
            var manager = CreateManager();

            yield return RunBatch(manager, NewAction(1, "mouse_down", Params(0d)));
            yield return null;

            // 반대 방향이다. Input.GetKey(KeyCode.Mouse0) 으로 클릭을 읽는 게임도 mouse_down 을 봐야 한다.
            Assert.That(VirtualInput.GetKey(KeyCode.Mouse0), Is.True);

            yield return RunBatch(manager, NewAction(2, "mouse_up", Params(0d)));
            yield return null;

            Assert.That(VirtualInput.GetKey(KeyCode.Mouse0), Is.False);
        }

        [UnityTest]
        public IEnumerator KeyClickMouse0_LetsGoAtTheEndOfTheDurationWithBothEdgesDispatched()
        {
            var manager = CreateManager();
            yield return null;
            var target = CreateDragTarget("click target", UnityPointOf(GrabPoint));
            yield return null;
            IsolateFixtureRaycaster();

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Coordinates(GrabPoint)),
                NewAction(2, "key_click", Params("Mouse0", 0.05d)));
            yield return null;

            // 만료를 상태에만 맡기면 놓는 순간이 배달되지 않아 up 과 click 이 빠진다.
            Assert.That(target.Events, Is.EqualTo(new[] { "down", "up", "click" }));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False);
        }

        /// <summary>
        /// <c>pause_time</c> 중에도 버튼이 놓여야 한다. scaled time 으로 재면 끝나지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyClickMouse0_LetsGoEvenWhileGameTimeIsFrozen()
        {
            var manager = CreateManager();

            yield return RunBatch(
                manager,
                NewAction(1, "pause_time", new List<object>()),
                NewAction(2, "key_click", Params("Mouse0", 0.05d)),
                NewAction(3, "resume_time", new List<object>()));
            yield return null;

            Assert.That(VirtualInput.GetMouseButton(0), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator MouseDownThenKeyDownMouse0_ReachesTheHandlerOnce()
        {
            var manager = CreateManager();
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

            // 같은 버튼을 두 방식으로 눌렀으므로 클릭은 한 번만 세야 한다.
            Assert.That(target.Events, Is.EqualTo(new[] { "down" }));
        }

        [UnityTest]
        public IEnumerator KeyDownMouse1_DrivesTheRightButtonAndLeavesTheLeftAlone()
        {
            var manager = CreateManager();

            yield return RunBatch(manager, NewAction(1, "key_down", Params("Mouse1")));
            yield return null;

            Assert.That(VirtualInput.GetMouseButton(1), Is.True);
            Assert.That(VirtualInput.GetKey(KeyCode.Mouse1), Is.True);
            Assert.That(VirtualInput.GetMouseButton(0), Is.False);
            Assert.That(VirtualInput.GetKey(KeyCode.Mouse0), Is.False);

            // Unity 의 anyKey 는 마우스 버튼도 센다.
            Assert.That(VirtualInput.anyKey, Is.True);
        }

        /// <summary>
        /// 마우스가 아닌 키는 기존처럼 스스로 만료되고 버튼을 누르지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyClick_OnANonMouseKeyStillExpiresOnItsOwn()
        {
            var manager = CreateManager();

            yield return RunBatch(manager, NewAction(1, "key_click", Params("Space", 0.05d)));
            yield return null;

            Assert.That(VirtualInput.GetKey(KeyCode.Space), Is.True);
            Assert.That(VirtualInput.GetMouseButton(0), Is.False, "a keyboard key must not press a button");

            yield return new WaitForSecondsRealtime(0.1f);
            yield return null;

            Assert.That(VirtualInput.GetKey(KeyCode.Space), Is.False);
        }

        [UnityTest]
        public IEnumerator MoveMouse_RefusesCoordinatesItCannotRead()
        {
            var executor = new ActionExecutor(
                new TargetLookup(), null, new PointerEventDispatcher());

            var moveResult = RunAction(executor, 1, "move_mouse", Params(10d));
            var buttonResult = RunAction(executor, 2, "mouse_down", Params(9d));

            Assert.That(moveResult.IsSuccess, Is.False);
            Assert.That(moveResult.Error, Does.Contain("move_mouse requires params [x, y]."));
            Assert.That(buttonResult.IsSuccess, Is.False);
            Assert.That(buttonResult.Error, Does.Contain("mouse_down requires params"));
            yield break;
        }

        /// <summary>
        /// Fixture points are top-left pixels, as move_mouse takes them. The canvas counts up from the
        /// bottom, so placing a target converts the other way.
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

        private static IEnumerator RunBatch(UnityPlayMcpHost manager, params ActionRequestDto[] actions)
        {
            var request = new AgentRequestDto
            {
                Type = "ACTION",
                Actions = new List<ActionRequestDto>(actions)
            };
            var routine = (IEnumerator)typeof(UnityPlayMcpHost)
                .GetMethod("ExecuteActionRequest", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { request });

            yield return manager.StartCoroutine(routine);
        }

        private UnityPlayMcpHost CreateManager()
        {
            host = new GameObject("Unity Play MCP pointer action test");
            var manager = host.AddComponent<UnityPlayMcpHost>();
            return manager;
        }

        private static ActionResultDto RunAction(
            ActionExecutor executor, int id, string method, List<object> parameters)
        {
            ActionResultDto result = null;
            Drain(executor.Execute(id, method, parameters, value => result = value));
            return result;
        }

        /// <summary>
        /// Runs an action to its end without the coroutine scheduler.
        /// </summary>
        /// <remarks>
        /// `Execute` yields a nested enumerator, so a plain `MoveNext` loop would never run it and the
        /// result would stay null.
        /// </remarks>
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

        /// <summary>
        /// Leaves the fixture's canvas as the only one answering pointer rays, so a game canvas cannot
        /// catch the action. The host builds its runtime in <c>Start</c>, so call this after the first frame.
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

    }
}
