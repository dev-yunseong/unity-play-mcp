using System.Collections.Generic;
using System.Reflection;
using Artel.Protocol.Dto;
using NUnit.Framework;
using TMPro;
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
            var controller = CreateController();
            targetObject = new GameObject("target", typeof(RectTransform));
            var target = targetObject.GetComponent<RectTransform>();
            target.position = new Vector3(120f, 240f, 0f);

            // RectTransform을 받는 MoveTo는 좌표를 계산해 다른 MoveTo에 넘긴다. Drain 없이
            // 손으로 돌리면 그 중첩 코루틴이 실행되지 않아 커서가 제자리에 숨어 있는다.
            Drain(controller.MoveTo(target, null));

            var cursor = controllerObject.transform
                .Find("Artel Virtual Cursor Canvas/Artel Virtual Cursor");
            Assert.That(cursor.gameObject.activeSelf, Is.True);
            Assert.That(cursor.position.x, Is.EqualTo(120f).Within(0.01f));
            Assert.That(cursor.position.y, Is.EqualTo(240f).Within(0.01f));
        }

        [Test]
        public void Cursor_UsesArtelCoralWithHighContrastBorder()
        {
            var hadTheme = PlayerPrefs.HasKey("Artel.DarkTheme");
            var previousTheme = PlayerPrefs.GetInt("Artel.DarkTheme");
            try
            {
                PlayerPrefs.SetInt("Artel.DarkTheme", 1);
                var controller = CreateController();

                var texture = controllerObject.transform
                    .Find("Artel Virtual Cursor Canvas/Artel Virtual Cursor")
                    .GetComponent<Image>().sprite.texture;
                var pixels = texture.GetPixels32();

                Assert.That(pixels, Has.Some.EqualTo(ArtelLogoGraphic.CoralDark));
                Assert.That(pixels, Has.Some.EqualTo(ArtelLogoGraphic.Ink));

                PlayerPrefs.SetInt("Artel.DarkTheme", 0);
                typeof(CursorController)
                    .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);

                var lightPixels = texture.GetPixels32();
                Assert.That(lightPixels, Has.Some.EqualTo(ArtelLogoGraphic.Coral));
                Assert.That(lightPixels, Has.Some.EqualTo(ArtelLogoGraphic.Charcoal));
            }
            finally
            {
                if (hadTheme)
                {
                    PlayerPrefs.SetInt("Artel.DarkTheme", previousTheme);
                }
                else
                {
                    PlayerPrefs.DeleteKey("Artel.DarkTheme");
                }
            }
        }

        [Test]
        public void ExecuteButtonClick_MovesCursorBeforeInvokingButton()
        {
            var controller = CreateController();
            targetObject = NewButtonObject("button target");
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
            var executor = new ActionExecutor(scanner, controller, new PointerEventDispatcher());

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

        [Test]
        public void ExecuteButtonClick_RefusesALockedButton()
        {
            var controller = CreateController();
            targetObject = NewButtonObject("locked button");
            var button = targetObject.GetComponent<Button>();
            button.interactable = false;
            var clicked = false;
            button.onClick.AddListener(() => clicked = true);
            var scanner = new SceneScanner();
            scanner.Scan();
            var executor = new ActionExecutor(scanner, controller, new PointerEventDispatcher());

            ActionResultDto result = null;
            Drain(executor.Execute(
                7,
                "button_click",
                new List<object> { targetObject.GetInstanceID() },
                value => result = value));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("not interactable"));
            Assert.That(clicked, Is.False);
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
            var scanner = new SceneScanner();
            scanner.Scan();
            var executor = new ActionExecutor(scanner, controller, new PointerEventDispatcher());

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
            // TMP_InputField is the field type most games actually ship, and it takes the other
            // branch of the interactability check than the legacy InputField does.
            var controller = CreateController();
            targetObject = new GameObject(
                "locked tmp field",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(TMP_InputField));
            var field = targetObject.GetComponent<TMP_InputField>();
            field.interactable = false;
            var scanner = new SceneScanner();
            scanner.Scan();
            var executor = new ActionExecutor(scanner, controller, new PointerEventDispatcher());

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
        /// EditMode에서는 AddComponent가 Awake를 부르지 않는다. 커서 오브젝트는 Awake에서
        /// 만들어지므로 직접 부르지 않으면 MoveTo가 아무것도 하지 않고 빠져나가고, 찾으려는
        /// 커서는 끝까지 존재하지 않는다.
        /// </summary>
        private CursorController CreateController()
        {
            controllerObject = new GameObject("cursor controller");
            var controller = controllerObject.AddComponent<CursorController>();
            typeof(CursorController)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            return controller;
        }

        private static GameObject NewButtonObject(string name)
        {
            return new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
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
