using System;
using System.Collections.Generic;
using UnityPlayMcp.Affordances.Live;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 화면에 그리는 component 를 pulse 가 포함하는 규칙: 무엇을 포함하고, 무엇을 읽고, 지금 보이는지.
    /// </summary>
    /// <remarks>
    /// watch list 는 assembly 에 구운 evidence 에서 오므로 walk 자체는 여기서 돌릴 수 없다
    /// (<see cref="PulseGoneTests"/> 와 같은 제약). 대신 walk 가 읽는 분류, 멤버 목록, 사각형 규칙을 확인한다.
    /// <c>Sight.OnScreen</c> 과 <c>Sight.Covers</c> 는 화면 크기를 인자로 받아 batch mode 에서도 결정적이다.
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

            // Button 은 Graphic 이 아니라 Selectable 이므로 따로 확인한다.
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

            // 값이 없는 요소도 멤버 하나는 가져야 `by` 에 써지고, 읽는 쪽은 `by[].on` 으로 요소를 고른다.
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
            // 게임 필드가 같은 이름을 가지면 그쪽이 우선한다. 같은 이름이 둘이면 읽는 쪽이 어느 것이 evidence 가 지목한 멤버인지 모른다.
            Assert.That(Read(typeof(Text), "text"), Is.Empty);
        }

        [Test]
        public void 프로퍼티로_읽는_멤버로_만든다()
        {
            var found = Read(typeof(Text));

            // `Field` 가 null 이면 component 자신의 `Member` 를 읽는다. `Text.text` 의 backing field 이름은 Unity 버전마다 다를 수 있다.
            Assert.That(found[0].Field, Is.Null);
            Assert.That(found[0].Member, Is.EqualTo("text"));
            Assert.That(found[0].Owner, Is.EqualTo(typeof(Text)));

            // evidence 가 요청하지 않았지만 읽을 수 있어 싣는 값이다.
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
            // watch list 타입 대신 Unity 타입을 쓴다. `Worth` 는 `byOwner` 포함 여부만 보므로 결과는 같다.
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
            // 이런 객체를 화면 요소로 세면 canvas 하나가 evidence 쪽 예산을 다 써서 정작 필요한 객체가 밀려난다.
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

            // 화면 왼쪽 밖.
            Assert.That(Sight.OnScreen(new Rect(-200f, 10f, 100f, 40f), 800, 600), Is.False);

            // 오른쪽 밖.
            Assert.That(Sight.OnScreen(new Rect(900f, 10f, 100f, 40f), 800, 600), Is.False);

            // 일부만 보여도 보이는 것이다.
            Assert.That(Sight.OnScreen(new Rect(-50f, 10f, 100f, 40f), 800, 600), Is.True);

            // 넓이가 0 이면 보이지 않는다. `ScreenArea.Of` 가 위치가 없는 것에 이 값을 준다.
            Assert.That(Sight.OnScreen(new Rect(10f, 10f, 0f, 40f), 800, 600), Is.False);
        }

        [Test]
        public void 한가운데를_품는_것만_덮는_것으로_센다()
        {
            var label = new Rect(100f, 100f, 100f, 40f);

            Assert.That(Sight.Covers(new Rect(0f, 0f, 800f, 600f), label), Is.True);

            // 일부만 겹치면 덮지 않는다. 겹친 넓이로 판정하면 게임마다 맞는 경계가 없다.
            Assert.That(Sight.Covers(new Rect(0f, 0f, 120f, 600f), label), Is.False);

            // 넓이가 0 이면 아무것도 덮지 않는다.
            Assert.That(Sight.Covers(new Rect(0f, 0f, 0f, 600f), label), Is.False);
        }

        /// <summary>같은 canvas 아래 화면 좌표가 정해진 요소를 만든다.</summary>
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
            // 닫힌 전체 화면 모달이 HUD 를 가린 것으로 처리되면 보이는 요소가 목록에서 빠진다. walk 는 꺼진 객체도 돌고
            // `GetWorldCorners` 는 활성 여부를 보지 않으므로 여기서 걸러야 한다.
            var canvas = Made("Canvas", typeof(Canvas));
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 600f);

            var hud = Under(canvas, "Score", new Rect(100f, 100f, 100f, 40f), typeof(Text));
            var modal = Under(canvas, "Modal", new Rect(0f, 0f, 800f, 600f), typeof(Image));

            modal.gameObject.SetActive(false);

            var walked = new List<Transform> { hud, modal };
            var closed = Sight.Survey(walked, 800, 600);

            Assert.That(closed[hud.gameObject.GetInstanceID()], Does.Contain("\"covered\":false"));

            // 열리면 가린다.
            modal.gameObject.SetActive(true);

            var opened = Sight.Survey(walked, 800, 600);

            Assert.That(opened[hud.gameObject.GetInstanceID()], Does.Contain("\"covered\":true"));
        }

        [Test]
        public void 자식은_제_부모를_가리지_않는다()
        {
            // 버튼 위 캡션은 버튼 위에 그려지지만 버튼의 일부다.
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
            // 없음은 "안 가려졌다" 가 아니라 "보이는 요소가 아니다" 다. 같은 모양으로 오면 읽는 쪽이 배경을 화면 요소로 읽는다.
            var plain = Made("Plain");

            var found = Sight.Survey(new List<Transform> { plain.transform }, 800, 600);

            Assert.That(found.ContainsKey(plain.GetInstanceID()), Is.False);
        }
    }
}
