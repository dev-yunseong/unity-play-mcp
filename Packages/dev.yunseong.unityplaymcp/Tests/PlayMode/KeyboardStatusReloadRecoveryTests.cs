using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 뒤 <see cref="KeyboardStatusController"/> 가 status overlay 를 다시
    /// 세우고 눌린 key 를 계속 표시하는지 확인한다.
    /// </summary>
    /// <remarks>
    /// <c>OnEnable</c> 과 <c>Update</c> 순서에 의존하므로 play mode 에서만 돈다. reload 뒤
    /// <c>RefreshText</c> 가 null <c>keyStatusText</c> 에 쓰면 예외가 log 로 올라와 실패한다 (#65).
    /// 표시 내용은 실제로 그려진 <c>Text</c> 에서 읽는다.
    /// </remarks>
    public sealed class KeyboardStatusReloadRecoveryTests
    {
        private const string CanvasName = "Unity Play MCP Keyboard Status Canvas";
        private const string PanelPath = CanvasName + "/Keyboard Status Panel";
        private const string DarkThemeKey = "UnityPlayMcp.DarkTheme";

        private GameObject controllerObject;

        /// <summary>fixture 가 바꾸는 전역 값이다. TearDown 에서 원래 값으로 되돌린다.</summary>
        private bool hadTheme;
        private int previousTheme;

        [SetUp]
        public void SetUp()
        {
            hadTheme = PlayerPrefs.HasKey(DarkThemeKey);
            previousTheme = PlayerPrefs.GetInt(DarkThemeKey, 1);
            PlayerPrefs.SetInt(DarkThemeKey, 1);
        }

        [TearDown]
        public void TearDown()
        {
            // 실패한 test 가 눌린 키를 다음 fixture 로 넘기지 않게 한다.
            VirtualInput.ReleaseAllVirtualInput();

            if (controllerObject != null)
            {
                Object.DestroyImmediate(controllerObject);
            }

            if (hadTheme)
            {
                PlayerPrefs.SetInt(DarkThemeKey, previousTheme);
            }
            else
            {
                PlayerPrefs.DeleteKey(DarkThemeKey);
            }
        }

        /// <remarks>
        /// key 는 reload 뒤에 누른다. rehearsal 은 <c>VirtualInput</c> static 을 초기화하지 않으므로
        /// reload 전에 누르면 실제보다 관대한 조건을 확인하게 된다.
        /// <c>keyStatusText</c> 가 null 이면 던지고, <c>keyboardKeys</c> 가 비어 있으면 표시가 <c>—</c> 에 머문다.
        /// </remarks>
        [UnityTest]
        public IEnumerator ShowsPressedKeysAfterAnAssemblyReload()
        {
            var controller = CreateController();
            yield return null;

            AssemblyReloadSimulation.Rehearse(controller);
            // 걷어낸 canvas 는 프레임 끝에 Destroy 된다.
            yield return null;

            // VirtualKeyboardState 는 누름을 다음 프레임부터 반영한다.
            VirtualInput.PressKey(KeyCode.Space);
            yield return null;
            yield return null;

            Assert.That(PanelText(), Does.Contain("SPACE"),
                "reload 뒤 keyboardKeys 가 비어 있거나 keyStatusText 가 null 이면 눌린 key 가 뜨지 않는다.");
        }

        /// <remarks>
        /// <c>FormatPointer</c> 는 좌표가 없으면 held button 을 보기 전에 <c>—</c> 를 반환하므로 pointer 를 먼저 옮긴다.
        /// </remarks>
        [UnityTest]
        public IEnumerator ShowsHeldMouseButtonsAfterAnAssemblyReload()
        {
            var controller = CreateController();
            yield return null;

            AssemblyReloadSimulation.Rehearse(controller);
            yield return null;

            VirtualInput.MoveMouse(new Vector2(100f, 200f));
            VirtualInput.PressMouseButton(0);
            yield return null;
            yield return null;

            Assert.That(PanelText(), Does.Contain("HOLD"));
            Assert.That(PanelText(), Does.Contain("LEFT"));
        }

        /// <remarks>
        /// reload 는 GameObject 를 파괴하지 않는다. 남은 canvas 를 걷어내지 않고 다시 만들면 panel 이 두 개가 된다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsOneStatusPanelAfterAReload()
        {
            var controller = CreateController();
            yield return null;

            AssemblyReloadSimulation.Rehearse(controller);
            yield return null;

            Assert.That(CanvasCount(), Is.EqualTo(1),
                "살아남은 canvas 를 걷어내지 않으면 status panel 이 두 벌이 된다.");
        }

        /// <remarks>
        /// reload 가 아닌 단순 disable/enable 경로는 기존 overlay 를 그대로 둬야 한다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsTheStatusPanelOnAPlainReEnable()
        {
            var controller = CreateController();
            yield return null;

            var canvas = controllerObject.transform.Find(CanvasName).gameObject;

            controller.enabled = false;
            controller.enabled = true;
            yield return null;

            Assert.That(controllerObject.transform.Find(CanvasName).gameObject, Is.SameAs(canvas),
                "재활성화는 서 있는 overlay 를 그대로 둬야 한다.");
            Assert.That(CanvasCount(), Is.EqualTo(1));
        }

        private KeyboardStatusController CreateController()
        {
            controllerObject = new GameObject("keyboard status reload test");
            return controllerObject.AddComponent<KeyboardStatusController>();
        }

        /// <summary>panel 이 그리고 있는 문자열을 모두 합친다.</summary>
        private string PanelText()
        {
            var panel = controllerObject.transform.Find(PanelPath);
            Assert.That(panel, Is.Not.Null, "reload 뒤 panel 을 다시 세우지 않으면 여기 아무것도 없다.");

            var joined = new StringBuilder();
            foreach (var text in panel.GetComponentsInChildren<Text>(true))
            {
                joined.Append(text.text).Append('\n');
            }

            return joined.ToString();
        }

        private int CanvasCount()
        {
            var found = 0;
            foreach (Transform child in controllerObject.transform)
            {
                if (child.name == CanvasName)
                {
                    found++;
                }
            }

            return found;
        }
    }
}
