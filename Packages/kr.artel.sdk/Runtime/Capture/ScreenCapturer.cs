using System;
using System.Collections;
using UnityEngine;

namespace Artel.Capture
{
    /// <summary>
    /// Captures the composited screen, including Screen Space Overlay UI.
    /// </summary>
    /// <remarks>
    /// Reads the back buffer the same way <see cref="Streaming.ScreenVideoSource"/> does, for the
    /// same reason: a camera render omits overlay UI, which is most of what a QA agent needs to
    /// judge. The virtual cursor and keyboard overlay are part of that composite and appear in
    /// captures on purpose — the agent seeing where its own pointer is beats a clean image.
    /// </remarks>
    internal sealed class ScreenCapturer : IScreenCapturer
    {
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

        public IEnumerator Capture(
            CaptureRequest request,
            Rect? pixelRect,
            Action<CapturedImage> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            if (Application.isBatchMode)
            {
                // There is no framebuffer to read. Said plainly here because the alternative is a
                // black image that reads as a rendering bug.
                completed(CapturedImage.Failed(
                    "The game runs in batchmode and has no screen to capture."));
                yield break;
            }

            // The screenshot reads the back buffer, so it is only correct once everything for this
            // frame has been drawn — including the overlay UI this path exists for.
            yield return endOfFrame;

            var screenWidth = Mathf.Max(2, Screen.width);
            var screenHeight = Mathf.Max(2, Screen.height);
            var source = pixelRect ?? new Rect(0f, 0f, screenWidth, screenHeight);
            var sourceWidth = Mathf.Max(1, Mathf.RoundToInt(source.width));
            var sourceHeight = Mathf.Max(1, Mathf.RoundToInt(source.height));
            var size = CaptureRect.Downscale(sourceWidth, sourceHeight, request.MaxEdge);

            RenderTexture screen = null;
            RenderTexture scaled = null;
            Texture2D readback = null;
            var previous = RenderTexture.active;
            try
            {
                screen = RenderTexture.GetTemporary(
                    screenWidth, screenHeight, 0, RenderTextureFormat.BGRA32);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);

                scaled = RenderTexture.GetTemporary(
                    size.x, size.y, 0, RenderTextureFormat.BGRA32);

                // The screenshot is written in framebuffer orientation, which under D3D is upside
                // down relative to what an encoder expects. The flip is not optional: getting it
                // wrong produces a working-but-inverted image, which passes a smoke test and is
                // only caught against on-screen text.
                //
                // Crop and flip ride in the same blit: the blit samples `uv * scale + offset`.
                // The rectangle is in Unity screen coordinates, whose origin is bottom-left, so
                // output row v must read screen row `yMin + v * height`; the buffer holds that
                // upside down, which is where the negative y and the `1 -` come from.
                var scale = new Vector2(source.width / screenWidth, -source.height / screenHeight);
                var offset = new Vector2(
                    source.xMin / screenWidth,
                    1f - source.yMin / screenHeight);
                Graphics.Blit(screen, scaled, scale, offset);

                readback = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                RenderTexture.active = scaled;
                readback.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0, false);
                readback.Apply(false);

                var bytes = request.UsePng
                    ? readback.EncodeToPNG()
                    : readback.EncodeToJPG(CaptureRequestReader.JpegQuality);
                if (bytes == null || bytes.Length == 0)
                {
                    completed(CapturedImage.Failed("The captured image could not be encoded."));
                    yield break;
                }

                completed(new CapturedImage
                {
                    Bytes = bytes,
                    Width = size.x,
                    Height = size.y
                });
            }
            finally
            {
                // Every capture allocates three native objects. Leaking any of them kills a long
                // run rather than the capture that caused it, so release runs even on failure.
                RenderTexture.active = previous;
                if (screen != null)
                {
                    RenderTexture.ReleaseTemporary(screen);
                }

                if (scaled != null)
                {
                    RenderTexture.ReleaseTemporary(scaled);
                }

                if (readback != null)
                {
                    UnityEngine.Object.Destroy(readback);
                }
            }
        }
    }
}
