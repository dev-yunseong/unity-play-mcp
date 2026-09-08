using System;
using System.Collections.Generic;
using UnityPlayMcp.Affordances.Live;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 화면에 무언가를 그리는 컴포넌트를 pulse 가 들이는 규칙 — 무엇을 들이는지, 그것에서 무엇을 읽는지, 그리고 그것이 지금
    /// 눈에 닿는지.
    /// </summary>
    /// <remarks>
    /// 걷기 자체는 여기서 돌릴 수 없다. watch list 가 어셈블리에 구워진 근거에서 오고 테스트 어셈블리에는 그것이 없다
    /// (<see cref="PulseGoneTests"/> 가 같은 제약을 적어 두었다). 그래서 걷기가 읽는 값들 — 분류, 멤버 목록, 사각형 규칙 —
    /// 을 그대로 놓고 본다.
    ///
    /// <c>Sight.OnScreen</c> 과 <c>Sight.Covers</c> 는 <c>Screen</c> 을 읽지 않는 순수 함수라 batch mode 에서도
    /// 결정적이다. 화면 크기를 인자로 받게 되어 있는 이유가 그것이다.
    /// </remarks>
    public sealed class VisibleElementTests
    {
        private readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var subject in made)
            {
                if (subject != null)
                {
                    UnityEngine.Object.DestroyImmediate(subject);
                }
            }

            made.Clear();
            Worth.Forget();
        }

        private GameObject Made(string name, params Type[] carries)
        {
            var subject = new GameObject(name, carries);
            made.Add(subject);
            return subject;
        }

        [Test]
        public void 화면에_그리는_타입을_통과시킨다()
        {
            Assert.That(Drawn.Is(typeof(Text)), Is.True);
            Assert.That(Drawn.Is(typeof(Image)), Is.True);
            Assert.That(Drawn.Is(typeof(RawImage)), Is.True);
            Assert.That(Drawn.Is(typeof(Slider)), Is.True);
            Assert.That(Drawn.Is(typeof(Toggle)), Is.True);
            Assert.That(Drawn.Is(typeof(Scrollbar)), Is.True);

            // Button 은 Graphic 이 아니라 Selectable 이다. 뿌리 하나만 보면 이것이 빠진다.
            Assert.That(Drawn.Is(typeof(Button)), Is.True);
        }

        [Test]
        public void 화면에_그리지_않는_타입은_거절한다()
        {
            Assert.That(Drawn.Is(typeof(Transform)), Is.False);
            Assert.That(Drawn.Is(typeof(Rigidbody)), Is.False);
            Assert.That(Drawn.Is(typeof(Canvas)), Is.False);
        }

        private static List<Watched> Read(Type type, params string[] taken)
        {
            var found = new List<Watched>();
            Drawn.Add(found, new HashSet<string>(taken, StringComparer.Ordinal), type);
            return found;
        }

        private static List<string> Named(List<Watched> members)
        {
            var names = new List<string>();

            foreach (var member in members)
            {
                names.Add(member.Member);
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        [Test]
        public void 그_컴포넌트가_보여_주는_것을_낸다()
        {
            Assert.That(Named(Read(typeof(Text))), Is.EqualTo(new[] { "text" }));
            Assert.That(Named(Read(typeof(Image))), Is.EqualTo(new[] { "fillAmount" }));
            Assert.That(Named(Read(typeof(Toggle))), Is.EqualTo(new[] { "interactable", "isOn" }));
            Assert.That(
                Named(Read(typeof(Slider))),
                Is.EqualTo(new[] { "interactable", "normalizedValue", "value" }));

            // 보여 줄 값이 하나도 없는 요소도 멤버를 하나는 들어야 한다. 멤버가 없는 컴포넌트는 `by` 에 안 써지고, 읽는 쪽이
            // 요소를 고르는 근거가 바로 그 `by[].on` 이다.
            Assert.That(Named(Read(typeof(Button))), Is.EqualTo(new[] { "interactable" }));
            Assert.That(Named(Read(typeof(RawImage))), Is.EqualTo(new[] { "texture" }));
        }

        [Test]
        public void 화면에_그리지_않는_타입에서는_아무것도_안_낸다()
        {
            Assert.That(Read(typeof(Rigidbody)), Is.Empty);
        }

        [Test]
        public void 이미_실린_이름은_두_번_싣지_않는다()
        {
            // 게임의 필드가 같은 이름을 쥐고 있으면 그쪽이 이긴다. 같은 이름으로 두 항목이 오면 읽는 쪽은 둘 중 어느 것이
            // 근거가 이름 댄 멤버인지 가릴 수 없다.
            Assert.That(Read(typeof(Text), "text"), Is.Empty);
        }

        [Test]
        public void 프로퍼티로_읽는_멤버로_만든다()
        {
            var found = Read(typeof(Text));

            // `Field` 가 null 이라는 것이 곧 "컴포넌트 자신에서 `Member` 를 걸어라" 는 뜻이다. `Text.text` 뒤의 필드는
            // `m_Text` 이고 그것은 Unity 버전마다 달라질 수 있는 이름이다.
            Assert.That(found[0].Field, Is.Null);
            Assert.That(found[0].Member, Is.EqualTo("text"));
            Assert.That(found[0].Owner, Is.EqualTo(typeof(Text)));

            // 어떤 근거도 이 값을 청한 적이 없다. 읽을 수 있어서 싣는 것이고, 그 차이는 읽는 쪽의 물음이다.
            Assert.That(found[0].Asked, Is.False);
            Assert.That(found[0].Static, Is.False);
        }

        [Test]
        public void 라벨만_단_객체도_들인다()
        {
            var subject = Made("Score", typeof(Text));

            Assert.That(
                Worth.Writing(subject, new Dictionary<Type, List<Watched>>()),
                Is.EqualTo(Worth.Admitted.Drawn));
        }

        [Test]
        public void 아무것도_안_단_객체는_안_들인다()
        {
            var subject = Made("Empty");

            Assert.That(
                Worth.Writing(subject, new Dictionary<Type, List<Watched>>()),
                Is.EqualTo(Worth.Admitted.No));
        }

        [Test]
        public void 근거를_나르는_객체는_근거로_센다()
        {
            // watch list 가 이름 댄 타입 자리에 Unity 자신의 타입을 세운다. `Worth` 가 읽는 것은 그 타입이 `byOwner` 에
            // 있는지뿐이라 어느 타입을 쓰든 같은 길을 지난다.
            var subject = Made("Player", typeof(Rigidbody));
            var byOwner = new Dictionary<Type, List<Watched>>
            {
                { typeof(Rigidbody), new List<Watched>() }
            };

            Assert.That(Worth.Writing(subject, byOwner), Is.EqualTo(Worth.Admitted.Evidence));
        }

        [Test]
        public void 근거와_라벨을_둘_다_단_객체는_근거로_센다()
        {
            // 예산이 둘로 갈리는 자리다. 이런 객체가 화면 요소로 세어지면 canvas 하나가 근거 쪽 예산을 다 먹고, pulse 가
            // 존재하는 이유인 객체들이 밀려난다.
            var subject = Made("HealthBar", typeof(Image), typeof(Canvas));
            var byOwner = new Dictionary<Type, List<Watched>>
            {
                { typeof(Canvas), new List<Watched>() }
            };

            Assert.That(Worth.Writing(subject, byOwner), Is.EqualTo(Worth.Admitted.Evidence));
        }

        [Test]
        public void 화면_안인지를_사각형으로_답한다()
        {
            Assert.That(Sight.OnScreen(new Rect(10f, 10f, 100f, 40f), 800, 600), Is.True);

            // 화면 왼쪽 밖으로 완전히 나간 것.
            Assert.That(Sight.OnScreen(new Rect(-200f, 10f, 100f, 40f), 800, 600), Is.False);

            // 오른쪽 밖.
            Assert.That(Sight.OnScreen(new Rect(900f, 10f, 100f, 40f), 800, 600), Is.False);

            // 걸쳐 있으면 보인다. 절반이 보이는 것도 보이는 것이다.
            Assert.That(Sight.OnScreen(new Rect(-50f, 10f, 100f, 40f), 800, 600), Is.True);

            // 넓이가 0 인 것은 아무도 볼 수 없다. `ScreenArea.Of` 가 아무 데도 아닌 것에 그것을 준다.
            Assert.That(Sight.OnScreen(new Rect(10f, 10f, 0f, 40f), 800, 600), Is.False);
        }

        [Test]
        public void 한가운데를_품는_것만_덮는_것으로_센다()
        {
            var label = new Rect(100f, 100f, 100f, 40f);

            Assert.That(Sight.Covers(new Rect(0f, 0f, 800f, 600f), label), Is.True);

            // 걸치기만 하는 것은 안 덮는다. 겹친 넓이를 재면 반쯤 가려진 것에 대해 어느 게임에도 맞지 않는 경계를 골라야 한다.
            Assert.That(Sight.Covers(new Rect(0f, 0f, 120f, 600f), label), Is.False);

            // 넓이가 0 인 것은 아무것도 못 덮는다.
            Assert.That(Sight.Covers(new Rect(0f, 0f, 0f, 600f), label), Is.False);
        }

        /// <summary>같은 canvas 아래, 화면 좌표가 정해진 요소 하나.</summary>
        private RectTransform Under(GameObject canvas, string name, Rect area, Type draws)
        {
            var subject = new GameObject(name, draws);
            subject.transform.SetParent(canvas.transform, false);

            var rect = subject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(area.width, area.height);
            rect.anchoredPosition = new Vector2(area.x, area.y);

            return rect;
        }

        [Test]
        public void 꺼진_덮개는_덮개로_세지_않는다()
        {
            // 닫힌 모달 — 같은 canvas 아래, HUD 뒤에 선언된 전체 화면 Image — 가 HUD 를 통째로 가려진 것으로 만들면, 화면에
            // 실제로 보이는 것이 목록에서 사라진다. 걷기는 꺼진 객체까지 걷고 `GetWorldCorners` 는 켜짐을 보지 않으므로
            // 여기서 걸러야 한다.
            var canvas = Made("Canvas", typeof(Canvas));
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 600f);

            var hud = Under(canvas, "Score", new Rect(100f, 100f, 100f, 40f), typeof(Text));
            var modal = Under(canvas, "Modal", new Rect(0f, 0f, 800f, 600f), typeof(Image));

            modal.gameObject.SetActive(false);

            var walked = new List<Transform> { hud, modal };
            var closed = Sight.Survey(walked, 800, 600);

            Assert.That(closed[hud.gameObject.GetInstanceID()], Does.Contain("\"covered\":false"));

            // 그리고 열리면 가린다.
            modal.gameObject.SetActive(true);

            var opened = Sight.Survey(walked, 800, 600);

            Assert.That(opened[hud.gameObject.GetInstanceID()], Does.Contain("\"covered\":true"));
        }

        [Test]
        public void 자식은_제_부모를_가리지_않는다()
        {
            // 버튼 위의 캡션은 그 버튼 위에 그려지지만 그 버튼의 일부다.
            var canvas = Made("Canvas", typeof(Canvas));
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 600f);

            var button = Under(canvas, "Exit", new Rect(100f, 100f, 200f, 60f), typeof(Image));
            var caption = Under(button.gameObject, "Caption", new Rect(0f, 0f, 200f, 60f), typeof(Text));

            var found = Sight.Survey(new List<Transform> { button, caption }, 800, 600);

            Assert.That(found[button.gameObject.GetInstanceID()], Does.Contain("\"covered\":false"));
        }

        [Test]
        public void 보이는_요소가_아닌_것은_목록에_없다()
        {
            // 없음은 "안 가려졌다" 가 아니라 "보이는 요소가 아니다" 다. 두 문장이 같은 모양으로 도착하면 읽는 쪽이 배경을
            // 화면 요소로 읽는다.
            var plain = Made("Plain");

            var found = Sight.Survey(new List<Transform> { plain.transform }, 800, 600);

            Assert.That(found.ContainsKey(plain.GetInstanceID()), Is.False);
        }
    }
}
