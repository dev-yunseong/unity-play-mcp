using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityPlayMcp.Play;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 선택적 semantic provider 등록소. provider 하나의 오류가 기본 기능이나 다른 provider 를 막지 않는지 확인한다.
    /// </summary>
    public sealed class PlaySemanticsTests
    {
        private sealed class Probe : PlaySemanticProvider
        {
            private readonly string id;
            private readonly Action<PlayObservationContext, PlayProviderOutput> describe;

            public Probe(string id, Action<PlayObservationContext, PlayProviderOutput> describe = null)
            {
                this.id = id;
                this.describe = describe;
            }

            public override string Id { get { return id; } }

            public override void Describe(PlayObservationContext context, PlayProviderOutput output)
            {
                if (describe != null)
                {
                    describe(context, output);
                }
            }
        }

        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            PlaySemantics.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlaySemantics.ResetForTests();
            foreach (var item in spawned)
            {
                UnityEngine.Object.DestroyImmediate(item);
            }

            spawned.Clear();
        }

        private static PlayObservationContext Context()
        {
            return new PlayObservationContext(true, 1, "scene", 0d, new List<GameObject>());
        }

        private GameObject Spawn()
        {
            var entity = new GameObject("provider fixture");
            spawned.Add(entity);
            return entity;
        }

        [Test]
        public void Register_RejectsADuplicateIdAndKeepsTheFirstProvider()
        {
            Assert.That(PlaySemantics.Register(new Probe("game.rules")), Is.True);

            Assert.That(PlaySemantics.Register(new Probe("game.rules")), Is.False);
            Assert.That(PlaySemantics.Statuses().Count, Is.EqualTo(1));
        }

        [Test]
        public void Register_RejectsAnInvalidId()
        {
            Assert.That(PlaySemantics.Register(new Probe("has space")), Is.False);
            Assert.That(PlaySemantics.Register(new Probe("")), Is.False);
            Assert.That(PlaySemantics.Register(null), Is.False);
        }

        [Test]
        public void Describe_IsolatesAProviderThatThrows()
        {
            PlaySemantics.Register(new Probe("bad", (context, output) => { throw new InvalidOperationException("boom"); }));
            var entity = Spawn();
            PlaySemantics.Register(new Probe("good", (context, output) => output.AddFact(entity, "hp", 3, "hp", PlayVisibility.Player)));

            var outputs = PlaySemantics.Describe(Context());

            Assert.That(outputs.Count, Is.EqualTo(2), "the failing provider must not stop the others");
            var statuses = PlaySemantics.Statuses();
            Assert.That(statuses.Find(status => status.Id == "bad").LastError, Does.Contain("boom"));
            var good = outputs.Find(pair => pair.Key == "good").Value;
            Assert.That(good.Facts.Count, Is.EqualTo(1));
        }

        [Test]
        public void Describe_DisablesAProviderThatIsSlowRepeatedly()
        {
            PlaySemantics.Register(new Probe("slow", (context, output) => Thread.Sleep(20)));

            for (var i = 0; i < PlaySemantics.MaxSlowRuns; i++)
            {
                PlaySemantics.Describe(Context());
            }

            var status = PlaySemantics.Statuses()[0];
            Assert.That(status.Status, Is.EqualTo("disabled_slow"));
            Assert.That(status.SlowCount, Is.EqualTo(PlaySemantics.MaxSlowRuns));
            Assert.That(PlaySemantics.Describe(Context()), Is.Empty, "a disabled provider is no longer called");
        }

        [Test]
        public void Describe_ResetsTheSlowStreakAfterAFastRun()
        {
            var slow = true;
            PlaySemantics.Register(new Probe("flaky", (context, output) =>
            {
                if (slow)
                {
                    Thread.Sleep(20);
                }
            }));

            PlaySemantics.Describe(Context());
            PlaySemantics.Describe(Context());
            slow = false;
            PlaySemantics.Describe(Context());
            slow = true;
            PlaySemantics.Describe(Context());

            Assert.That(PlaySemantics.Statuses()[0].Status, Is.EqualTo("active"));
        }

        [Test]
        public void Output_DropsInvalidEntriesAndRecordsWhy()
        {
            var entity = Spawn();
            var output = new PlayProviderOutput();

            output.AddFact(entity, "ok", 1);
            output.AddFact(entity, "", 1);
            output.AddFact(entity, "object", new object());
            output.AddRelation(entity, "no-namespace", entity);
            output.AddRelation(entity, "ns:owner", entity);
            output.AddAction(entity, new PlayProviderAction { Id = "bad id" });

            Assert.That(output.Facts.Count, Is.EqualTo(1));
            Assert.That(output.Relations.Count, Is.EqualTo(1));
            Assert.That(output.Actions.Count, Is.Zero);
            Assert.That(output.Errors.Count, Is.EqualTo(4));
        }

        [Test]
        public void Facts_DefaultToDebugVisibilityAndUnknownFactsCarryNoValue()
        {
            var entity = Spawn();
            var output = new PlayProviderOutput();

            output.AddFact(entity, "hp", 5);
            output.AddUnknownFact(entity, "cost", "not declared");

            Assert.That(output.Facts[0].Visibility, Is.EqualTo(PlayVisibility.Debug));
            Assert.That(output.Facts[1].Status, Is.EqualTo("unknown"));
            Assert.That(output.Facts[1].Value, Is.Null, "an unknown value must not be filled with a default");
        }

        [Test]
        public void AddAction_DropsAnActionWhoseRecipeCouldNotBeBuilt()
        {
            var entity = Spawn();
            var output = new PlayProviderOutput();
            var action = new PlayProviderAction { Id = "act" };
            action.Recipe.Add(null);

            output.AddAction(entity, action);

            Assert.That(output.Actions, Is.Empty, "a partial recipe would run only some of the steps");
        }

        [Test]
        public void VisibleToPlayer_FollowsTheMostRestrictiveProvider()
        {
            var entity = Spawn();
            PlaySemantics.Register(new VisibilityProbe("a", true));
            PlaySemantics.Register(new VisibilityProbe("b", false));

            Assert.That(PlaySemantics.HasVisibilityProvider(), Is.True);
            Assert.That(PlaySemantics.VisibleToPlayer(entity), Is.False);
        }

        private sealed class VisibilityProbe : PlaySemanticProvider
        {
            private readonly string id;
            private readonly bool visible;

            public VisibilityProbe(string id, bool visible)
            {
                this.id = id;
                this.visible = visible;
            }

            public override string Id { get { return id; } }
            public override bool DeclaresPlayerVisibility { get { return true; } }
            public override bool IsVisibleToPlayer(GameObject entity) { return visible; }
            public override void Describe(PlayObservationContext context, PlayProviderOutput output) { }
        }

        [Test]
        public void Emit_RecordsTheFrameAndProviderProvenanceAndKeepsACursor()
        {
            PlaySemantics.Emit("game.rules", "goal_reached", "goal", null, 3, PlayVisibility.Player);
            PlaySemantics.Emit("game.rules", "secret", null, null, null);

            long next, dropped;
            var events = PlaySemantics.ReadEvents(0, 10, out next, out dropped);

            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[0].ProviderId, Is.EqualTo("game.rules"));
            Assert.That(events[0].PlayerVisible, Is.True);
            Assert.That(events[1].PlayerVisible, Is.False, "the default is debug-only");
            Assert.That(next, Is.EqualTo(2));

            var after = PlaySemantics.ReadEvents(1, 10, out next, out dropped);
            Assert.That(after.Count, Is.EqualTo(1));
        }

        [Test]
        public void Emit_DropsTheOldestEventsBeyondCapacityAndCountsThem()
        {
            for (var i = 0; i < PlaySemantics.EventCapacity + 5; i++)
            {
                PlaySemantics.Emit("p", "tick");
            }

            long next, dropped;
            var events = PlaySemantics.ReadEvents(0, 5000, out next, out dropped);

            Assert.That(events.Count, Is.EqualTo(PlaySemantics.EventCapacity));
            Assert.That(dropped, Is.EqualTo(5));
        }

        [Test]
        public void Predicates_ProduceTheJsonTheServerParses()
        {
            Assert.That(PlayPredicates.SceneIs("Main"), Is.EqualTo("{\"sceneIs\":\"Main\"}"));
            Assert.That(
                PlayPredicates.FactCompare("p", "energy", "gte", 3.5, "energy"),
                Is.EqualTo("{\"fact\":{\"providerId\":\"p\",\"name\":\"energy\",\"op\":\"gte\",\"value\":3.5,\"unit\":\"energy\"}}"));
        }
    }
}
