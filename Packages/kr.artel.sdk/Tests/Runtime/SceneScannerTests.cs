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
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    public sealed class SceneScannerTests
    {
        /// <summary>이름 없는 씬을 Build Settings에 올리려고 한 번 저장할 자리.</summary>
        private const string TemporaryScenePath = "Assets/ArtelSceneScanReportTemp.unity";

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
        public void Scan_ReportsAnImageWithItsSpriteName()
        {
            gameObject.AddComponent<CanvasRenderer>();
            var image = gameObject.AddComponent<Image>();
            image.sprite = CreateSprite("hp_bar_fill");

            var component = ComponentsOf(gameObject).OfType<VisualComponent>().Single();

            Assert.That(component.Kind, Is.EqualTo(VisualKind.Image));
            Assert.That(component.SpriteName, Is.EqualTo("hp_bar_fill"));
        }

        [Test]
        public void Scan_ReportsAFlatColourImageWithNoSprite()
        {
            // A panel or an invisible raycast catcher has no sprite, and is still both on screen and
            // in the way of anything the pointer aims at behind it.
            gameObject.AddComponent<CanvasRenderer>();
            gameObject.AddComponent<Image>();

            var component = ComponentsOf(gameObject).OfType<VisualComponent>().Single();

            Assert.That(component.Kind, Is.EqualTo(VisualKind.Image));
            Assert.That(component.SpriteName, Is.Null);
        }

        [Test]
        public void Scan_ReportsASpriteRendererAsItsOwnKind()
        {
            gameObject.AddComponent<SpriteRenderer>().sprite = CreateSprite("goblin_idle");

            var component = ComponentsOf(gameObject).OfType<VisualComponent>().Single();

            Assert.That(component.Kind, Is.EqualTo(VisualKind.Sprite));
            Assert.That(component.SpriteName, Is.EqualTo("goblin_idle"));
        }

        [Test]
        public void Scan_GivesASpriteTheAreaItCoversRatherThanAPoint()
        {
            // A SpriteRenderer is not a RectTransform, and the point a plain Transform reports has
            // no extent — nothing could be aimed at it.
            var cameraObject = new GameObject("main camera", typeof(Camera)) { tag = "MainCamera" };
            // Back from the origin, or the sprite sits on the near plane and projects as unusable.
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            spawned.Add(cameraObject);
            gameObject.AddComponent<SpriteRenderer>().sprite = CreateSprite("goblin_idle");

            var block = new SceneScanner().Scan().Scene.Children
                .Single(child => child.Name == gameObject.name);

            Assert.That(block.Transform.OnScreen, Is.True);
            Assert.That(block.Transform.ScreenRect.width, Is.GreaterThan(0f));
            Assert.That(block.Transform.ScreenRect.height, Is.GreaterThan(0f));
        }

        private static Sprite CreateSprite(string name)
        {
            var texture = new Texture2D(32, 32);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        private static IReadOnlyList<SceneComponent> ComponentsOf(GameObject target)
        {
            return new SceneScanner().Scan().Scene.Children
                .Single(child => child.Name == target.name)
                .Components;
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

        /// <summary>
        /// 보고서가 Build Settings의 씬을 그대로 싣고, 그 씬을 실제로 스캔하는지 본다.
        /// </summary>
        /// <remarks>
        /// 전제 조건을 테스트가 직접 만든다. 호스트 프로젝트의 Build Settings를 빌려 쓰면 씬을
        /// 하나도 등록하지 않은 프로젝트 — 패키지 테스트를 돌리는 빈 프로젝트가 늘 그렇다 —
        /// 에서는 빈 보고서를 받고 실패한다.
        ///
        /// 이미 열려 있는 씬을 등록하는 이유는 EditMode에서 <c>SceneManager.LoadSceneAsync</c>가
        /// 돌지 않기 때문이다. 열려 있는 씬은 <see cref="AllSceneScanner"/>가 그 자리에서 스캔한다.
        /// </remarks>
        [UnityTest]
        public IEnumerator CreateReport_ListsBuildScenesAndScansThem()
        {
            var originalBuildScenes = EditorBuildSettings.scenes;
            var activeScene = SceneManager.GetActiveScene();
            var scenePath = activeScene.path;
            var savedTemporaryScene = false;
            try
            {
                // 저장된 적 없는 씬은 경로가 없어 Build Settings에 올릴 수 없다. 임시 파일로 한
                // 번 저장해 경로를 준다 — 오브젝트는 그대로 살아 있고, 이미 경로가 있는 씬은
                // 건드리지 않는다.
                if (string.IsNullOrEmpty(scenePath))
                {
                    scenePath = TemporaryScenePath;
                    Assert.That(
                        EditorSceneManager.SaveScene(activeScene, scenePath),
                        Is.True,
                        "테스트가 쓸 임시 씬을 저장하지 못했습니다.");
                    savedTemporaryScene = true;
                }

                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

                SceneScanReportDto report = null;
                yield return SceneScanReporter.CreateReport(result => report = result);

                Assert.That(report, Is.Not.Null);
                Assert.That(report.ScenesInBuild, Is.EqualTo(new[] { scenePath }));
                Assert.That(report.ScannedScenes, Is.Not.Empty);
                Assert.That(
                    report.ScannedScenes.Any(scene =>
                        scene.Children.Any(child => child.Name == gameObject.name)),
                    Is.True);
            }
            finally
            {
                EditorBuildSettings.scenes = originalBuildScenes;
                if (savedTemporaryScene)
                {
                    AssetDatabase.DeleteAsset(TemporaryScenePath);
                }
            }
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
        public void Scan_Full_ReportsOnClickTargetTypeAndMethod()
        {
            var button = gameObject.AddComponent<Button>();
            var listener = gameObject.AddComponent<TrackedFixtureBehaviour>();
            UnityEventTools.AddPersistentListener(button.onClick, listener.Ping);

            var full = new SceneScanner().Scan(SceneScanOptions.Full).Scene;
            var handler = ButtonOf(full, gameObject.name).ClickHandlers.Single();

            Assert.That(handler.Target, Is.EqualTo(gameObject.name));
            Assert.That(handler.TargetType, Is.EqualTo(typeof(TrackedFixtureBehaviour).FullName));
            Assert.That(handler.Method, Is.EqualTo(nameof(TrackedFixtureBehaviour.Ping)));

            // The poller rescans constantly, so the default scan keeps paying nothing for this.
            var byDefault = new SceneScanner().Scan().Scene;
            Assert.That(ButtonOf(byDefault, gameObject.name).ClickHandlers, Is.Empty);
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
