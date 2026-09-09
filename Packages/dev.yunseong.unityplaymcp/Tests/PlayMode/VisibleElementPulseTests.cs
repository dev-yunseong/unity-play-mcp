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
    /// 점수를 띄우는 <c>Text</c> 하나가 실제 pulse 문서에 실리는지, 그리고 그 문서가 그것에 대해 무엇을 말하는지.
    /// </summary>
    /// <remarks>
    /// edit mode 에서는 못 한다. <c>Canvas</c> 아래 <c>Graphic</c> 은 <c>OnEnable</c> 에서 제 canvas 에 등록되고,
    /// <c>RectTransform</c> 이 화면 좌표를 얻는 것도 그 뒤다.
    ///
    /// 이것이 이 변경의 요점이다: 이 객체는 감시 대상 멤버를 하나도 소유하지 않고, 구운 근거도 없으며, 인스펙터로 연결된
    /// 호출도 없다. 예전에는 그래서 모든 pulse 에서 빠졌다.
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
            // persistent 씬은 넘기지 않는다. 로드된 씬을 한 번 걷는 것으로 충분하고, 활성 씬을 그 자리에 넘기면 같은 객체를
            // 두 번 걷는다.
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

            // OnEnable 이 돌고 레이아웃이 한 번 지나갈 짬.
            yield return null;

            var document = Compose();

            Assert.That(document, Does.Contain("\"path\":\"Canvas/Score\""));
            Assert.That(document, Does.Contain("\"on\":\"UnityEngine.UI.Text\""));
            Assert.That(document, Does.Contain("\"member\":\"text\""));
            Assert.That(document, Does.Contain("\"value\":\"Score: 12\""));

            // 어디인지와, 지금 눈에 닿는지.
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

            // 꺼진 객체는 값을 안 싣고 자리만 잡는다. 그것이 어느 통에 도착했는가가 곧 꺼져 있다는 진술이다.
            Assert.That(
                document.IndexOf("\"path\":\"Canvas/Paused\"", System.StringComparison.Ordinal),
                Is.GreaterThan(split));
        }
    }
}
