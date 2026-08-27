using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// <c>OnMouse</c> 계열까지 닿는지를 본다. EventSystem 이 아니라 콜라이더와 카메라를 통하는
    /// 경로라서, uGUI 를 쓰지 않는 2D 게임 대부분이 클릭을 받는 유일한 자리다.
    /// </summary>
    /// <remarks>
    /// 에디트 모드로 내려올 수 없다. <c>VirtualMouseMessenger</c> 를 미는 것은 매니저의
    /// <c>Update</c> 이고, 매니저는 <c>Awake</c> 에서 <c>DontDestroyOnLoad</c> 를 부른다.
    /// </remarks>
    public sealed class MouseMessageActionTests
    {
        /// <summary>커서를 둘 자리. 화면 한가운데라 어떤 해상도에서도 콜라이더 위다.</summary>
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
            foreach (var stale in Object.FindObjectsOfType<ArtelManager>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        [TearDown]
        public void TearDown()
        {
            ArtelInput.ReleaseAllVirtualInput();

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
            // 메신저는 매니저의 Update 에서 돈다. 한 프레임을 줘야 그 자리가 온다.
            yield return null;

            Assert.That(target.Messages, Does.Contain("enter"));
            Assert.That(target.Messages, Does.Contain("down"), "key_down did not reach OnMouseDown");
            Assert.That(target.OverCount, Is.GreaterThan(0));

            yield return RunBatch(manager, NewAction(3, "key_up", Params("Mouse0")));
            yield return null;

            Assert.That(target.Messages, Does.Contain("up"));
            // 커서가 누른 그 오브젝트 위에서 놓였으므로, 엔진이라면 여기서 버튼 클릭을 알린다.
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

            // 두 어휘가 같은 곳에 닿는다는 것이 이 변경의 전부다. 한쪽만 닿으면 여기서 갈린다.
            Assert.That(target.Messages, Does.Contain("down"));
        }

        /// <summary>
        /// 포인터를 잡지 않은 클릭은 조용하다. 고를 자리가 없으니 엔진도 아무에게도 보내지 않는다.
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
            // 폴링하는 쪽에는 그래도 닿는다. 눌린 것 자체는 사실이다.
            Assert.That(ArtelInput.GetMouseButton(0), Is.True);
        }

        private ArtelManager CreateManager()
        {
            host = new GameObject("Artel mouse message test");
            var manager = host.AddComponent<ArtelManager>();
            manager.SetWebSocketTransport(new SilentTransport(), false);
            return manager;
        }

        /// <summary>
        /// <c>VirtualMouseMessenger</c> 는 <c>Camera.main</c> 에서 쏜 레이로 대상을 고른다. 카메라가
        /// <c>MainCamera</c> 태그를 달고 있지 않으면 고를 것이 아예 없다.
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
            // move_mouse 는 스캔이 보고하는 좌표계, 즉 위에서 아래로 세는 픽셀을 받는다.
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

        private sealed class SilentTransport : IArtelWebSocketTransport
        {
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
            }

            public void Dispose()
            {
            }
        }
    }
}
