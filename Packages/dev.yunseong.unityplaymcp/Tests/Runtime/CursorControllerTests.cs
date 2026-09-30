using System.Collections.Generic;
using System.Reflection;
using UnityPlayMcp.Protocol.Dto;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
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
            var controller = CreateController();
            targetObject = new GameObject("target", typeof(RectTransform));
            var target = targetObject.GetComponent<RectTransform>();
            target.position = new Vector3(120f, 240f, 0f);

            // RectTransform 을 받는 MoveTo 는 중첩 coroutine 을 넘기므로 Drain 없이는 커서가 움직이지 않는다.
            Drain(controller.MoveTo(target, null));

            var cursor = controllerObject.transform
                .Find("Unity Play MCP Virtual Cursor Canvas/Unity Play MCP Virtual Cursor");
            Assert.That(cursor.gameObject.activeSelf, Is.True);
            Assert.That(cursor.position.x, Is.EqualTo(120f).Within(0.01f));
            Assert.That(cursor.position.y, Is.EqualTo(240f).Within(0.01f));
        }

        [Test]
        public void Cursor_UsesCoralWithHighContrastBorder()
        {
            var hadTheme = PlayerPrefs.HasKey("UnityPlayMcp.DarkTheme");
            var previousTheme = PlayerPrefs.GetInt("UnityPlayMcp.DarkTheme");
            try
            {
                PlayerPrefs.SetInt("UnityPlayMcp.DarkTheme", 1);
                var controller = CreateController();

                var texture = controllerObject.transform
                    .Find("Unity Play MCP Virtual Cursor Canvas/Unity Play MCP Virtual Cursor")
                    .GetComponent<Image>().sprite.texture;
                var pixels = texture.GetPixels32();

                Assert.That(pixels, Has.Some.EqualTo(KeyboardStatusController.DarkAccentColor));
                Assert.That(pixels, Has.Some.EqualTo(KeyboardStatusController.DarkForegroundColor));

                PlayerPrefs.SetInt("UnityPlayMcp.DarkTheme", 0);
                typeof(CursorController)
                    .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);

                var lightPixels = texture.GetPixels32();
                Assert.That(lightPixels, Has.Some.EqualTo(KeyboardStatusController.LightAccentColor));
                Assert.That(lightPixels, Has.Some.EqualTo(KeyboardStatusController.LightForegroundColor));
            }
            finally
            {
                if (hadTheme)
                {
                    PlayerPrefs.SetInt("UnityPlayMcp.DarkTheme", previousTheme);
                }
                else
                {
                    PlayerPrefs.DeleteKey("UnityPlayMcp.DarkTheme");
                }
            }
        }

        [Test]
        public void ExecuteEnterText_RefusesALockedField()
        {
            var controller = CreateController();
            targetObject = new GameObject(
                "locked field",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(InputField));
            var field = targetObject.GetComponent<InputField>();
            field.text = "before";
            field.interactable = false;
            var targetLookup = new TargetLookup();
            var executor = new ActionExecutor(targetLookup, controller, new PointerEventDispatcher());

            ActionResultDto result = null;
            Drain(executor.Execute(
                8,
                "enter_text",
                new List<object> { targetObject.GetInstanceID(), "after" },
                value => result = value));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("not interactable"));
            Assert.That(field.text, Is.EqualTo("before"));
        }

        [Test]
        public void ExecuteEnterText_RefusesALockedTmpField()
        {
            // TMP_InputField is what most games ship, and it takes a different interactability branch
            // than the legacy InputField.
            var controller = CreateController();
            targetObject = new GameObject(
                "locked tmp field",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(TMP_InputField));
            var field = targetObject.GetComponent<TMP_InputField>();
            field.interactable = false;
            var targetLookup = new TargetLookup();
            var executor = new ActionExecutor(targetLookup, controller, new PointerEventDispatcher());

            ActionResultDto result = null;
            Drain(executor.Execute(
                9,
                "enter_text",
                new List<object> { targetObject.GetInstanceID(), "after" },
                value => result = value));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("not interactable"));
            Assert.That(field.text, Is.Empty);
        }

        /// <summary>
        /// edit mode 에서는 AddComponent 가 OnEnable 을 부르지 않는다. 커서 오브젝트는 OnEnable 에서 만들어지므로
        /// 직접 불러야 MoveTo 가 동작한다.
        /// </summary>
        private CursorController CreateController()
        {
            controllerObject = new GameObject("cursor controller");
            var controller = controllerObject.AddComponent<CursorController>();
            typeof(CursorController)
                .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            return controller;
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
