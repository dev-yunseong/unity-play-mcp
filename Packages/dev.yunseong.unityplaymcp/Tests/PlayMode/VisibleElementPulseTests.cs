using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Affordances.Live;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 점수를 표시하는 <c>Text</c> 가 실제 pulse 문서에 실리는지와 그 내용을 확인한다.
    /// </summary>
    /// <remarks>
    /// <c>Graphic</c> 은 <c>OnEnable</c> 에서 canvas 에 등록되고 그 뒤에 화면 좌표를 얻으므로 play mode 에서만 돈다.
    /// 이 객체는 감시 멤버, evidence, inspector 연결 호출이 모두 없어도 pulse 에 실려야 한다.
    /// </remarks>
    public sealed class VisibleElementPulseTests
    {
        private GameObject canvas;

        [TearDown]
        public void TearDown()
        {
            if (canvas != null)
            {
                Object.DestroyImmediate(canvas);
            }

            Worth.Forget();
        }

        private static string Compose()
        {
            // persistent scene 은 넘기지 않는다. 활성 scene 을 함께 넘기면 같은 객체를 두 번 walk 한다.
            return LiveState.Compose(
                1L,
                default(Scene),
                new Restless(),
                new Restless(1f),
                new Dictionary<string, string>(),
                false,
                out _);
        }

        [UnityTest]
        public IEnumerator 라벨이_실리고_그_글자가_함께_온다()
        {
            canvas = new GameObject("Canvas", typeof(Canvas));

            var label = new GameObject("Score", typeof(Text));
            label.transform.SetParent(canvas.transform, false);
            label.GetComponent<Text>().text = "Score: 12";

            // OnEnable 과 layout 이 한 번 돌 때까지 기다린다.
            yield return null;

            var document = Compose();

            Assert.That(document, Does.Contain("\"path\":\"Canvas/Score\""));
            Assert.That(document, Does.Contain("\"on\":\"UnityEngine.UI.Text\""));
            Assert.That(document, Does.Contain("\"member\":\"text\""));
            Assert.That(document, Does.Contain("\"value\":\"Score: 12\""));

            // 위치와 현재 보이는지.
            Assert.That(document, Does.Contain("\"rect\":"));
            Assert.That(document, Does.Contain("\"onScreen\":"));
            Assert.That(document, Does.Contain("\"covered\":"));
        }

        [UnityTest]
        public IEnumerator 채움_비율과_값도_같은_길로_온다()
        {
            canvas = new GameObject("Canvas", typeof(Canvas));

            var bar = new GameObject("Health", typeof(Image));
            bar.transform.SetParent(canvas.transform, false);
            bar.GetComponent<Image>().type = Image.Type.Filled;
            bar.GetComponent<Image>().fillAmount = 0.25f;

            var slider = new GameObject("Volume", typeof(Slider));
            slider.transform.SetParent(canvas.transform, false);
            slider.GetComponent<Slider>().value = 0.75f;

            yield return null;

            var document = Compose();

            Assert.That(document, Does.Contain("\"on\":\"UnityEngine.UI.Image\""));
            Assert.That(document, Does.Contain("\"member\":\"fillAmount\""));
            Assert.That(document, Does.Contain("\"value\":0.25"));

            Assert.That(document, Does.Contain("\"on\":\"UnityEngine.UI.Slider\""));
            Assert.That(document, Does.Contain("\"member\":\"value\""));
            Assert.That(document, Does.Contain("\"value\":0.75"));
        }

        [UnityTest]
        public IEnumerator 꺼진_라벨은_deactive_로_간다()
        {
            canvas = new GameObject("Canvas", typeof(Canvas));

            var label = new GameObject("Paused", typeof(Text));
            label.transform.SetParent(canvas.transform, false);
            label.GetComponent<Text>().text = "Paused";
            label.SetActive(false);

            yield return null;

            var document = Compose();

            var split = document.IndexOf("\"deactive\":", System.StringComparison.Ordinal);
            Assert.That(split, Is.GreaterThan(0), "pulse 가 deactive 목록을 쓰지 않았다.");

            // 꺼진 객체는 값 없이 자리만 싣는다. deactive 목록에 있다는 것이 꺼져 있다는 뜻이다.
            Assert.That(
                document.IndexOf("\"path\":\"Canvas/Paused\"", System.StringComparison.Ordinal),
                Is.GreaterThan(split));
        }
    }
}
