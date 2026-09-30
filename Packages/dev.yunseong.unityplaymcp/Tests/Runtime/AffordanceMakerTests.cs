using System.Text;
using UnityPlayMcp.Affordances.Scan;
using NUnit.Framework;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// <c>createdBy</c> 가 프리팹을 지목하는지, walk 가 멈춘 곳을 <c>gaps</c> 에 적는지 확인한다.
    /// 틀려도 조용히 틀린다. <c>createdBy</c> 가 비면 소비자는 죽은 코드로 읽는다.
    /// <c>unplaced</c> 는 assembly 에 구운 evidence 에서 나오므로 여기서는 검증하지 않는다.
    /// </summary>
    public sealed class AffordanceMakerTests
    {
        [SetUp]
        public void SetUp() => AffordanceReport.Forget();

        [TearDown]
        public void TearDown() => AffordanceReport.Forget();

        /// <summary>
        /// 항목이 어느 프리팹인지 id 까지 적어, 두 항목이 같은 프리팹인지 구분할 수 있게 한다.
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
        /// walk 가 멈춘 항목은 <c>cut</c> 을 단다. 이 프리팹 너머는 보지 않았다는 뜻이다.
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
        /// 목록이 잘리면 잘렸다고 적는다. 개수만으로는 잘렸는지 알 수 없다.
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

        /// <summary>여덟 이하면 잘림을 적지 않는다.</summary>
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
        /// 같은 필드가 같은 프리팹을 여러 번 쥐어도 한 번만 센다. 아니면 clone 이 여덟 칸을 다 채운다.
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
        /// 깊이 제한으로 멈춘 곳을 <c>gaps</c> 에 적는다. 빈 <c>createdBy</c> 는 "아무도 만들지 않는다" 만 뜻해야 한다.
        /// </summary>
        [Test]
        public void Gaps_RecordWhereTheWalkStopped()
        {
            AffordanceReport.CreatesCut("Deep.Nested", "Holder", "container", "NestedPrefab", 777, "depth");

            Assert.That(AffordanceReport.Compose(), Does.Contain("trace-depth-exceeded:Deep.Nested"));
        }

        /// <summary>프리팹이 가진 component 목록이 잘리면 그것도 적는다.</summary>
        [Test]
        public void Gaps_RecordThatCarriedTypesWereTruncated()
        {
            AffordanceReport.CarriedTruncated("CrowdedPrefab");

            Assert.That(AffordanceReport.Compose(), Does.Contain("carried-truncated:CrowdedPrefab"));
        }

        /// <summary>같은 프리팹이 여러 씬에서 같은 한계에 걸려도 한 번만 적는다.</summary>
        [Test]
        public void Gaps_SayTheSameThingOnce()
        {
            AffordanceReport.CarriedTruncated("CrowdedPrefab");
            AffordanceReport.CarriedTruncated("CrowdedPrefab");

            Assert.That(Occurrences(AffordanceReport.Compose(), "carried-truncated:CrowdedPrefab"), Is.EqualTo(1));
        }

        /// <summary>잘린 것이 없으면 <c>gaps</c> 가 비어 있다.</summary>
        [Test]
        public void Gaps_StaySilentWhenTheWalkFinished()
        {
            AffordanceReport.Creates("Enemy", "Pool", "prefab", "Slime", 42);

            var document = AffordanceReport.Compose();

            Assert.That(document, Does.Not.Contain("trace-depth-exceeded"));
            Assert.That(document, Does.Not.Contain("carried-truncated"));
        }

        /// <summary><c>createdBy</c> 형태가 바뀌었으므로 schema 버전을 올린다.</summary>
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
