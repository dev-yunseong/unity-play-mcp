using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 를 건넌 <see cref="KeyboardStatusController"/> 가 status overlay 를 다시
    /// 세우고 눌린 key 를 계속 표시하는지.
    /// </summary>
    /// <remarks>
    /// edit mode 로 내려올 수 없다. 여기서 보는 것이 <c>OnEnable</c> 과 <c>Update</c> 가 서로에게 무엇을
    /// 남기는가이고, 그 둘은 play mode 밖에서 돌지 않는다.
    ///
    /// 실패는 대개 assert 가 아니라 log 로 온다. reload 뒤 <c>RefreshText</c> 가 null 인
    /// <c>keyStatusText</c> 에 쓰면 — issue #65 가 인용한 <c>KeyboardStatusController.cs:253</c> 이다 —
    /// 그 예외만으로 이 fixture 가 붉어진다.
    ///
    /// panel 이 무엇을 보여 주는지는 private field 대신 <c>Text</c> component 를 훑어 읽는다. panel 에
    /// 실제로 그려진 문자열이 이 test 가 확인하려는 것이기 때문이다.
    /// </remarks>
    public sealed class KeyboardStatusReloadRecoveryTests
    {
        private const string CanvasName = "Unity Play MCP Keyboard Status Canvas";
        private const string PanelPath = CanvasName + "/Keyboard Status Panel";
        private const string DarkThemeKey = "UnityPlayMcp.DarkTheme";

        private GameObject controllerObject;

        /// <summary>이 fixture 가 건드리는 유일한 전역. 사람의 값을 그대로 돌려주려고 적어 둔다.</summary>
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
        /// key 를 reload 뒤에 누른다. 진짜 reload 는 <c>VirtualInput</c> 이 쥔 static 도 초기값으로
        /// 되돌리는데 rehearsal 은 거기까지 따라 하지 않으므로, reload 전에 누른 키가 건너편에 남는지를
        /// 여기서 약속하면 실제보다 관대한 자리를 지키게 된다. issue 가 요구하는 것도 reload 뒤에 누른
        /// key 가 표시되는 것이다.
        ///
        /// 이 한 test 가 acceptance criterion 두 개를 함께 덮는다. <c>keyStatusText</c> 가 null 이면 던지고,
        /// <c>keyboardKeys</c> 가 빈 채로 남으면 눌린 key 가 목록에 오르지 못해 표시가 <c>—</c> 에 머문다.
        /// </remarks>
        [UnityTest]
        public IEnumerator ShowsPressedKeysAfterAnAssemblyReload()
        {
            var controller = CreateController();
            yield return null;

            AssemblyReloadSimulation.Rehearse(controller);
            // 걷어낸 canvas 는 Destroy 가 처리되는 프레임 끝에야 사라진다.
            yield return null;

            // 누름은 다음 프레임부터 눌린 것으로 읽힌다. 폴링하는 쪽이 script 실행 순서와 무관하게 그것을
            // 보게 하려고 VirtualKeyboardState 가 그렇게 정해 두었다.
            VirtualInput.PressKey(KeyCode.Space);
            yield return null;
            yield return null;

            Assert.That(PanelText(), Does.Contain("SPACE"),
                "reload 뒤 keyboardKeys 가 비어 있거나 keyStatusText 가 null 이면 눌린 key 가 뜨지 않는다.");
        }

        /// <remarks>
        /// pointer 를 먼저 쥐어야 한다. <c>FormatPointer</c> 는 좌표가 없으면 held button 을 보기도 전에
        /// <c>—</c> 를 돌려준다.
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
        /// reload 는 GameObject 를 파괴하지 않는다. 살아남은 canvas 를 걷어내지 않고 다시 만들면 status
        /// panel 이 두 벌이 되는데, 그것은 이 fix 가 스스로 들여올 수 있는 위험이지 지금 있는 결함이 아니다
        /// — 이 test 는 fix 없이도 통과한다.
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
        /// reload 가 아니라 그냥 껐다 켜는 길. 생성을 <c>Awake</c> 에서 <c>OnEnable</c> 로 옮긴 변경이라
        /// 이쪽이 예전처럼 도는지를 함께 지킨다.
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

        /// <summary>panel 이 지금 그리고 있는 문자열 전부.</summary>
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
