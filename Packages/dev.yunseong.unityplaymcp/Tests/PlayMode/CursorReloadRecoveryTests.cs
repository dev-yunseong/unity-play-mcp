using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 를 건넌 <see cref="CursorController"/> 가 cursor overlay 를 다시 세우는지.
    /// </summary>
    /// <remarks>
    /// edit mode 로 내려올 수 없다. 여기서 보는 것이 <c>OnEnable</c> 과 <c>Update</c> 가 서로에게 무엇을
    /// 남기는가이고, 그 둘은 play mode 밖에서 돌지 않는다.
    ///
    /// 실패는 assert 로도 log 로도 온다. Unity Test Framework 는 test 가 도는 동안 올라온 예외를 그대로
    /// 실패로 치므로, reload 뒤 <c>Update</c> 가 null 인 <c>cursorTexture</c> 로 던지면 — issue #65 가
    /// 그것이다 — 프레임을 넘기는 것만으로 이 fixture 가 붉어진다.
    /// </remarks>
    public sealed class CursorReloadRecoveryTests
    {
        private const string CanvasName = "Unity Play MCP Virtual Cursor Canvas";
        private const string CursorPath = CanvasName + "/Unity Play MCP Virtual Cursor";
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
        /// reload 전에 한 번 옮겨 두고 뒤에 다른 자리로 옮긴다. 살아남은 cursor 가 그대로 남아 있으면
        /// 좌표가 reload 전 자리에 멈춰 있으므로, 다시 세웠는지를 좌표 하나로 가른다.
        /// </remarks>
        [UnityTest]
        public IEnumerator MovesTheCursorAfterAnAssemblyReload()
        {
            var controller = CreateController();
            yield return null;

            yield return controller.MoveTo(new Vector2(40f, 60f), null);

            AssemblyReloadSimulation.Rehearse(controller);
            // 걷어낸 canvas 는 Destroy 가 처리되는 프레임 끝에야 사라진다.
            yield return null;

            yield return controller.MoveTo(new Vector2(120f, 240f), null);

            var cursor = controllerObject.transform.Find(CursorPath);
            Assert.That(cursor, Is.Not.Null, "reload 뒤 cursor 를 다시 세우지 않으면 여기 아무것도 없다.");
            Assert.That(cursor.gameObject.activeSelf, Is.True,
                "cursorTransform 이 null 이면 MoveTo 가 조용히 빠져나가 cursor 가 꺼진 채로 남는다.");
            Assert.That(cursor.position.x, Is.EqualTo(120f).Within(0.01f));
            Assert.That(cursor.position.y, Is.EqualTo(240f).Within(0.01f));
        }

        /// <remarks>
        /// issue #65 가 인용한 <c>CursorController.cs:44</c> 다. reload 뒤 <c>cursorTexture</c> 가 null 인
        /// 채로 theme 이 바뀌면 <c>PaintCursorTexture</c> 가 그 자리에서 던진다.
        /// </remarks>
        [UnityTest]
        public IEnumerator RepaintsTheCursorWhenTheThemeFlipsAfterAReload()
        {
            var controller = CreateController();
            yield return null;

            AssemblyReloadSimulation.Rehearse(controller);
            yield return null;

            PlayerPrefs.SetInt(DarkThemeKey, 0);
            yield return null;
            yield return null;

            var texture = controllerObject.transform.Find(CursorPath).GetComponent<Image>().sprite.texture;
            Assert.That(texture.GetPixels32(), Has.Some.EqualTo(KeyboardStatusController.LightAccentColor),
                "theme 이 바뀌면 다시 세운 texture 를 라이트 색으로 칠해야 한다.");
        }

        /// <remarks>
        /// reload 는 GameObject 를 파괴하지 않는다. 살아남은 canvas 를 걷어내지 않고 다시 만들면 cursor 가
        /// 두 벌이 되는데, 그것은 이 fix 가 스스로 들여올 수 있는 위험이지 지금 있는 결함이 아니다 — 이
        /// test 는 fix 없이도 통과한다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsOneCursorCanvasAfterAReload()
        {
            var controller = CreateController();
            yield return null;

            AssemblyReloadSimulation.Rehearse(controller);
            yield return null;

            Assert.That(CanvasCount(), Is.EqualTo(1),
                "살아남은 canvas 를 걷어내지 않으면 cursor 가 두 벌이 된다.");
        }

        /// <remarks>
        /// reload 가 아니라 그냥 껐다 켜는 길. 생성을 <c>Awake</c> 에서 <c>OnEnable</c> 로 옮긴 변경이라
        /// 이쪽이 예전처럼 도는지를 함께 지킨다 — 다시 만들면 cursor 가 깜빡이고 있던 자리를 잃는다.
        /// </remarks>
        [UnityTest]
        public IEnumerator KeepsTheCursorOnAPlainReEnable()
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

        private CursorController CreateController()
        {
            controllerObject = new GameObject("cursor reload test");
            return controllerObject.AddComponent<CursorController>();
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
