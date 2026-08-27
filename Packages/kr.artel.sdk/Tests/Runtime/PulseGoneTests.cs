using System.Collections.Generic;
using Artel.Affordances.Live;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// 판독이 사라진 객체를 말하는지, 그리고 <b>안 걸은 것을 사라졌다고 말하지 않는지</b> 검증한다(ARTEL-651).
    ///
    /// 뒤쪽이 이 규칙의 어려운 절반이다. 파괴를 말하는 것은 쉽고, 한도에 걸려 걷지 못한 것을 사라졌다고 하지
    /// 않는 것이 어렵다 — 거기서 틀리면 읽는 쪽이 살아 있는 객체를 지우고, 그것은 아무도 되돌려 주지 않는다.
    ///
    /// 걷기 자체는 여기서 돌릴 수 없다. watch list 가 어셈블리에 구워진 근거에서 오고 테스트 어셈블리에는
    /// 그것이 없다. 그래서 규칙이 실제로 읽는 것 — 장부 두 개 — 을 그대로 놓고 본다.
    /// </summary>
    public sealed class PulseGoneTests
    {
        private static Dictionary<string, string> Ledger(params string[] keys)
        {
            var made = new Dictionary<string, string>();

            foreach (var key in keys)
            {
                made[key] = "true";
            }

            return made;
        }

        [Test]
        public void 파괴된_객체를_말한다()
        {
            var gone = LiveState.Gone(
                Ledger("Battle/Card(Clone)[16]|active", "Battle/Word[12]|active"),
                Ledger("Battle/Word[12]|active"),
                false,
                0);

            Assert.That(gone, Is.EqualTo(new[] { "Battle/Card(Clone)[16]" }));
        }

        [Test]
        public void 값을_안_든_객체도_사라지면_말한다()
        {
            // 사라짐을 멤버 키로 세면 이것을 놓친다. `CombineZone` 처럼 누를 수만 있고 값은 하나도
            // 안 내놓는 객체가 그렇고, 그런 잔상은 영영 안 지워진다.
            var gone = LiveState.Gone(
                Ledger("Battle/CombineZone[1]|active", "Battle/CombineZone[1]|offers"),
                Ledger(),
                false,
                0);

            Assert.That(gone, Is.EqualTo(new[] { "Battle/CombineZone[1]" }));
        }

        [Test]
        public void 가만히_있는_객체는_사라진_것이_아니다()
        {
            // 장부는 읽은 전부를 쥔다. 안 움직여서 델타에 안 실린 것과 못 만난 것은 여기서 갈린다.
            var gone = LiveState.Gone(
                Ledger("Battle/Card(Clone)[16]|active"),
                Ledger("Battle/Card(Clone)[16]|active"),
                false,
                0);

            Assert.That(gone, Is.Null);
        }

        [Test]
        public void 잘린_판독은_아무_말도_하지_않는다()
        {
            // 한도에 걸려 안 걸은 객체다. 사라졌다고 하면 읽는 쪽이 살아 있는 것을 지운다 —
            // 잔상이 한 판독 더 남는 쪽이 싸다.
            var gone = LiveState.Gone(
                Ledger("Battle/Card(Clone)[16]|active"),
                Ledger(),
                false,
                12);

            Assert.That(gone, Is.Null);
        }

        [Test]
        public void 전량_판독은_말할_필요가_없다()
        {
            // 읽는 쪽이 전량 판독에서 쥔 것을 통째로 갈아치운다.
            var gone = LiveState.Gone(
                Ledger("Battle/Card(Clone)[16]|active"),
                Ledger(),
                true,
                0);

            Assert.That(gone, Is.Null);
        }

        [Test]
        public void 이름은_객체_하나에_하나다()
        {
            // 같은 객체의 키 여럿이 함께 사라져도 이름은 한 번이다. 읽는 쪽이 객체 단위로 들고 있다.
            var gone = LiveState.Gone(
                Ledger(
                    "Battle/Card(Clone)[16]|active",
                    "Battle/Card(Clone)[16]|world",
                    "Battle/Card(Clone)[16]|Cards.Card::cardType"),
                Ledger(),
                false,
                0);

            Assert.That(gone, Is.EqualTo(new[] { "Battle/Card(Clone)[16]" }));
        }
    }
}
