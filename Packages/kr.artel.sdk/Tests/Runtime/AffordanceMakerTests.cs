using System.Text;
using Artel.Affordances.Scan;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// <c>createdBy</c> 가 프리팹을 정체로 지목하는지, 걷기가 버린 것을 적는지 검증한다(ARTEL-459).
    ///
    /// 이 표가 틀리면 조용히 틀린다. <c>createdBy</c> 가 비면 소비자는 죽은 코드로 읽고, 폐기
    /// 판정은 되돌려 보는 사람이 없다. 실제로 <c>SpellObj</c> 가 한 번 그렇게 오판됐다.
    ///
    /// <c>unplaced</c> 본문은 여기서 검증할 수 없다 — 그 목록은 어셈블리에 구워진 근거에서 나오고
    /// 테스트 어셈블리에는 그것이 없다. 그래서 형식은 [WriteMaker] 로, 버린 자리는 <c>gaps</c> 로
    /// 각각 그 값이 실제로 나오는 자리에서 본다.
    /// </summary>
    public sealed class AffordanceMakerTests
    {
        [SetUp]
        public void SetUp() => AffordanceReport.Forget();

        [TearDown]
        public void TearDown() => AffordanceReport.Forget();

        /// <summary>
        /// 항목이 어느 프리팹인지 말한다.
        ///
        /// 문자열 하나였을 때는 두 항목이 같은 프리팹인지 리포트만 봐서 답할 수 없었다. 실측이
        /// 정확히 그랬다 — <c>MagicEnemy.fireShoot</c> 는 YellowProjectile(6334) 을,
        /// <c>BossEnemy.fireShoot</c> 는 LightGreenProjectile(6278) 을 쥔다.
        /// </summary>
        [Test]
        public void Maker_NamesThePrefabBehindTheField()
        {
            var text = new StringBuilder();

            AffordanceReport.WriteMaker(text, new AffordanceReport.Maker
            {
                Field = "Combat.Enemies.MagicEnemy.fireShoot",
                Prefab = "YellowProjectile",
                PrefabId = 6334
            });

            Assert.That(
                text.ToString(),
                Is.EqualTo("{\"field\":\"Combat.Enemies.MagicEnemy.fireShoot\",\"prefab\":\"YellowProjectile\",\"prefabId\":6334}"));
        }

        /// <summary>
        /// 걷기가 멈춘 항목은 <c>cut</c> 을 단다.
        ///
        /// <c>cut</c> 이 말하는 것은 "이 프리팹을 못 봤다"가 아니라 "이 프리팹 뒤로 더 걷지
        /// 않았다"이다. 그 너머에 또 다른 프리팹이 있었다면 그것은 여전히 리포트에 없다.
        /// </summary>
        [Test]
        public void Maker_SaysWhereTheWalkStopped()
        {
            var text = new StringBuilder();

            AffordanceReport.WriteMaker(text, new AffordanceReport.Maker
            {
                Field = "Holder.container",
                Prefab = "NestedPrefab",
                PrefabId = 777,
                Cut = "depth"
            });

            Assert.That(text.ToString(), Does.Contain("\"cut\":\"depth\""));
            Assert.That(text.ToString(), Does.Contain("\"prefabId\":777"));
        }

        /// <summary>
        /// 폭이 잘리면 잘렸다고 적는다.
        ///
        /// 실측에서 <c>SpellObj</c> 는 여덟이 적히고 일곱이 사라졌는데 사라졌다는 표시가 없었다.
        /// 8 이라는 숫자만 보고는 다 실린 것인지 잘린 것인지 알 수 없다.
        /// </summary>
        [Test]
        public void Gaps_RecordThatTheMakerListWasTruncated()
        {
            for (var maker = 0; maker < 15; maker++)
            {
                AffordanceReport.Creates(
                    "Combat.Spells.SpellObj", "Combat.Spells.Shoot", "prefab" + maker, "Shoot" + maker, 6000 + maker);
            }

            Assert.That(AffordanceReport.Compose(), Does.Contain("makers-truncated:Combat.Spells.SpellObj"));
        }

        /// <summary>여덟 이하는 잘리지 않았으므로 그런 말을 하지 않는다.</summary>
        [Test]
        public void Gaps_StaySilentWhenNothingWasTruncated()
        {
            for (var maker = 0; maker < 8; maker++)
            {
                AffordanceReport.Creates("Enemy", "Pool", "prefab" + maker, "Slime" + maker, 100 + maker);
            }

            Assert.That(AffordanceReport.Compose(), Does.Not.Contain("makers-truncated"));
        }

        /// <summary>
        /// 같은 필드가 같은 프리팹을 여러 번 쥐어도 한 번만 센다. 그렇지 않으면 클론 다섯이 자리를
        /// 다 차지해 여덟 칸이 프리팹 하나로 채워진다.
        /// </summary>
        [Test]
        public void Makers_DoNotSpendTheBudgetOnTheSamePrefabTwice()
        {
            for (var repeat = 0; repeat < 20; repeat++)
            {
                AffordanceReport.Creates("Enemy", "Pool", "prefab", "Slime", 42);
            }

            Assert.That(AffordanceReport.Compose(), Does.Not.Contain("makers-truncated"));
        }

        /// <summary>
        /// 깊이에 막힌 자리를 <c>gaps</c> 에 적는다.
        ///
        /// 이것이 없으면 빈 <c>createdBy</c> 가 "아무도 만들지 않는다"와 "우리가 못 걸어갔다" 둘
        /// 다를 뜻한다. <b>빈 목록은 앞의 것만 뜻해야 한다.</b>
        /// </summary>
        [Test]
        public void Gaps_RecordWhereTheWalkStopped()
        {
            AffordanceReport.CreatesCut("Deep.Nested", "Holder", "container", "NestedPrefab", 777, "depth");

            Assert.That(AffordanceReport.Compose(), Does.Contain("trace-depth-exceeded:Deep.Nested"));
        }

        /// <summary>프리팹이 나르는 컴포넌트 목록이 잘리면 그것도 적는다.</summary>
        [Test]
        public void Gaps_RecordThatCarriedTypesWereTruncated()
        {
            AffordanceReport.CarriedTruncated("CrowdedPrefab");

            Assert.That(AffordanceReport.Compose(), Does.Contain("carried-truncated:CrowdedPrefab"));
        }

        /// <summary>같은 프리팹이 여러 씬에서 같은 한계에 걸려도 한 번만 말한다.</summary>
        [Test]
        public void Gaps_SayTheSameThingOnce()
        {
            AffordanceReport.CarriedTruncated("CrowdedPrefab");
            AffordanceReport.CarriedTruncated("CrowdedPrefab");

            Assert.That(Occurrences(AffordanceReport.Compose(), "carried-truncated:CrowdedPrefab"), Is.EqualTo(1));
        }

        /// <summary>아무것도 버리지 않았으면 그런 말이 없다.</summary>
        [Test]
        public void Gaps_StaySilentWhenTheWalkFinished()
        {
            AffordanceReport.Creates("Enemy", "Pool", "prefab", "Slime", 42);

            var document = AffordanceReport.Compose();

            Assert.That(document, Does.Not.Contain("trace-depth-exceeded"));
            Assert.That(document, Does.Not.Contain("carried-truncated"));
        }

        /// <summary>소비자가 문자열 배열을 가정하고 있으므로 세대를 올린다.</summary>
        [Test]
        public void Schema_MovesBecauseCreatedByChangedShape()
        {
            Assert.That(AffordanceReport.SchemaVersion, Is.EqualTo(7));
            Assert.That(AffordanceReport.Compose(), Does.Contain("\"schema\":7"));
        }

        private static int Occurrences(string text, string needle)
        {
            var count = 0;

            for (var at = text.IndexOf(needle, System.StringComparison.Ordinal);
                 at >= 0;
                 at = text.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
