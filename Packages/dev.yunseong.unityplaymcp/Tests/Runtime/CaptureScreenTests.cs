using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Capture;
using UnityPlayMcp.Protocol.Dto;
using UnityPlayMcp.Serialization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// The decisions in `capture_screen`, exercised without a framebuffer.
    /// </summary>
    /// <remarks>
    /// The pixel path itself needs a real screen and is verified by hand in play mode. What is
    /// covered here is everything that decides *what* to capture and *what to say* about it — the
    /// parts that fail silently, by producing a plausible image of the wrong area or a result the
    /// agent cannot act on.
    /// </remarks>
    public sealed class CaptureScreenTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in spawned)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            spawned.Clear();
        }

        // --- params ---

        [Test]
        public void ReadParams_TreatsNoParamsAsTheWholeScreen()
        {
            Assert.That(
                CaptureRequestReader.TryRead(new List<object>(), out var request, out _),
                Is.True);

            Assert.That(request.IsFullScreen, Is.True);
            Assert.That(request.MaxEdge, Is.EqualTo(CaptureRequestReader.FullScreenMaxEdge));

            // A whole screen is mostly rendered scene, where JPEG's cost is invisible and its
            // saving is not.
            Assert.That(request.ContentType, Is.EqualTo("image/jpeg"));
        }

        [Test]
        public void ReadParams_TreatsATargetIdAsACrop()
        {
            Assert.That(
                CaptureRequestReader.TryRead(new List<object> { 42L }, out var request, out _),
                Is.True);

            Assert.That(request.TargetId, Is.EqualTo(42));
            Assert.That(request.MaxEdge, Is.EqualTo(CaptureRequestReader.CropMaxEdge));

            // A crop is usually UI, where JPEG ringing lands on the glyph edges and borders that
            // are the thing being judged.
            Assert.That(request.ContentType, Is.EqualTo("image/png"));
        }

        [Test]
        public void ReadParams_LetsOptionsOverrideTheDefaults()
        {
            var options = new Dictionary<string, object> { { "maxEdge", 256L }, { "padding", 4L } };

            Assert.That(
                CaptureRequestReader.TryRead(
                    new List<object> { 42L, options },
                    out var request,
                    out _),
                Is.True);

            Assert.That(request.MaxEdge, Is.EqualTo(256));
            Assert.That(request.Padding, Is.EqualTo(4f));
        }

        [Test]
        public void ReadParams_RefusesAMaxEdgeThatWouldProduceNoImage()
        {
            var options = new Dictionary<string, object> { { "maxEdge", 0L } };

            Assert.That(
                CaptureRequestReader.TryRead(new List<object> { 42L, options }, out _, out var error),
                Is.False);
            Assert.That(error, Does.Contain("maxEdge"));
        }

        [Test]
        public void ReadParams_RefusesATargetIdItCannotRead()
        {
            Assert.That(
                CaptureRequestReader.TryRead(new List<object> { "not an id" }, out _, out var error),
                Is.False);
            Assert.That(error, Does.Contain("capture_screen params"));
        }

        // --- rectangle ---

        [UnityTest]
        public IEnumerator ResolveRect_ProjectsAnOverlayElementOntoItsScreenPixels()
        {
            var screen = new Rect(0f, 0f, 800f, 600f);
            var panel = Panel("panel", OverlayCanvas(), new Vector2(200f, 100f), new Vector2(80f, 40f));

            // An overlay canvas sizes itself to the screen during the canvas update, not on the
            // frame it was created, and its children's world corners are meaningless until then.
            yield return null;

            Assert.That(CaptureRect.TryResolve(panel, 0f, screen, out var region), Is.True);

            Assert.That(region.PixelRect.xMin, Is.EqualTo(200f).Within(0.5f));
            Assert.That(region.PixelRect.yMin, Is.EqualTo(100f).Within(0.5f));
            Assert.That(region.PixelRect.width, Is.EqualTo(80f).Within(0.5f));
            Assert.That(region.PixelRect.height, Is.EqualTo(40f).Within(0.5f));
            Assert.That(region.Clipped, Is.False);
        }

        [UnityTest]
        public IEnumerator ResolveRect_GrowsTheRectangleByThePadding()
        {
            var screen = new Rect(0f, 0f, 800f, 600f);
            var panel = Panel("panel", OverlayCanvas(), new Vector2(200f, 100f), new Vector2(80f, 40f));

            yield return null;

            Assert.That(CaptureRect.TryResolve(panel, 10f, screen, out var region), Is.True);

            Assert.That(region.PixelRect.xMin, Is.EqualTo(190f).Within(0.5f));
            Assert.That(region.PixelRect.width, Is.EqualTo(100f).Within(0.5f));
        }

        /// <summary>
        /// A half-visible element is itself the kind of defect the agent is looking for, so the
        /// visible part is captured and the fact reported rather than treated as an error.
        /// </summary>
        [UnityTest]
        public IEnumerator ResolveRect_ReportsAnElementTheScreenCutsShortAsClipped()
        {
            var screen = new Rect(0f, 0f, 800f, 600f);
            var panel = Panel("panel", OverlayCanvas(), new Vector2(-20f, 100f), new Vector2(80f, 40f));

            yield return null;

            Assert.That(CaptureRect.TryResolve(panel, 0f, screen, out var region), Is.True);

            Assert.That(region.Clipped, Is.True);
            Assert.That(region.PixelRect.xMin, Is.EqualTo(0f).Within(0.5f));
            Assert.That(region.PixelRect.width, Is.EqualTo(60f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator ResolveRect_RefusesAnElementWithNoPixelsOnScreen()
        {
            var screen = new Rect(0f, 0f, 800f, 600f);
            var panel = Panel("panel", OverlayCanvas(), new Vector2(-500f, 100f), new Vector2(80f, 40f));

            yield return null;

            Assert.That(CaptureRect.TryResolve(panel, 0f, screen, out _), Is.False);
        }

        // --- downscale ---

        [Test]
        public void Downscale_CapsTheLongestEdgeAndKeepsTheShape()
        {
            var size = CaptureRect.Downscale(1920, 1080, 1024);

            Assert.That(size.x, Is.EqualTo(1024));
            Assert.That(size.y, Is.EqualTo(576));
        }

        [Test]
        public void Downscale_LeavesAnImageAlreadyUnderTheCapAlone()
        {
            // Upscaling a small button to the cap costs bytes and adds no detail for the model.
            var size = CaptureRect.Downscale(200, 80, 512);

            Assert.That(size.x, Is.EqualTo(200));
            Assert.That(size.y, Is.EqualTo(80));
        }

        [Test]
        public void Downscale_NeverProducesAZeroSidedImage()
        {
            var size = CaptureRect.Downscale(2000, 3, 512);

            Assert.That(size.x, Is.EqualTo(512));
            Assert.That(size.y, Is.EqualTo(1));
        }

        // --- executor ---

        [Test]
        public void CaptureScreen_ReturnsTheImageBytesInline()
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var executor = ExecutorWith(
                new FakeScreenCapturer(new CapturedImage { Bytes = bytes, Width = 1024, Height = 576 }));

            var result = Run(executor, new List<object>());

            Assert.That(result.IsSuccess, Is.True);
            var returned = (CaptureResultDto)result.ReturnValue;
            Assert.That(returned.Data, Is.EqualTo(Convert.ToBase64String(bytes)));
            Assert.That(returned.MimeType, Is.EqualTo("image/jpeg"));
            Assert.That(returned.Width, Is.EqualTo(1024));
            Assert.That(returned.TargetId, Is.Null);
            Assert.That(returned.Clipped, Is.False);
        }

        [Test]
        public void CaptureScreen_RefusesATargetIdTheSceneDoesNotHave()
        {
            var executor = ExecutorWith(new FakeScreenCapturer());

            var result = Run(executor, new List<object> { 999999L });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("Unknown target id"));
        }

        [Test]
        public void CaptureScreen_ReportsWhyTheScreenCouldNotBeRead()
        {
            var executor = ExecutorWith(
                new FakeScreenCapturer(CapturedImage.Failed("The game runs in batchmode and has no screen to capture.")));

            var result = Run(executor, new List<object>());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("batchmode"));
        }

        // --- wire shape ---

        /// <summary>
        /// The whole point of `NullValueHandling.Ignore`: results that return nothing keep the
        /// exact shape the relay and the agent already parse.
        /// </summary>
        [Test]
        public void Serialize_LeavesAResultWithNothingToReturnUnchanged()
        {
            var json = new NewtonsoftJsonCodec().Serialize(ActionResultDto.Success(3));

            Assert.That(json, Does.Not.Contain("returnValue"));
        }

        [Test]
        public void Serialize_CarriesTheCaptureReturnValue()
        {
            var json = new NewtonsoftJsonCodec().Serialize(ActionResultDto.Success(3, new CaptureResultDto
            {
                MimeType = "image/png",
                Width = 120,
                Height = 40,
                TargetId = 7,
                Clipped = true,
                Data = "AQIDBA=="
            }));

            Assert.That(json, Does.Contain("\"returnValue\""));
            Assert.That(json, Does.Contain("\"targetId\":7"));
            Assert.That(json, Does.Contain("\"clipped\":true"));
            Assert.That(json, Does.Contain("\"data\":\"AQIDBA==\""));
        }

        // --- helpers ---

        private static ActionExecutor ExecutorWith(IScreenCapturer capturer)
        {
            return new ActionExecutor(new TargetLookup(), null, new PointerEventDispatcher(), capturer);
        }

        private static ActionResultDto Run(ActionExecutor executor, List<object> parameters)
        {
            ActionResultDto result = null;
            Drain(executor.Execute(7, "capture_screen", parameters, value => result = value));
            return result;
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }

        private Canvas OverlayCanvas()
        {
            var canvas = Spawn("overlay canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        /// <summary>
        /// A panel at an exact pixel offset from the bottom-left corner, so the expected rectangle
        /// is readable rather than derived from the screen size the test happens to run at.
        /// </summary>
        private RectTransform Panel(string name, Canvas canvas, Vector2 position, Vector2 size)
        {
            var panel = Spawn(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.SetParent(canvas.transform, false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = Vector2.zero;
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = position;
            return rectTransform;
        }

        private GameObject Spawn(string name, params Type[] components)
        {
            var gameObject = new GameObject(name, components);
            spawned.Add(gameObject);
            return gameObject;
        }

        private sealed class FakeScreenCapturer : IScreenCapturer
        {
            private readonly CapturedImage image;

            public FakeScreenCapturer(CapturedImage image = default)
            {
                this.image = image;
            }

            public IEnumerator Capture(
                CaptureRequest request,
                Rect? pixelRect,
                Action<CapturedImage> completed)
            {
                completed(image);
                yield break;
            }
        }

    }
}
