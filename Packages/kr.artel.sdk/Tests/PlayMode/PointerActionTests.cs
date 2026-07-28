using System.Collections;
using System.Collections.Generic;
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

        [TearDown]
        public void TearDown()
        {
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

            Assert.That(
                source.Events,
                // The up arrives before the end of the drag, the order Unity's input module uses.
                Is.EqualTo(new[] { "down", "beginDrag", "drag", "up", "endDrag" }));
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
