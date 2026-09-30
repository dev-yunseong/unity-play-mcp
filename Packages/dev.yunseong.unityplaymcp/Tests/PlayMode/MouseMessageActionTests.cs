using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityPlayMcp.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// <c>OnMouse</c> 메시지 전달을 확인한다. EventSystem 이 아닌 collider 와 camera 경로라,
    /// uGUI 를 쓰지 않는 2D 게임은 대부분 여기로 클릭을 받는다.
    /// </summary>
    /// <remarks>
    /// <c>VirtualMouseMessenger</c> 는 host 의 <c>Update</c> 가 돌리고 host 는 <c>Awake</c> 에서
    /// <c>DontDestroyOnLoad</c> 를 부르므로 play mode 에서만 돈다.
    /// </remarks>
    public sealed class MouseMessageActionTests
    {
        /// <summary>화면 중앙이라 어느 해상도에서도 collider 위다.</summary>
        private static Vector2 CenterOfScreen
        {
            get { return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f); }
        }

        private GameObject host;
        private GameObject cameraObject;
        private GameObject targetObject;

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

            foreach (var alive in new[] { targetObject, cameraObject, host })
            {
                if (alive != null)
                {
                    Object.DestroyImmediate(alive);
                }
            }
        }

        [UnityTest]
        public IEnumerator KeyDownMouse0_ReachesTheOnMouseHandlersUnderTheCursor()
        {
            var manager = CreateManager();
            var target = CreateColliderTarget();
            yield return null;

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", ScreenTopLeft(CenterOfScreen)),
                NewAction(2, "key_down", Params("Mouse0")));
            // messenger 는 host 의 Update 에서 도므로 한 프레임을 기다린다.
            yield return null;

            Assert.That(target.Messages, Does.Contain("enter"));
            Assert.That(target.Messages, Does.Contain("down"), "key_down did not reach OnMouseDown");
            Assert.That(target.OverCount, Is.GreaterThan(0));

            yield return RunBatch(manager, NewAction(3, "key_up", Params("Mouse0")));
            yield return null;

            Assert.That(target.Messages, Does.Contain("up"));
            // 누른 오브젝트 위에서 놓았으므로 upAsButton 도 와야 한다.
            Assert.That(target.Messages, Does.Contain("upAsButton"));
        }

        [UnityTest]
        public IEnumerator MouseDown_ReachesTheSameHandlersAsTheKeyCode()
        {
            var manager = CreateManager();
            var target = CreateColliderTarget();
            yield return null;

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", ScreenTopLeft(CenterOfScreen)),
                NewAction(2, "mouse_down", Params(0d)));
            yield return null;

            // 두 방식이 모두 같은 대상에 닿아야 한다.
            Assert.That(target.Messages, Does.Contain("down"));
        }

        /// <summary>
        /// 포인터 아래 대상이 없으면 엔진처럼 아무 메시지도 보내지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyDownMouse0_WithoutAMoveSaysNothingToAnyone()
        {
            var manager = CreateManager();
            var target = CreateColliderTarget();
            yield return null;

            yield return RunBatch(manager, NewAction(1, "key_down", Params("Mouse0")));
            yield return null;

            Assert.That(target.Messages, Is.Empty);
            // 폴링하는 쪽에는 눌림이 보인다.
            Assert.That(VirtualInput.GetMouseButton(0), Is.True);
        }

        private UnityPlayMcpHost CreateManager()
        {
            host = new GameObject("Unity Play MCP mouse message test");
            var manager = host.AddComponent<UnityPlayMcpHost>();
            return manager;
        }

        /// <summary>
        /// <c>VirtualMouseMessenger</c> 는 <c>Camera.main</c> 에서 ray 를 쏘므로 <c>MainCamera</c> tag 가 필요하다.
        /// </summary>
        private MouseMessageFixtureBehaviour CreateColliderTarget()
        {
            cameraObject = new GameObject("main camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;

            targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            targetObject.name = "mouse message target";
            targetObject.transform.position = Vector3.zero;
            targetObject.transform.localScale = new Vector3(4f, 4f, 4f);

            return targetObject.AddComponent<MouseMessageFixtureBehaviour>();
        }

        private static List<object> ScreenTopLeft(Vector2 unityPoint)
        {
            // move_mouse 는 좌상단 기준 픽셀 좌표를 받는다.
            return Params((double)unityPoint.x, (double)(Screen.height - unityPoint.y));
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

    }
}
