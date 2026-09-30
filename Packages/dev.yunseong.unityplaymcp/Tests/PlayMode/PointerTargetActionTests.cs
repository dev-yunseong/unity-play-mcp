using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityPlayMcp.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// MCP <c>click</c> 과 <c>drag</c> 가 보내는 <c>move_mouse</c>/<c>mouse_down</c>/<c>mouse_up</c>.
    /// </summary>
    /// <remarks>
    /// 에디트 모드로 내려올 수 없다. <c>OnMouse*</c> 를 배달하는 것은 host 의 <c>Update</c> 안
    /// <c>VirtualInput.AdvanceFrame</c> 이고, uGUI 는 <c>OnEnable</c> 에서야 graphic 을 raycast
    /// 대상으로 등록한다.
    /// <para>
    /// host 는 프레임을 돌리는 데만 쓰고 액션은 이 테스트가 만든 <see cref="ActionExecutor"/> 로
    /// 직접 돌린다. 결과 DTO 를 읽어야 하는데 host 의 배치 실행은 결과를 소켓으로만 내보내기
    /// 때문이고, 덤으로 host 의 private 메서드에 reflection 으로 손을 넣지 않게 된다.
    /// </para>
    /// </remarks>
    public sealed class PointerTargetActionTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        private GameObject host;
        private GameObject canvasObject;
        private CursorController cursorController;
        private ActionExecutor executor;

        [SetUp]
        public void SetUp()
        {
            // 씬을 넘어 살아남는 host 가 하나라도 남아 있으면 아래에서 만드는 것이 중복이 되고,
            // 중복은 Awake 에서 스스로를 파괴한다. 파괴된 host 는 프레임을 돌리지 않는다.
            foreach (var stale in Object.FindObjectsOfType<UnityPlayMcpHost>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // 가상 입력은 정적이라 테스트 사이를 넘어간다. 버튼을 쥔 채 끝난 테스트가 다음
            // 테스트를 엉뚱한 곳에서 실패시킨다.
            VirtualInput.ReleaseAllVirtualInput();

            foreach (var alive in spawned)
            {
                if (alive != null)
                {
                    Object.DestroyImmediate(alive);
                }
            }

            spawned.Clear();
            host = null;
            canvasObject = null;
            cursorController = null;
            executor = null;
        }

        /// <summary>
        /// issue #59 그 자체. collider 로만 입력을 받는 오브젝트를 ID 로 클릭한다. 예전에는
        /// <c>Target is not a Button</c> 으로 끝나던 자리다.
        /// </summary>
        [UnityTest]
        public IEnumerator Click_ReachesTheOnMouseHandlersOfAColliderTarget()
        {
            CreateRuntime();
            var target = CreateColliderTarget();
            yield return null;

            var result = default(ActionResultDto);
            yield return ClickOn(target.gameObject, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(target.Messages, Does.Contain("down"), "click did not reach OnMouseDown");
            Assert.That(target.Messages, Does.Contain("up"));
            // 누른 그 오브젝트 위에서 놓였으므로 엔진이라면 여기서 버튼 클릭을 알린다.
            Assert.That(target.Messages, Does.Contain("upAsButton"));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False, "the button was left held");
        }

        /// <summary>
        /// collider 가 자식에 앉은 배치. 스프라이트 하나에 빈 부모를 씌우는 흔한 모양이고, scan 이
        /// 보고하는 id 는 대개 그 부모다. 엔진이 고르는 것은 자식이므로 자식이 답해도 성공이다 —
        /// 사람이 같은 자리를 클릭해도 같은 자식이 받는다.
        /// </summary>
        [UnityTest]
        public IEnumerator Click_ReachesATargetWhoseColliderSitsOnAChild()
        {
            CreateRuntime();
            var child = CreateColliderTarget();
            var parent = new GameObject("collider parent");
            spawned.Add(parent);
            parent.transform.position = Vector3.zero;
            child.transform.SetParent(parent.transform, true);
            yield return null;

            var result = default(ActionResultDto);
            yield return ClickOn(parent, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(child.Messages, Does.Contain("down"));
        }

        [UnityTest]
        public IEnumerator Click_ReachesAuGuiPointerHandler()
        {
            CreateRuntime();
            var target = CreateGraphicTarget("click target", 0.5f);
            yield return null;
            IsolateFixtureRaycaster();

            var result = default(ActionResultDto);
            yield return ClickOn(target.gameObject, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(target.Events, Is.EqualTo(new[] { "down", "up", "click" }));
        }

        /// <summary>
        /// <c>Button</c> 도 실제 입력으로 닿는다. <c>onClick</c> 을 직접
        /// 부르는 것이 아니라 포인터가 눌러서 나는 클릭이다.
        /// </summary>
        [UnityTest]
        public IEnumerator Click_PressesAButtonThroughTheRealInputPath()
        {
            CreateRuntime();
            var target = CreateGraphicTarget("button target", 0.5f);
            var button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = target.GetComponent<Image>();
            var clicks = 0;
            button.onClick.AddListener(() => clicks++);
            yield return null;
            IsolateFixtureRaycaster();

            var result = default(ActionResultDto);
            yield return ClickOn(target.gameObject, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PointerDrag_CarriesTheDragFromTheSourceToTheTarget()
        {
            CreateRuntime();
            var source = CreateGraphicTarget("drag source", 0.25f);
            var destination = CreateGraphicTarget("drop target", 0.75f);
            yield return null;
            IsolateFixtureRaycaster();

            var result = default(ActionResultDto);
            yield return DragBetween(source.gameObject, destination.gameObject, r => result = r);

            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(source.Events.First(), Is.EqualTo("down"));
            Assert.That(source.Events[1], Is.EqualTo("beginDrag"));
            Assert.That(source.Events, Has.Some.EqualTo("drag"));
            // 활강의 걸음 수는 프레임 속도가 정한다. 못 박을 수 있는 것은 순서와, up 이 endDrag
            // 앞에 온다는 것 — Unity 자신의 input module 이 두는 자리다.
            Assert.That(
                source.Events.Skip(2).Where(name => name != "drag"),
                Is.EqualTo(new[] { "up", "endDrag" }));
            Assert.That(destination.Events, Is.EqualTo(new[] { "drop" }));
            Assert.That(VirtualInput.GetMouseButton(0), Is.False, "the drag left the button held");
        }

        /// <summary>
        /// pointer capture. 포인터가 원본을 떠난 뒤에도 드래그는 원본이 받는다. 이것이 없으면
        /// 포인터가 경계를 넘는 순간 드래그가 끊긴다.
        /// </summary>
        [UnityTest]
        public IEnumerator PointerDrag_KeepsSendingToTheSourceAfterThePointerHasLeftIt()
        {
            CreateRuntime();
            var source = CreateGraphicTarget("drag source", 0.25f);
            var destination = CreateGraphicTarget("drop target", 0.75f);
            yield return null;
            IsolateFixtureRaycaster();

            yield return DragBetween(source.gameObject, destination.gameObject, _ => { });

            Assert.That(source.DragPositions, Is.Not.Empty);
            var sourceCentre = source.transform.position.x;
            var destinationCentre = destination.transform.position.x;
            var lastDrag = source.DragPositions.Last().x;

            // 마지막 drag 는 목적지 쪽에서 났는데도 원본이 받았다. 그것이 capture 다.
            Assert.That(
                Mathf.Abs(lastDrag - destinationCentre),
                Is.LessThan(Mathf.Abs(lastDrag - sourceCentre)),
                "the last drag was not reported near the destination");
        }

        /// <summary>
        /// host 는 <c>AdvanceFrame</c> 을 돌리려고만 세운다. 액션은 여기서 만든 executor 가 돈다.
        /// </summary>
        private void CreateRuntime()
        {
            host = new GameObject("Unity Play MCP pointer target test host");
            host.AddComponent<UnityPlayMcpHost>();
            spawned.Add(host);

            var cursorObject = new GameObject("pointer target test cursor");
            cursorController = cursorObject.AddComponent<CursorController>();
            spawned.Add(cursorObject);

            executor = new ActionExecutor(
                new TargetLookup(), cursorController, new PointerEventDispatcher());
        }

        /// <summary>
        /// MCP 의 <c>click</c> 이 보내는 그 세 action 으로 대상의 겨눌 자리를 누른다. 자리는
        /// <see cref="PointerTargeting.TryAim"/> 이 고르고, 좌표는 <c>move_mouse</c> 가 받는 좌상단 기준이다.
        /// </summary>
        /// <summary>
        /// MCP 의 <c>drag</c> 가 보내는 네 action: 원본으로 옮기고, 누르고, 목적지로 활강하고, 놓는다.
        /// </summary>
        private IEnumerator DragBetween(
            GameObject source, GameObject destination, System.Action<ActionResultDto> completed)
        {
            var failure = default(ActionResultDto);
            System.Action<ActionResultDto> note = r => failure = failure ?? (r.IsSuccess ? null : r);

            yield return MoveOnto(source, note);
            yield return Run("mouse_down", Params(0d), note);
            yield return MoveOnto(destination, note);
            yield return Run("mouse_up", Params(0d), note);

            completed(failure ?? ActionResultDto.Success(0));
        }

        private IEnumerator MoveOnto(GameObject target, System.Action<ActionResultDto> completed)
        {
            var aimed = PointerTargeting.TryAim(
                "drag", target.GetInstanceID(), target, new PointerEventDispatcher(), out var aim, out var error);
            Assert.That(aimed, Is.True, error);
            yield return Run(
                "move_mouse",
                Params((double)aim.ScreenPosition.x, (double)(Screen.height - aim.ScreenPosition.y)),
                completed);
        }

        private IEnumerator ClickOn(GameObject target, System.Action<ActionResultDto> completed)
        {
            var aimed = PointerTargeting.TryAim(
                "click", target.GetInstanceID(), target, new PointerEventDispatcher(), out var aim, out var error);
            Assert.That(aimed, Is.True, error);

            var failure = default(ActionResultDto);
            yield return Run("move_mouse", Params((double)aim.ScreenPosition.x, (double)(Screen.height - aim.ScreenPosition.y)),
                r => failure = failure ?? (r.IsSuccess ? null : r));
            yield return Run("mouse_down", Params(0d), r => failure = failure ?? (r.IsSuccess ? null : r));
            yield return Run("mouse_up", Params(0d), r => failure = failure ?? (r.IsSuccess ? null : r));

            completed(failure ?? ActionResultDto.Success(0));
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

        /// <summary>
        /// <c>VirtualMouseMessenger</c> 는 <c>Camera.main</c> 에서 쏜 레이로 대상을 고른다.
        /// <c>MainCamera</c> 태그가 없으면 고를 것이 아예 없다.
        /// </summary>
        private MouseMessageFixtureBehaviour CreateColliderTarget()
        {
            var cameraObject = new GameObject("main camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            spawned.Add(cameraObject);

            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            targetObject.name = "collider target";
            targetObject.transform.position = Vector3.zero;
            targetObject.transform.localScale = new Vector3(4f, 4f, 4f);
            spawned.Add(targetObject);

            return targetObject.AddComponent<MouseMessageFixtureBehaviour>();
        }

        /// <param name="acrossTheScreen">화면 가로에서 차지할 자리, 0 에서 1 사이.</param>
        private PointerFixtureBehaviour CreateGraphicTarget(string name, float acrossTheScreen)
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
            rectTransform.sizeDelta = new Vector2(60f, 60f);
            rectTransform.anchoredPosition =
                new Vector2(Screen.width * acrossTheScreen, Screen.height * 0.5f);

            return targetObject.GetComponent<PointerFixtureBehaviour>();
        }

        /// <summary>
        /// fixture 의 canvas 만 포인터 레이에 답하게 둔다. 게임 자신의 canvas 가 먼저 답하면
        /// 액션이 엉뚱한 오브젝트에 닿는다.
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
    }
}
