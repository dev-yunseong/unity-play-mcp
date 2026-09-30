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
    /// `capture_screen` decisions, tested without a framebuffer.
    /// </summary>
    /// <remarks>
    /// The pixel path needs a real screen and is checked by hand. These cover what to capture and
    /// what to report, which fail silently with a plausible image of the wrong area.
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

            // A full screen is mostly rendered scene, where JPEG artifacts are invisible.
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

            // A crop is usually UI, where JPEG ringing lands on glyph edges and borders.
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

            // An overlay canvas sizes itself during the canvas update, not on the frame it was created.
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
        /// A half-visible element is itself a defect the agent looks for, so the visible part is
        /// captured and reported rather than treated as an error.
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

            // 잘리기 전 요청 영역도 남겨야 무엇이 잘렸는지 알 수 있다.
            Assert.That(region.Requested.xMin, Is.EqualTo(-20f).Within(0.5f));
            Assert.That(region.Requested.width, Is.EqualTo(80f).Within(0.5f));
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
            // Upscaling a small button costs bytes and adds no detail.
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

        // --- coordinates (#71) ---

        /// <summary>
        /// 1920x1080 화면을 1024x576 으로 줄인 스크린샷의 픽셀을 <c>move_mouse</c> 좌표로 되돌린다.
        /// </summary>
        [Test]
        public void Geometry_MapsADownscaledFullScreenBackToScreenPixels()
        {
            var source = new Rect(0f, 0f, 1920f, 1080f);
            var size = CaptureRect.Downscale(1920, 1080, CaptureRequestReader.FullScreenMaxEdge);

            var region = CaptureRect.TopLeft(source, 1080);
            var scale = CaptureRect.Scale(source, size.x, size.y);

            Assert.That(region.X, Is.EqualTo(0f));
            Assert.That(region.Y, Is.EqualTo(0f));
            Assert.That(region.Width, Is.EqualTo(1920f));
            Assert.That(region.Height, Is.EqualTo(1080f));
            Assert.That(scale.X, Is.EqualTo(1024f / 1920f).Within(1e-5f));
            Assert.That(scale.Y, Is.EqualTo(576f / 1080f).Within(1e-5f));

            // 이미지 (512, 288) 은 화면 중앙 (960, 540) 이다 (배율 1.875).
            Assert.That(region.X + 512f / scale.X, Is.EqualTo(960f).Within(0.01f));
            Assert.That(region.Y + 288f / scale.Y, Is.EqualTo(540f).Within(0.01f));
        }

        /// <summary>같은 이미지 크기라도 해상도가 다르면 비율이 달라진다.</summary>
        [Test]
        public void Geometry_FollowsAResolutionChange()
        {
            var source = new Rect(0f, 0f, 1280f, 720f);
            var size = CaptureRect.Downscale(1280, 720, CaptureRequestReader.FullScreenMaxEdge);

            Assert.That(size.x, Is.EqualTo(1024));
            Assert.That(size.y, Is.EqualTo(576));
            var scale = CaptureRect.Scale(source, size.x, size.y);
            Assert.That(scale.X, Is.EqualTo(0.8f).Within(1e-5f));

            // 같은 이미지 점 (512, 288) 이 이번에는 (640, 360) 이다.
            Assert.That(512f / scale.X, Is.EqualTo(640f).Within(0.01f));
            Assert.That(288f / scale.Y, Is.EqualTo(360f).Within(0.01f));
        }

        /// <summary>대상 crop 의 원점은 좌하단 Unity 좌표를 좌상단 기준으로 뒤집은 값이다.</summary>
        [Test]
        public void Geometry_PlacesACropAtItsTopLeftOrigin()
        {
            // 1920x1080 화면에서 아래 200, 왼쪽 100 에 있는 300x100 영역이다.
            var source = new Rect(100f, 200f, 300f, 100f);

            var region = CaptureRect.TopLeft(source, 1080);
            var scale = CaptureRect.Scale(source, 300, 100);

            Assert.That(region.X, Is.EqualTo(100f));
            Assert.That(region.Y, Is.EqualTo(780f));
            Assert.That(scale.X, Is.EqualTo(1f));

            // 이미지 좌상단 픽셀은 화면 (100, 780), 우하단은 (400, 880) 이다.
            Assert.That(region.X + 300f / scale.X, Is.EqualTo(400f));
            Assert.That(region.Y + 100f / scale.Y, Is.EqualTo(880f));
        }

        // --- executor ---

        [Test]
        public void CaptureScreen_ReportsWhereTheImageSitsOnTheScreenAndWhen()
        {
            var executor = ExecutorWith(new FakeScreenCapturer(new CapturedImage
            {
                Bytes = new byte[] { 1, 2, 3, 4 },
                Width = 1024,
                Height = 576,
                ScreenWidth = 1920,
                ScreenHeight = 1080,
                Source = new Rect(0f, 0f, 1920f, 1080f),
                Frame = 4321,
                Scene = "GameScene"
            }));

            var result = Run(executor, new List<object>());

            Assert.That(result.IsSuccess, Is.True, result.Error);
            var returned = (CaptureResultDto)result.ReturnValue;
            Assert.That(returned.Screen.Width, Is.EqualTo(1920));
            Assert.That(returned.Screen.Height, Is.EqualTo(1080));
            Assert.That(returned.Region.Width, Is.EqualTo(1920f));
            Assert.That(returned.Scale.X, Is.EqualTo(1024f / 1920f).Within(1e-5f));
            Assert.That(returned.Frame, Is.EqualTo(4321));
            Assert.That(returned.Scene, Is.EqualTo("GameScene"));
            Assert.That(returned.RequestedRegion, Is.Null, "a full screen is never clipped");
        }

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
        /// With `NullValueHandling.Ignore`, results that return nothing keep the shape the relay and
        /// agent already parse.
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

        [Test]
        public void Serialize_CarriesTheCoordinateMetadata()
        {
            var json = new NewtonsoftJsonCodec().Serialize(ActionResultDto.Success(3, new CaptureResultDto
            {
                MimeType = "image/jpeg",
                Width = 1024,
                Height = 576,
                Clipped = false,
                Screen = new CaptureScreenSizeDto { Width = 1920, Height = 1080 },
                Region = new CaptureAreaDto { X = 0f, Y = 0f, Width = 1920f, Height = 1080f },
                Scale = new CaptureScaleDto { X = 0.5f, Y = 0.5f },
                Frame = 12,
                Scene = "Lobby",
                Data = "AQIDBA=="
            }));

            Assert.That(json, Does.Contain("\"screen\":{\"width\":1920,\"height\":1080}"));
            Assert.That(json, Does.Contain("\"region\":{"));
            Assert.That(json, Does.Contain("\"scale\":{\"x\":0.5,\"y\":0.5}"));
            Assert.That(json, Does.Contain("\"frame\":12"));
            Assert.That(json, Does.Contain("\"scene\":\"Lobby\""));
            Assert.That(json, Does.Not.Contain("requestedRegion"));
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
        /// A panel at an exact pixel offset from the bottom-left, so the expected rectangle does not
        /// depend on the test's screen size.
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
