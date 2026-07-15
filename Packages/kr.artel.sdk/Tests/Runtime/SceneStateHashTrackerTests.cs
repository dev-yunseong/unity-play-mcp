using Artel.Tracking;
using NUnit.Framework;

namespace Artel.Tests.Tracking
{
    public sealed class SceneStateHashTrackerTests
    {
        [Test]
        public void Observe_FirstSceneEstablishesBaseline()
        {
            var tracker = new SceneStateHashTracker();

            Assert.That(tracker.Observe("{\"name\":\"Lobby\"}"), Is.False);
        }

        [Test]
        public void Observe_UnchangedSceneDoesNotReportChange()
        {
            var tracker = new SceneStateHashTracker();
            tracker.Observe("{\"name\":\"Lobby\"}");

            Assert.That(tracker.Observe("{\"name\":\"Lobby\"}"), Is.False);
        }

        [Test]
        public void Observe_ChangedSceneReportsChangeOnce()
        {
            var tracker = new SceneStateHashTracker();
            tracker.Observe("{\"name\":\"Lobby\"}");

            Assert.That(tracker.Observe("{\"name\":\"Game\"}"), Is.True);
            Assert.That(tracker.Observe("{\"name\":\"Game\"}"), Is.False);
        }

        [Test]
        public void Reset_ClearsPreviousBaseline()
        {
            var tracker = new SceneStateHashTracker();
            tracker.Observe("{\"name\":\"Lobby\"}");
            tracker.Reset();

            Assert.That(tracker.Observe("{\"name\":\"Game\"}"), Is.False);
        }
    }
}
