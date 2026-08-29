using System;
using Artel.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Tracking
{
    public sealed class ActionTrackingTests
    {
        private GameObject gameObject;
        private TrackedFixtureBehaviour fixture;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("tracked fixture");
            fixture = gameObject.AddComponent<TrackedFixtureBehaviour>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void IlPostProcessor_InjectsActionSourceAndRecordsSuccess()
        {
            Assert.That(fixture, Is.InstanceOf<IArtelActionSource>());

            var result = fixture.Attack(3);
            var snapshot = ActionSource.ArtelActionBuffer.Snapshot();

            Assert.That(result, Is.EqualTo(6));
            Assert.That(snapshot.Actions, Has.Count.EqualTo(1));
            Assert.That(snapshot.Actions[0].Tag, Is.EqualTo("attack"));
            Assert.That(snapshot.Actions[0].Name, Is.EqualTo(nameof(TrackedFixtureBehaviour.Attack)));
            Assert.That(snapshot.Actions[0].Success, Is.True);
            Assert.That(snapshot.Actions[0].ReturnValue, Is.EqualTo(6));
            Assert.That(snapshot.Actions[0].Timestamp, Is.Not.EqualTo(default(DateTimeOffset)));

            fixture.Ping();
            var voidAction = ActionSource.ArtelActionBuffer.Snapshot().Actions[1];
            Assert.That(voidAction.Tag, Is.EqualTo("ping"));
            Assert.That(voidAction.ReturnValue, Is.Null);
        }

        [Test]
        public void IlPostProcessor_RecordsFailureAndRethrowsOriginalException()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => fixture.Fail());
            var snapshot = ActionSource.ArtelActionBuffer.Snapshot();

            Assert.That(exception.Message, Is.EqualTo("boom"));
            Assert.That(snapshot.Actions, Has.Count.EqualTo(1));
            Assert.That(snapshot.Actions[0].Success, Is.False);
            Assert.That(snapshot.Actions[0].ErrorType, Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(snapshot.Actions[0].ErrorMessage, Is.EqualTo("boom"));
        }

        [Test]
        public void Commit_RemovesSnapshotOnlyAndPreservesLaterActions()
        {
            var source = ActionSource;
            fixture.Attack(1);
            var firstSnapshot = source.ArtelActionBuffer.Snapshot();

            fixture.Attack(2);
            source.ArtelActionBuffer.Commit(firstSnapshot.Watermark);
            var remaining = source.ArtelActionBuffer.Snapshot();

            Assert.That(firstSnapshot.Actions, Has.Count.EqualTo(1));
            Assert.That(remaining.Actions, Has.Count.EqualTo(1));
            Assert.That(remaining.Actions[0].ReturnValue, Is.EqualTo(4));
            Assert.That(remaining.Actions[0].Sequence, Is.GreaterThan(firstSnapshot.Watermark));
        }

        [Test]
        public void Snapshot_DoesNotConsumeActions()
        {
            var source = ActionSource;
            fixture.Attack(1);

            var first = source.ArtelActionBuffer.Snapshot();
            var second = source.ArtelActionBuffer.Snapshot();

            Assert.That(first.Actions, Has.Count.EqualTo(1));
            Assert.That(second.Actions, Has.Count.EqualTo(1));
            Assert.That(second.Actions[0].Sequence, Is.EqualTo(first.Actions[0].Sequence));
        }

        private IArtelActionSource ActionSource => (IArtelActionSource)(object)fixture;
    }
}
