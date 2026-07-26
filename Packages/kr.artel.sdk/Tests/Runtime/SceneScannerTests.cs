using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Artel.Domain;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Tests.Tracking;
using Artel.Tracking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    public sealed class SceneScannerTests
    {
        private GameObject gameObject;
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("scene scanner target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
            foreach (var spawnedObject in spawned)
            {
                // A child spawned under another spawned object is already gone by the time its turn
                // comes.
                if (spawnedObject != null)
                {
                    Object.DestroyImmediate(spawnedObject);
                }
            }

            spawned.Clear();
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

        [Test]
        public void Scan_ReportsWhetherAButtonAcceptsAPress()
        {
            Spawn("live button", typeof(Button));
            var locked = Spawn("locked button", typeof(Button));
            locked.GetComponent<Button>().interactable = false;
            var turnedOff = Spawn("turned off button", typeof(Button));
            turnedOff.GetComponent<Button>().enabled = false;

            var scene = new SceneScanner().Scan().Scene;

            Assert.That(ButtonOf(scene, "live button").Interactable, Is.True);
            Assert.That(ButtonOf(scene, "locked button").Interactable, Is.False);

            // IsInteractable alone says true here. Unity's event system still refuses to deliver a
            // click to a disabled component, so the SDK must refuse it too.
            Assert.That(ButtonOf(scene, "turned off button").Interactable, Is.False);
        }

        [Test]
        public void Scan_ReportsAButtonBlockedByItsCanvasGroupAsNotInteractable()
        {
            var panel = Spawn("locked panel");
            panel.AddComponent<CanvasGroup>().interactable = false;
            var child = Spawn("button in locked panel", typeof(Button));
            child.transform.SetParent(panel.transform);

            var scene = new SceneScanner().Scan().Scene;
            var block = scene.Children
                .Single(candidate => candidate.Name == "locked panel")
                .Children
                .Single();

            Assert.That(block.Components.OfType<ButtonComponent>().Single().Interactable, Is.False);
        }

        [Test]
        public void Scan_ReportsWhetherAFieldAcceptsTyping()
        {
            Spawn("live field", typeof(InputField));
            var locked = Spawn("locked field", typeof(InputField));
            locked.GetComponent<InputField>().interactable = false;
            Spawn("live tmp field", typeof(TMP_InputField));
            var lockedTmp = Spawn("locked tmp field", typeof(TMP_InputField));
            lockedTmp.GetComponent<TMP_InputField>().interactable = false;

            var scene = new SceneScanner().Scan().Scene;

            Assert.That(EditTextOf(scene, "live field").Interactable, Is.True);
            Assert.That(EditTextOf(scene, "locked field").Interactable, Is.False);
            Assert.That(EditTextOf(scene, "live tmp field").Interactable, Is.True);
            Assert.That(EditTextOf(scene, "locked tmp field").Interactable, Is.False);
        }

        [Test]
        public void Click_RefusesAButtonLockedAfterTheScan()
        {
            // The executor checks interactability before it moves the cursor, and that move spans
            // frames. This is the guard that catches a game locking the button inside that window.
            var target = Spawn("button locked after the scan", typeof(Button));
            var button = target.GetComponent<Button>();
            var clicked = false;
            button.onClick.AddListener(() => clicked = true);
            var scanner = new SceneScanner();
            scanner.Scan();
            Assert.That(scanner.TryGetTarget(target.GetInstanceID(), out var scanned), Is.True);

            button.interactable = false;

            Assert.That(scanned.Click(), Is.False);
            Assert.That(clicked, Is.False);
        }

        [Test]
        public void Scan_CarriesInteractabilityThroughToTheSerializedScene()
        {
            // The scan reads it and the mapper ships it. Nothing else asserts that hop, and a
            // dropped assignment there would report every target as locked without failing anywhere.
            Spawn("live button", typeof(Button));
            var locked = Spawn("locked button", typeof(Button));
            locked.GetComponent<Button>().interactable = false;

            var scene = JObject.Parse(JsonConvert.SerializeObject(
                SceneSnapshotMapper.ToDto(new SceneScanner().Scan().Scene)));
            var blocks = scene["children"];

            Assert.That((bool)BlockNamed(blocks, "live button")["components"][0]["interactable"], Is.True);
            Assert.That((bool)BlockNamed(blocks, "locked button")["components"][0]["interactable"], Is.False);
        }

        private static JToken BlockNamed(JToken blocks, string name)
        {
            return blocks.Single(block => (string)block["name"] == name);
        }

        private GameObject Spawn(string name, params System.Type[] components)
        {
            var created = components.Length == 0
                ? new GameObject(name)
                : new GameObject(
                    name,
                    new[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) }
                        .Concat(components)
                        .ToArray());
            spawned.Add(created);
            return created;
        }

        private static ButtonComponent ButtonOf(SceneSnapshot scene, string name)
        {
            return ComponentsOf(scene, name).OfType<ButtonComponent>().Single();
        }

        private static EditTextComponent EditTextOf(SceneSnapshot scene, string name)
        {
            return ComponentsOf(scene, name).OfType<EditTextComponent>().Single();
        }

        private static IReadOnlyList<SceneComponent> ComponentsOf(SceneSnapshot scene, string name)
        {
            return scene.Children.Single(child => child.Name == name).Components;
        }
    }
}
