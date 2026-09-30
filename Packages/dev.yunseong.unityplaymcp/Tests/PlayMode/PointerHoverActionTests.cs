using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// MCP <c>hover</c> 가 보내는 <c>move_mouse</c> 로 대상 위에 올린다 (#70).
    /// </summary>
    /// <remarks>
    /// <see cref="PointerTargetActionTests"/> 와 같은 이유로 play mode 여야 하고 같은 방식으로 돈다: host 는 프레임과
    /// <c>OnMouse*</c> 배달만 맡고, 액션은 여기서 만든 executor 가 돌려 결과 DTO 를 직접 읽는다.
    /// </remarks>
    public sealed class PointerHoverActionTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        private GameObject canvasObject;
        private ActionExecutor executor;

        [SetUp]
        public void SetUp()
        {
            foreach (var stale in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        [TearDown]
        public void TearDown()
        {
            VirtualInput.ReleaseAllVirtualInput();

            foreach (var alive in spawned)
            {
                if (alive != null)
                {
                    Object.DestroyImmediate(alive);
                }
            }

            spawned.Clear();
            canvasObject = null;
            executor = null;
        }

        /// <summary>
        /// 카드 hover 그 자체. 버튼을 누르지 않고 uGUI 의 pointer enter 만 게임에 닿는다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_EntersAuGuiCardWithoutPressingIt()
        {
            CreateRuntime();
            var card = CreateCard("hovered card", 0.5f);
            yield return null;
            IsolateFixtureRaycaster();

            var result = default(ActionResultDto);
            yield return HoverOn(card.gameObject, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(card.Events, Is.EqualTo(new[] { "enter" }));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False, "hover pressed a button");
        }

        /// <summary>
        /// 다른 카드로 옮기면 먼저 것이 exit 를 받는다. 툴팁을 닫는 게임 코드가 기대하는 순서다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_MovingToAnotherCardExitsTheFirst()
        {
            CreateRuntime();
            var first = CreateCard("first card", 0.3f);
            var second = CreateCard("second card", 0.7f);
            yield return null;
            IsolateFixtureRaycaster();

            yield return HoverOn(first.gameObject, _ => { });
            var result = default(ActionResultDto);
            yield return HoverOn(second.gameObject, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(first.Events, Is.EqualTo(new[] { "enter", "exit" }));
            Assert.That(second.Events, Is.EqualTo(new[] { "enter" }));
        }

        /// <summary>
        /// collider 대상의 hover 는 엔진과 같은 <c>OnMouseEnter</c>/<c>OnMouseOver</c> 로 닿는다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_ReachesTheOnMouseEnterOfAColliderTarget()
        {
            CreateRuntime();
            var target = CreateColliderTarget();
            yield return null;

            var result = default(ActionResultDto);
            yield return HoverOn(target.gameObject, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(target.Messages, Does.Contain("enter"), "hover did not reach OnMouseEnter");
            Assert.That(target.OverCount, Is.GreaterThan(0), "hover did not reach OnMouseOver");
            Assert.That(target.Messages, Does.Not.Contain("down"));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False);
        }

        /// <summary>
        /// MCP 의 <c>hover</c> 가 보내는 그 한 action. 자리는 <see cref="PointerTargeting.TryAim"/> 이 고르고,
        /// 좌표는 <c>move_mouse</c> 가 받는 좌상단 기준이다.
        /// </summary>
        private IEnumerator HoverOn(GameObject target, System.Action<ActionResultDto> completed)
        {
            var aimed = PointerTargeting.TryAim(
                "hover", target.GetInstanceID(), target, new PointerEventDispatcher(), out var aim, out var error);
            Assert.That(aimed, Is.True, error);

            yield return Run(
                "move_mouse",
                Params((double)aim.ScreenPosition.x, (double)(Screen.height - aim.ScreenPosition.y)),
                completed);
        }

        private void CreateRuntime()
        {
            var host = new GameObject("Unity Play MCP pointer hover test host");
            host.AddComponent<UnityPlayMcpHost>();
            spawned.Add(host);

            var cursorObject = new GameObject("pointer hover test cursor");
            var cursorController = cursorObject.AddComponent<CursorController>();
            spawned.Add(cursorObject);

            executor = new ActionExecutor(
                new TargetLookup(), cursorController, new PointerEventDispatcher());
        }

        private IEnumerator Run(
            string method, List<object> parameters, System.Action<ActionResultDto> completed)
        {
            yield return executor.Execute(NextActionId(), method, parameters, completed);
            // 결과를 받은 프레임과 마지막 OnMouse* 가 배달되는 프레임이 같아, 한 프레임을 더 준다.
            yield return null;
        }

        private static int nextActionId = 1;

        private static int NextActionId()
        {
            return nextActionId++;
        }

        private static List<object> Params(params object[] values)
        {
            return new List<object>(values);
        }

        private MouseMessageFixtureBehaviour CreateColliderTarget()
        {
            var cameraObject = new GameObject("main camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.transform.rotation = Quaternion.identity;
            spawned.Add(cameraObject);

            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            targetObject.name = "hovered collider";
            targetObject.transform.position = Vector3.zero;
            targetObject.transform.localScale = new Vector3(4f, 4f, 4f);
            spawned.Add(targetObject);

            return targetObject.AddComponent<MouseMessageFixtureBehaviour>();
        }

        /// <param name="acrossTheScreen">화면 가로에서 차지할 자리, 0 에서 1 사이.</param>
        private HoverFixtureBehaviour CreateCard(string name, float acrossTheScreen)
        {
            if (canvasObject == null)
            {
                var eventSystemObject = new GameObject("event system", typeof(EventSystem));
                spawned.Add(eventSystemObject);

                canvasObject = new GameObject(
                    "canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                spawned.Add(canvasObject);
            }

            var cardObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(HoverFixtureBehaviour));
            cardObject.transform.SetParent(canvasObject.transform, false);

            var rectTransform = cardObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(60f, 60f);
            rectTransform.anchoredPosition =
                new Vector2(Screen.width * acrossTheScreen, Screen.height * 0.5f);

            return cardObject.GetComponent<HoverFixtureBehaviour>();
        }

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
    }
}
