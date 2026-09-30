using System.Collections.Generic;
using UnityPlayMcp.Affordances.Live;
using NUnit.Framework;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// pulse 가 사라진 객체를 보고하되, walk 하지 않은 객체를 사라졌다고 보고하지 않는지 확인한다.
    /// 한도 때문에 walk 하지 못한 객체를 사라졌다고 하면 읽는 쪽이 살아 있는 객체를 지운다.
    /// watch list 는 assembly 에 구운 evidence 에서 오므로 walk 대신 규칙이 읽는 `ledger` 두 개를 직접 넘긴다.
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
            // 사라짐을 멤버 키로 세면 `CombineZone` 처럼 값이 없는 객체의 잔상이 지워지지 않는다.
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
            // `ledger` 는 읽은 객체를 모두 가지므로, 변하지 않아 델타에 없는 것과 만나지 못한 것을 구분한다.
            var gone = LiveState.Gone(
                Ledger("Battle/Card(Clone)[16]|active"),
                Ledger("Battle/Card(Clone)[16]|active"),
                false,
                0);

            Assert.That(gone, Is.Null);
        }

        [Test]
        public void 잘린_pulse_는_아무_말도_하지_않는다()
        {
            // 한도 때문에 walk 하지 않은 객체다. 잔상이 한 pulse 더 남는 편이 살아 있는 객체를 지우는 것보다 낫다.
            var gone = LiveState.Gone(
                Ledger("Battle/Card(Clone)[16]|active"),
                Ledger(),
                false,
                12);

            Assert.That(gone, Is.Null);
        }

        [Test]
        public void 전량_pulse_는_말할_필요가_없다()
        {
            // 읽는 쪽은 whole pulse 로 받은 것을 통째로 교체한다.
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
            // 읽는 쪽은 객체 단위로 가지므로 같은 객체의 키가 여럿 사라져도 이름은 한 번만 보낸다.
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
