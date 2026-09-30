using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 뒤 <see cref="CursorController"/> 가 cursor overlay 를 다시 세우는지 확인한다.
    /// </summary>
    /// <remarks>
    /// <c>OnEnable</c> 과 <c>Update</c> 순서에 의존하므로 play mode 에서만 돈다. reload 뒤 <c>Update</c> 가
    /// null <c>cursorTexture</c> 로 던지면 예외가 log 로 올라와 실패한다 (#65).
    /// </remarks>
    public sealed class CursorReloadRecoveryTests
    {
        private const string CanvasName = "Unity Play MCP Virtual Cursor Canvas";
        private const string CursorPath = CanvasName + "/Unity Play MCP Virtual Cursor";
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
        /// reload 전후로 다른 좌표로 옮긴다. 이전 cursor 가 남아 있으면 좌표가 reload 전 위치에 멈춰 있다.
        /// </remarks>
        [UnityTest]
        public IEnumerator MovesTheCursorAfterAnAssemblyReload()
        {
            var controller = CreateController();
            yield return null;

            yield return controller.MoveTo(new Vector2(40f, 60f), null);

            AssemblyReloadSimulation.Rehearse(controller);
            // 걷어낸 canvas 는 프레임 끝에 Destroy 된다.
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
        /// reload 뒤 <c>cursorTexture</c> 가 null 인 채로 theme 이 바뀌면 <c>PaintCursorTexture</c> 가 던진다 (#65).
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
        /// reload 는 GameObject 를 파괴하지 않는다. 남은 canvas 를 걷어내지 않고 다시 만들면 cursor 가 두 개가 된다.
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
        /// reload 가 아닌 단순 disable/enable 경로는 기존 cursor 를 그대로 둬야 한다. 다시 만들면 cursor 위치를 잃는다.
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
