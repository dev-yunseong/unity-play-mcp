using System.Collections;
using System.Linq;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
