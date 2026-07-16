using System.Collections.Generic;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Artel.Tests
{
    public sealed class CursorControllerTests
    {
        private GameObject controllerObject;
        private GameObject targetObject;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(controllerObject);
        }

        [Test]
        public void MoveTo_ShowsCursorAtTargetCenter()
        {
            controllerObject = new GameObject("cursor controller");
            var controller = controllerObject.AddComponent<CursorController>();
            targetObject = new GameObject("target", typeof(RectTransform));
            var target = targetObject.GetComponent<RectTransform>();
            target.position = new Vector3(120f, 240f, 0f);

            var movement = controller.MoveTo(target);
            while (movement.MoveNext())
            {
            }

            var cursor = controllerObject.transform
                .Find("Artel Virtual Cursor Canvas/Artel Virtual Cursor");
            Assert.That(cursor.gameObject.activeSelf, Is.True);
            Assert.That(cursor.position.x, Is.EqualTo(120f).Within(0.01f));
            Assert.That(cursor.position.y, Is.EqualTo(240f).Within(0.01f));
        }

        [Test]
        public void ExecuteButtonClick_MovesCursorBeforeInvokingButton()
        {
            controllerObject = new GameObject("cursor controller");
            var controller = controllerObject.AddComponent<CursorController>();
            targetObject = new GameObject(
                "button target",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var button = targetObject.GetComponent<Button>();
            var cursorWasVisibleDuringClick = false;
            button.onClick.AddListener(() =>
            {
                cursorWasVisibleDuringClick = controllerObject.transform
                    .Find("Artel Virtual Cursor Canvas/Artel Virtual Cursor")
                    .gameObject.activeSelf;
            });
            var scanner = new SceneScanner();
            scanner.Scan();
            var executor = new ActionExecutor(scanner, controller);

            ActionResultDto result = null;
            var execution = executor.Execute(
                7,
                "button_click",
                new List<object> { targetObject.GetInstanceID() },
                value => result = value);
            Drain(execution);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(cursorWasVisibleDuringClick, Is.True);
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
    }
}
