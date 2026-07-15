using System.Collections.Generic;
using Artel.Domain;
using Artel.Serialization;
using Artel.Tracking;
using NUnit.Framework;

namespace Artel.Tests.Tracking
{
    public sealed class SceneStatePollerTests
    {
        [Test]
        public void TryPoll_ScansAtIntervalAndReturnsOnlyChangedScene()
        {
            var scanner = new StubSceneScanner("Lobby");
            var poller = new SceneStatePoller(
                scanner,
                new SceneStateHashTracker(new NewtonsoftJsonCodec()),
                1f);
            poller.Reset(5f);

            Assert.That(poller.TryPoll(5.99f, out _), Is.False);
            Assert.That(scanner.ScanCount, Is.Zero);
            Assert.That(poller.TryPoll(6f, out _), Is.False);
            Assert.That(scanner.ScanCount, Is.EqualTo(1));

            scanner.SceneName = "Game";

            Assert.That(poller.TryPoll(7f, out var changedScene), Is.True);
            Assert.That(changedScene.Scene.Name, Is.EqualTo("Game"));
            Assert.That(scanner.ScanCount, Is.EqualTo(2));
        }

        [Test]
        public void ScanNow_UpdatesBaselineForNextPoll()
        {
            var scanner = new StubSceneScanner("Lobby");
            var poller = new SceneStatePoller(
                scanner,
                new SceneStateHashTracker(new NewtonsoftJsonCodec()),
                1f);
            poller.Reset(0f);

            var currentScene = poller.ScanNow();

            Assert.That(currentScene.Scene.Name, Is.EqualTo("Lobby"));
            Assert.That(poller.TryPoll(1f, out _), Is.False);
        }

        private sealed class StubSceneScanner : ISceneSnapshotScanner
        {
            public StubSceneScanner(string sceneName)
            {
                SceneName = sceneName;
            }

            public string SceneName { get; set; }
            public int ScanCount { get; private set; }

            public SceneScanResult Scan()
            {
                ScanCount++;
                return new SceneScanResult(
                    new SceneSnapshot(1, SceneName, new List<SceneBlock>()),
                    new List<ActionBatchCommit>());
            }
        }
    }
}
