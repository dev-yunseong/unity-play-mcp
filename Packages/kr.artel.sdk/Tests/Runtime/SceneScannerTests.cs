using System.Collections;
using System.Linq;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Tests.Tracking;
using Artel.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    public sealed class SceneScannerTests
    {
        private GameObject gameObject;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("scene scanner target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void Scan_UsesUnitySceneAndGameObjectIdentifiers()
        {
            var scanner = new SceneScanner();

            var result = scanner.Scan();
            var block = result.Scene.Children.Single(child => child.Name == gameObject.name);

            Assert.That(result.Scene.Id, Is.EqualTo(SceneManager.GetActiveScene().handle));
            Assert.That(block.Id, Is.EqualTo(gameObject.GetInstanceID()));
            Assert.That(scanner.TryGetTarget(block.Id, out _), Is.True);
        }

        [UnityTest]
        public IEnumerator CreateReport_ListsBuildScenesAndScansThem()
        {
            SceneScanReportDto report = null;

            yield return SceneScanReporter.CreateReport(result => report = result);

            Assert.That(report, Is.Not.Null);
            Assert.That(report.ScannedScenes, Is.Not.Empty);
            Assert.That(
                report.ScannedScenes.Any(scene =>
                    scene.Children.Any(child => child.Name == gameObject.name)),
                Is.True);
        }

        [Test]
        public void Scan_SkipsInactiveObjectsByDefault()
        {
            var child = new GameObject("inactive child");
            child.transform.SetParent(gameObject.transform);
            child.SetActive(false);

            var block = new SceneScanner().Scan().Scene.Children
                .Single(candidate => candidate.Name == gameObject.name);

            Assert.That(block.Active, Is.True);
            Assert.That(block.Children, Is.Empty);
        }

        [Test]
        public void Scan_Full_ReportsInactiveObjectsAsInactive()
        {
            var child = new GameObject("inactive child");
            child.transform.SetParent(gameObject.transform);
            child.SetActive(false);

            var block = new SceneScanner().Scan(SceneScanOptions.Full).Scene.Children
                .Single(candidate => candidate.Name == gameObject.name);
            var inactive = block.Children.Single();

            Assert.That(inactive.Name, Is.EqualTo("inactive child"));
            Assert.That(inactive.Active, Is.False);
        }

        [Test]
        public void Scan_Full_ReadsGameBehaviourFieldsAndLeavesEngineComponentsOut()
        {
            var uiObject = new GameObject(
                "full scan target",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            uiObject.transform.SetParent(gameObject.transform);
            uiObject.AddComponent<SerializedFixtureBehaviour>();

            var components = new SceneScanner().Scan(SceneScanOptions.Full).Scene.Children
                .Single(candidate => candidate.Name == gameObject.name)
                .Children
                .Single(candidate => candidate.Name == uiObject.name)
                .Components;
            var fixture = components
                .OfType<TrackedComponent>()
                .Single(component => component.ComponentType == typeof(SerializedFixtureBehaviour).FullName);

            Assert.That(fixture.States.Select(state => state.Name), Contains.Item("Level"));
            Assert.That(fixture.States.Single(state => state.Name == "Level").Value, Is.EqualTo(3));
            Assert.That(
                components.OfType<TrackedComponent>().Any(component =>
                    component.ComponentType == typeof(Image).FullName),
                Is.False,
                "Engine components would bury the game's own fields.");
        }

        [Test]
        public void Scan_Full_KeepsTaggedStateTagsInsteadOfReadingTheFieldTwice()
        {
            gameObject.AddComponent<TrackedFixtureBehaviour>();

            var tracked = new SceneScanner().Scan(SceneScanOptions.Full).Scene.Children
                .Single(candidate => candidate.Name == gameObject.name)
                .Components
                .OfType<TrackedComponent>()
                .Single(component => component.ComponentType == typeof(TrackedFixtureBehaviour).FullName);
            var hp = tracked.States.Single(state => state.Name == "Hp");

            Assert.That(hp.Tag, Is.EqualTo("hp"));
            Assert.That(hp.Value, Is.EqualTo(10));
        }

        [Test]
        public void Scan_Default_ReadsOnlyTaggedState()
        {
            gameObject.AddComponent<SerializedFixtureBehaviour>();

            var components = new SceneScanner().Scan().Scene.Children
                .Single(candidate => candidate.Name == gameObject.name)
                .Components;

            Assert.That(
                components.OfType<TrackedComponent>().Any(component =>
                    component.ComponentType == typeof(SerializedFixtureBehaviour).FullName),
                Is.False);
        }

        [Test]
        public void Scan_KeepsBlockIdWhenHierarchyOrderChanges()
        {
            var scanner = new SceneScanner();
            var firstId = scanner.Scan().Scene.Children
                .Single(child => child.Name == gameObject.name)
                .Id;
            var sibling = new GameObject("earlier sibling");
            sibling.transform.SetSiblingIndex(0);

            try
            {
                var secondId = scanner.Scan().Scene.Children
                    .Single(child => child.Name == gameObject.name)
                    .Id;

                Assert.That(secondId, Is.EqualTo(firstId));
            }
            finally
            {
                Object.DestroyImmediate(sibling);
            }
        }
    }
}
