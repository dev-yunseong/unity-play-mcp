using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Artel.Domain;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using Artel.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    public sealed class BlockTransformTests
    {
        /// <summary>
        /// Rects are pixels now, and a projection through a camera lands a fraction of one away
        /// from the same rect drawn straight onto the screen.
        /// </summary>
        private const float Tolerance = 1f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in spawned)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator Scan_ReportsAnOverlayRectInScreenPixelsFromTheTopLeft()
        {
            var panel = QuarterPanel("overlay panel", OverlayCanvas());

            // The canvas sizes itself to the screen during the canvas update, not on the frame it
            // was created.
            yield return null;

            var rect = TransformOf(panel.name).ScreenRect;

            // Anchored to the bottom-left corner at half the width and a quarter of the height, so
            // its top edge sits three quarters of the way down a top-left origin.
            Assert.That(rect.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(rect.y, Is.EqualTo(Screen.height * 0.75f).Within(Tolerance));
            Assert.That(rect.width, Is.EqualTo(Screen.width * 0.5f).Within(Tolerance));
            Assert.That(rect.height, Is.EqualTo(Screen.height * 0.25f).Within(Tolerance));
        }

        [Test]
        public void Scan_ReportsTheScreenTheRectsWereMeasuredAgainst()
        {
            // A pixel rect says nothing on its own: x 860 is the middle of a 1920 screen and the
            // right third of a 1280 one.
            var scene = new SceneScanner().Scan().Scene;

            Assert.That(scene.Screen.x, Is.EqualTo(Screen.width));
            Assert.That(scene.Screen.y, Is.EqualTo(Screen.height));
            Assert.That(SceneSnapshotMapper.ToDto(scene).Screen.W, Is.EqualTo(Screen.width));
        }

        /// <summary>
        /// The whole point of reporting a screen rect: the same UI lands in the same place
        /// whichever way its canvas is drawn, even though its world position differs wildly
        /// between the two.
        /// </summary>
        [UnityTest]
        public IEnumerator Scan_ReportsTheSameRectForAnOverlayAndAScreenSpaceCameraCanvas()
        {
            var overlay = QuarterPanel("overlay panel", OverlayCanvas());
            var throughCamera = QuarterPanel("camera panel", ScreenSpaceCameraCanvas());

            yield return null;

            var overlayTransform = TransformOf(overlay.name);
            var cameraTransform = TransformOf(throughCamera.name);

            Assert.That(cameraTransform.ScreenRect.x, Is.EqualTo(overlayTransform.ScreenRect.x).Within(Tolerance));
            Assert.That(cameraTransform.ScreenRect.y, Is.EqualTo(overlayTransform.ScreenRect.y).Within(Tolerance));
            Assert.That(cameraTransform.ScreenRect.width, Is.EqualTo(overlayTransform.ScreenRect.width).Within(Tolerance));
            Assert.That(cameraTransform.ScreenRect.height, Is.EqualTo(overlayTransform.ScreenRect.height).Within(Tolerance));

            Assert.That(cameraTransform.OnScreen, Is.True);
            Assert.That(overlayTransform.OnScreen, Is.True);

            // The world halves are not interchangeable, which is why the screen halves have to be
            // reported rather than left for a reader to work out.
            Assert.That(cameraTransform.World, Is.Not.EqualTo(overlayTransform.World));
        }

        [UnityTest]
        public IEnumerator Scan_KeepsTheMeasuredRectOfAPanelPushedOffTheSideOfTheScreen()
        {
            var canvas = OverlayCanvas();
            var panel = QuarterPanel("offscreen panel", canvas);

            yield return null;

            // Two full screen widths to the left, so nothing of it overlaps the frame.
            panel.GetComponent<RectTransform>().anchoredPosition = new Vector2(-2f * Screen.width, 0f);

            yield return null;

            var transform = TransformOf(panel.name);

            Assert.That(transform.OnScreen, Is.False);

            // How far off it sits is information a reader can act on, so the numbers survive.
            Assert.That(transform.ScreenRect.x, Is.LessThan(-Screen.width));
            Assert.That(transform.ScreenRect.width, Is.EqualTo(Screen.width * 0.5f).Within(Tolerance));
        }

        [Test]
        public void Scan_ReportsAPlainObjectsOwnPositionAsItsWorld()
        {
            MainCamera();
            var target = Spawn("plain object");
            target.transform.position = new Vector3(1.5f, 2.5f, 12f);

            var transform = TransformOf(target.name);

            Assert.That(transform.World, Is.EqualTo(new Vector3(1.5f, 2.5f, 12f)));

            // A point has no extent, and reporting one would invent a click area that is not there.
            Assert.That(transform.ScreenRect.width, Is.EqualTo(0f));
            Assert.That(transform.ScreenRect.height, Is.EqualTo(0f));
        }

        /// <summary>
        /// Unity hands back a mirrored, plausible-looking screen point for anything behind the
        /// camera. Without the depth check a reader would aim at it.
        /// </summary>
        [Test]
        public void Scan_RefusesToReportAScreenRectForAnObjectBehindTheCamera()
        {
            var camera = MainCamera();
            var target = Spawn("object behind camera");
            target.transform.position = camera.transform.position - (camera.transform.forward * 10f);

            var transform = TransformOf(target.name);

            Assert.That(transform.OnScreen, Is.False);
            Assert.That(transform.ScreenRect, Is.EqualTo(new Rect(0f, 0f, 0f, 0f)));

            // The world half stays usable: something behind the camera still has a position, and
            // that is the whole reason both frames are reported.
            Assert.That(transform.World, Is.EqualTo(target.transform.position));
        }

        [Test]
        public void Map_RoundsCoordinatesSoAStillSceneHashesTheSame()
        {
            var tracker = new SceneStateHashTracker(new NewtonsoftJsonCodec());

            var settled = Snapshot(new Vector3(1.000001f, 2.000002f, 3f), new Rect(10f, 20f, 30f, 40f));
            var jittered = Snapshot(new Vector3(1.000004f, 2.000005f, 3f), new Rect(10.4f, 20.3f, 30.2f, 40.1f));

            Assert.That(tracker.Observe(SceneSnapshotMapper.ToDto(settled)), Is.False);

            // Sub-pixel drift is noise from a breathing animation or a layout pass, and resending
            // the whole scene for it would flood the socket.
            Assert.That(tracker.Observe(SceneSnapshotMapper.ToDto(jittered)), Is.False);

            // A whole pixel of movement is a real change and still gets through.
            Assert.That(
                tracker.Observe(SceneSnapshotMapper.ToDto(
                    Snapshot(new Vector3(1f, 2f, 3f), new Rect(12f, 20f, 30f, 40f)))),
                Is.True);
        }

        [Test]
        public void Map_ReportsScreenRectsAsWholePixels()
        {
            var rect = SceneSnapshotMapper
                .ToDto(Snapshot(Vector3.zero, new Rect(10.6f, 20.4f, 30.5f, 40f)))
                .Children.Single().Transform.Rect;

            Assert.That(rect.X, Is.EqualTo(11));
            Assert.That(rect.Y, Is.EqualTo(20));
            Assert.That(rect.H, Is.EqualTo(40));
        }

        [Test]
        public void Map_FlattensCoordinatesJsonCannotCarry()
        {
            var transform = SceneSnapshotMapper
                .ToDto(Snapshot(
                    new Vector3(float.NaN, float.PositiveInfinity, 1f),
                    new Rect(float.NaN, 0f, float.PositiveInfinity, 0f)))
                .Children.Single().Transform;

            // Newtonsoft writes NaN and Infinity as bare literals that a strict parser rejects, and
            // one bad object would cost the whole payload.
            Assert.That(transform.World.X, Is.EqualTo(0f));
            Assert.That(transform.World.Y, Is.EqualTo(0f));
            Assert.That(transform.World.Z, Is.EqualTo(1f));
            Assert.That(transform.Rect.X, Is.EqualTo(0));
            Assert.That(transform.Rect.W, Is.EqualTo(0));
        }

        private static SceneSnapshot Snapshot(Vector3 world, Rect screenRect)
        {
            return new SceneSnapshot(
                1,
                "scene",
                new Vector2Int(1920, 1080),
                new List<SceneBlock>
                {
                    new SceneBlock(
                        2,
                        "block",
                        true,
                        new BlockTransform(world, screenRect, true),
                        new List<SceneComponent>(),
                        new List<SceneBlock>())
                });
        }

        private static BlockTransform TransformOf(string name)
        {
            return Find(new SceneScanner().Scan().Scene.Children, name).Transform;
        }

        private static SceneBlock Find(IReadOnlyList<SceneBlock> blocks, string name)
        {
            foreach (var block in blocks)
            {
                if (block.Name == name)
                {
                    return block;
                }

                var match = Find(block.Children, name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private Canvas OverlayCanvas()
        {
            var canvas = Spawn("overlay canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        private Canvas ScreenSpaceCameraCanvas()
        {
            var canvas = Spawn("camera canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = MainCamera();
            canvas.planeDistance = 50f;
            return canvas;
        }

        private Camera MainCamera()
        {
            var existing = Camera.main;
            if (existing != null)
            {
                return existing;
            }

            var camera = Spawn("main camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 0f, -10f);
            return camera;
        }

        /// <summary>
        /// A panel pinned to the bottom-left corner covering half the screen's width and a quarter
        /// of its height, so the expected fractions are readable rather than derived.
        /// </summary>
        private GameObject QuarterPanel(string name, Canvas canvas)
        {
            var panel = Spawn(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.SetParent(canvas.transform, false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(Screen.width * 0.5f, Screen.height * 0.25f);
            return panel;
        }

        private GameObject Spawn(string name, params System.Type[] components)
        {
            var created = components.Length == 0
                ? new GameObject(name)
                : new GameObject(name, components);
            spawned.Add(created);
            return created;
        }
    }
}
