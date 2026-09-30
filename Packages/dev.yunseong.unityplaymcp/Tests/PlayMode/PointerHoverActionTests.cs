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
    /// id 로 대상을 지정하는 <c>pointer_hover</c> 를 확인한다 (#70).
    /// </summary>
    /// <remarks>
    /// <see cref="PointerTargetActionTests"/> 와 같은 방식이다. host 는 프레임과 <c>OnMouse*</c> 배달만 맡고,
    /// action 은 여기서 만든 executor 로 실행해 결과 DTO 를 읽는다.
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
        /// 버튼을 누르지 않고 uGUI pointer enter 만 게임에 전달한다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_EntersAuGuiCardWithoutPressingIt()
        {
            CreateRuntime();
            var card = CreateCard("hovered card", 0.5f);
            yield return null;
            IsolateFixtureRaycaster();

            var result = default(ActionResultDto);
            yield return Run("pointer_hover", Params(card.gameObject.GetInstanceID()), r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(card.Events, Is.EqualTo(new[] { "enter" }));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False, "hover pressed a button");

            var hit = result.ReturnValue as PointerHitDto;
            Assert.That(hit, Is.Not.Null, "pointer_hover returned no PointerHitDto");
            Assert.That(hit.TargetId, Is.EqualTo(card.gameObject.GetInstanceID()));
            Assert.That(hit.HitId, Is.EqualTo(card.gameObject.GetInstanceID()));
            Assert.That(hit.Hit, Is.EqualTo("hovered card"));

            // 좌상단 기준 화면 좌표이고 카드 안쪽이다.
            var cardCenterFromTop = Screen.height * 0.5f;
            Assert.That(hit.X, Is.EqualTo(Screen.width * 0.5f).Within(30f));
            Assert.That(hit.Y, Is.EqualTo(cardCenterFromTop).Within(30f));
        }

        /// <summary>
        /// 다른 카드로 옮기면 이전 카드가 exit 를 받는다. 툴팁을 닫는 게임 코드가 이 순서에 의존한다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_MovingToAnotherCardExitsTheFirst()
        {
            CreateRuntime();
            var first = CreateCard("first card", 0.3f);
            var second = CreateCard("second card", 0.7f);
            yield return null;
            IsolateFixtureRaycaster();

            yield return Run("pointer_hover", Params(first.gameObject.GetInstanceID()), _ => { });
            var result = default(ActionResultDto);
            yield return Run("pointer_hover", Params(second.gameObject.GetInstanceID()), r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(first.Events, Is.EqualTo(new[] { "enter", "exit" }));
            Assert.That(second.Events, Is.EqualTo(new[] { "enter" }));
        }

        /// <summary>
        /// collider 대상 hover 는 엔진과 같이 <c>OnMouseEnter</c>/<c>OnMouseOver</c> 로 전달된다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_ReachesTheOnMouseEnterOfAColliderTarget()
        {
            CreateRuntime();
            var target = CreateColliderTarget();
            yield return null;

            var result = default(ActionResultDto);
            yield return Run("pointer_hover", Params(target.gameObject.GetInstanceID()), r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(target.Messages, Does.Contain("enter"), "pointer_hover did not reach OnMouseEnter");
            Assert.That(target.OverCount, Is.GreaterThan(0), "pointer_hover did not reach OnMouseOver");
            Assert.That(target.Messages, Does.Not.Contain("down"));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False);

            var hit = result.ReturnValue as PointerHitDto;
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit.HitId, Is.EqualTo(target.gameObject.GetInstanceID()));
        }

        /// <summary>
        /// 가려진 카드는 거절하고 가린 것의 이름을 알린다. 포인터를 옮기지 않으므로 둘 다 hover 를 받지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_RefusesACoveredCardAndNamesWhatCoveredIt()
        {
            CreateRuntime();
            var covered = CreateCard("covered card", 0.5f);
            // 나중에 만든 형제가 위에 그려져 raycast 에 먼저 맞는다.
            var coverer = CreateCard("covering card", 0.5f);
            yield return null;
            IsolateFixtureRaycaster();

            var result = default(ActionResultDto);
            yield return Run("pointer_hover", Params(covered.gameObject.GetInstanceID()), r => result = r);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("covering card"));
            Assert.That(result.Error, Does.Contain("covered card"));
            Assert.That(covered.Events, Is.Empty);
            Assert.That(coverer.Events, Is.Empty, "a refused hover must not move the pointer");
        }

        /// <summary>
        /// 비활성, 파괴, scene 변경 뒤의 id 는 다른 대상으로 풀리지 않고 각각 다른 오류로 실패한다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_TellsInactiveAndDestroyedTargetsApart()
        {
            CreateRuntime();
            var inactive = CreateCard("inactive card", 0.3f);
            var doomed = CreateCard("doomed card", 0.7f);
            yield return null;

            var inactiveId = inactive.gameObject.GetInstanceID();
            var doomedId = doomed.gameObject.GetInstanceID();
            inactive.gameObject.SetActive(false);
            Object.DestroyImmediate(doomed.gameObject);

            // 같은 이름의 새 카드를 만든다. scene 이 바뀐 뒤 같은 UI 가 다시 생기는 경우다.
            var replacement = CreateCard("doomed card", 0.7f);
            yield return null;
            IsolateFixtureRaycaster();

            var whenInactive = default(ActionResultDto);
            yield return Run("pointer_hover", Params(inactiveId), r => whenInactive = r);
            var whenDestroyed = default(ActionResultDto);
            yield return Run("pointer_hover", Params(doomedId), r => whenDestroyed = r);

            Assert.That(whenInactive.IsSuccess, Is.False);
            Assert.That(whenInactive.Error, Does.Contain("not active in the scene"));

            Assert.That(whenDestroyed.IsSuccess, Is.False);
            Assert.That(whenDestroyed.Error, Does.Contain("no live object has id"));
            Assert.That(replacement.Events, Is.Empty, "a stale id was resolved to the object that replaced it");
        }

        /// <summary>
        /// 포인터가 이동하는 사이 게임이 대상을 끄면 성공으로 보고하지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_FailsWhenTheTargetIsDeactivatedAsThePointerArrives()
        {
            CreateRuntime();
            var card = CreateCard("vanishing card", 0.5f);
            card.Entered = () => card.gameObject.SetActive(false);
            yield return null;
            IsolateFixtureRaycaster();

            var result = default(ActionResultDto);
            yield return Run("pointer_hover", Params(card.gameObject.GetInstanceID()), r => result = r);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("was deactivated while the pointer moved onto it"));
        }

        /// <summary>
        /// 포인터가 이동하는 사이 다른 것이 위에 덮이면 실제로 포인터 아래 있는 것의 이름을 알린다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerHover_FailsWhenSomethingCoversTheTargetAsThePointerArrives()
        {
            CreateRuntime();
            var card = CreateCard("buried card", 0.5f);
            yield return null;
            IsolateFixtureRaycaster();
            card.Entered = () => CreateCard("card dealt on top", 0.5f);

            var result = default(ActionResultDto);
            yield return Run("pointer_hover", Params(card.gameObject.GetInstanceID()), r => result = r);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("rests on card dealt on top"));
            Assert.That(result.Error, Does.Contain("buried card"));
        }

        [UnityTest]
        public IEnumerator PointerHover_RefusesParamsItCannotRead()
        {
            CreateRuntime();
            yield return null;

            var result = default(ActionResultDto);
            yield return Run("pointer_hover", Params("not an id"), r => result = r);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("pointer_hover requires params [targetId]"));
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
            // 결과가 온 프레임에 마지막 OnMouse* 가 배달되므로 한 프레임을 더 기다린다.
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

        /// <param name="acrossTheScreen">화면 가로 위치 비율 (0–1).</param>
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
