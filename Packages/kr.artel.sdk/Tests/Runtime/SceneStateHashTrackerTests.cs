using Artel.Tracking;
using Artel.Protocol.Dto;
using Artel.Serialization;
using NUnit.Framework;

namespace Artel.Tests.Tracking
{
    public sealed class SceneStateHashTrackerTests
    {
        [Test]
        public void Observe_FirstSceneEstablishesBaseline()
        {
            var tracker = CreateTracker();

            Assert.That(tracker.Observe(CreateScene("Lobby")), Is.False);
        }

        [Test]
        public void Observe_UnchangedSceneDoesNotReportChange()
        {
            var tracker = CreateTracker();
            tracker.Observe(CreateScene("Lobby"));

            Assert.That(tracker.Observe(CreateScene("Lobby")), Is.False);
        }

        [Test]
        public void Observe_ChangedSceneReportsChangeOnce()
        {
            var tracker = CreateTracker();
            tracker.Observe(CreateScene("Lobby"));

            Assert.That(tracker.Observe(CreateScene("Game")), Is.True);
            Assert.That(tracker.Observe(CreateScene("Game")), Is.False);
        }

        [Test]
        public void Reset_ClearsPreviousBaseline()
        {
            var tracker = CreateTracker();
            tracker.Observe(CreateScene("Lobby"));
            tracker.Reset();

            Assert.That(tracker.Observe(CreateScene("Game")), Is.False);
        }

        private static SceneStateHashTracker CreateTracker()
        {
            return new SceneStateHashTracker(new NewtonsoftJsonCodec());
        }

        private static SceneDto CreateScene(string name)
        {
            return new SceneDto { Id = 1, Type = "scene", Name = name };
        }
    }
}
