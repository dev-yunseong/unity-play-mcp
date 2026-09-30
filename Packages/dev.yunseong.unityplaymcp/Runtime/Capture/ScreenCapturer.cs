using System;
using System.Collections;
using UnityPlayMcp.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp.Capture
{
    /// <summary>
    /// Captures the composited screen, including Screen Space Overlay UI.
    /// </summary>
    /// <remarks>
    /// Reads the back buffer like <see cref="Streaming.ScreenVideoSource"/>, because a camera
    /// render omits overlay UI. The virtual cursor and keyboard overlay appear on purpose so the
    /// agent can see its own pointer.
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
                // No framebuffer to read. Failing here avoids a black image that looks like a rendering bug.
                completed(CapturedImage.Failed(
                    "The game runs in batchmode and has no screen to capture."));
                yield break;
            }

            // The back buffer is complete, overlay UI included, only at end of frame.
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
                // 다른 end-of-frame 소비자가 남긴 target 으로 back buffer grab 이 향하지 않게 한다.
                // 호출자의 target 은 finally 에서 되돌린다.
                RenderTexture.active = null;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);

                scaled = RenderTexture.GetTemporary(
                    size.x, size.y, 0, RenderTextureFormat.BGRA32);

                // The screenshot is in framebuffer orientation, upside down under D3D. Getting the
                // flip wrong yields an inverted image that only on-screen text reveals.
                //
                // Crop and flip share one blit that samples `uv * scale + offset`. The rect is
                // bottom-left origin, so output row v reads screen row `yMin + v * height`; the
                // buffer holds it upside down, hence the negative y and the `1 -`.
                var scale = new Vector2(source.width / screenWidth, -source.height / screenHeight);
                var offset = new Vector2(
                    source.xMin / screenWidth,
                    1f - source.yMin / screenHeight);
                Graphics.Blit(screen, scaled, scale, offset);

                // Only synchronous work is measured; spanning the end-of-frame wait would count idle time.
                using (ProfilerMarkers.CaptureReadback.Auto())
                {
                    readback = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                    RenderTexture.active = scaled;
                    readback.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0, false);
                    readback.Apply(false);
                }

                byte[] bytes;
                using (ProfilerMarkers.CaptureEncode.Auto())
                {
                    bytes = request.UsePng
                        ? readback.EncodeToPNG()
                        : readback.EncodeToJPG(CaptureRequestReader.JpegQuality);
                }

                if (bytes == null || bytes.Length == 0)
                {
                    completed(CapturedImage.Failed("The captured image could not be encoded."));
                    yield break;
                }

                // 좌표 변환 값은 여기서 잰다. 이미지 크기만으로는 maxEdge 축소 비율과 crop 원점을
                // 알 수 없다 (#71). 프레임과 씬도 back buffer 를 읽은 순간의 값이다.
                var active = SceneManager.GetActiveScene();
                completed(new CapturedImage
                {
                    Bytes = bytes,
                    Width = size.x,
                    Height = size.y,
                    // blit 이 쓴 값이다. 창이 최소화돼 Screen 이 0 이어도 region 이 screen 안에 들게 한다.
                    ScreenWidth = screenWidth,
                    ScreenHeight = screenHeight,
                    Source = source,
                    Frame = Time.frameCount,
                    Scene = active.IsValid() ? active.name : null
                });
            }
            finally
            {
                // Release even on failure; a leak here eventually kills a long run.
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
