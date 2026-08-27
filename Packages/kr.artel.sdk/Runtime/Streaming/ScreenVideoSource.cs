using System;
using System.Collections;
using Artel.Diagnostics;
using UnityEngine;

namespace Artel.Streaming
{
    internal interface ICaptureLoopRunner
    {
        void Begin(IEnumerator loop);
        void End();
    }

    internal sealed class BehaviourCaptureLoopRunner : ICaptureLoopRunner
    {
        private readonly MonoBehaviour behaviour;
        private Coroutine loop;

        public BehaviourCaptureLoopRunner(MonoBehaviour behaviour)
        {
            this.behaviour = behaviour != null
                ? behaviour
                : throw new ArgumentNullException(nameof(behaviour));
        }

        public void Begin(IEnumerator routine)
        {
            End();
            loop = behaviour.StartCoroutine(routine);
        }

        public void End()
        {
            if (loop == null)
            {
                return;
            }

            behaviour.StopCoroutine(loop);
            loop = null;
        }
    }

    /// <summary>
    /// Captures the composited screen into a single RenderTexture that backs a video track.
    ///
    /// This exists instead of Camera.CaptureStreamTrack because a camera render does not contain
    /// Screen Space Overlay UI, which is most of what a QA viewer needs to see.
    /// </summary>
    internal sealed class ScreenVideoSource
    {
        private readonly ICaptureLoopRunner captureLoop;
        private readonly int maxWidth;
        private readonly float captureIntervalSeconds;
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        private RenderTexture frame;
        private float nextCaptureTime;

        /// <param name="maxWidth">Zero or less means the screen's own width.</param>
        /// <param name="maxFramerate">Zero or less means every frame.</param>
        public ScreenVideoSource(ICaptureLoopRunner captureLoop, int maxWidth, int maxFramerate)
        {
            this.captureLoop = captureLoop ?? throw new ArgumentNullException(nameof(captureLoop));
            this.maxWidth = maxWidth;
            captureIntervalSeconds = maxFramerate > 0 ? 1f / maxFramerate : 0f;
        }

        public RenderTexture Frame { get { return frame; } }

        public bool IsCapturing { get { return frame != null; } }

        /// <summary>
        /// Allocates the capture target and starts the end-of-frame loop. Returns the texture the
        /// video track is to be built on.
        /// </summary>
        public RenderTexture Start()
        {
            if (frame != null)
            {
                return frame;
            }

            ResolveSize(out var width, out var height);

            // BGRA32 is the layout com.unity.webrtc reads a RenderTexture-backed track in. The
            // depth buffer is deliberately zero: nothing renders into this, it is only blitted to.
            frame = new RenderTexture(width, height, 0, RenderTextureFormat.BGRA32)
            {
                name = "ArtelScreenVideoSource"
            };
            frame.Create();

            nextCaptureTime = 0f;
            captureLoop.Begin(CaptureFrames());
            return frame;
        }

        public void Stop()
        {
            captureLoop.End();

            if (frame == null)
            {
                return;
            }

            frame.Release();

            // Destroy is deferred and refuses to run outside play mode, so the edit-mode branch is
            // what keeps a test run from leaking one native texture per case.
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(frame);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(frame);
            }

            frame = null;
        }

        private IEnumerator CaptureFrames()
        {
            while (frame != null)
            {
                // The screenshot reads the back buffer, so it is only correct once everything for
                // this frame has been drawn — including the overlay UI this path exists for.
                yield return endOfFrame;

                if (Time.unscaledTime < nextCaptureTime)
                {
                    continue;
                }

                nextCaptureTime = Time.unscaledTime + captureIntervalSeconds;
                CaptureFrame();
            }
        }

        private void CaptureFrame()
        {
            // The end-of-frame wait sits in the caller on purpose: wrapping it would report the
            // wait as capture cost.
            using (ArtelProfilerMarkers.StreamCaptureFrame.Auto())
            {
                // CaptureScreenshotIntoRenderTexture wants a target the size of the screen, so the
                // downscale to maxWidth happens in the blit rather than here.
                var backBuffer = RenderTexture.GetTemporary(
                    Mathf.Max(2, Screen.width), Mathf.Max(2, Screen.height), 0, RenderTextureFormat.BGRA32);
                var previous = RenderTexture.active;

                try
                {
                    // grab은 현재 target이 아니라 화면 back buffer에서 시작해야 한다. 복원하지 않으면
                    // 같은 end-of-frame에 도는 capture_screen이 이 frame texture를 화면으로 오인한다.
                    RenderTexture.active = null;
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(backBuffer);

                    // The screenshot is written in framebuffer orientation, which under D3D is upside
                    // down relative to what the encoder expects. The flip is not optional: getting it
                    // wrong produces a working-but-inverted stream, which reads as success in a smoke
                    // test and is only caught against on-screen text.
                    Graphics.Blit(backBuffer, frame, new Vector2(1f, -1f), new Vector2(0f, 1f));
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(backBuffer);
                }
            }
        }

        private void ResolveSize(out int width, out int height)
        {
            var screenWidth = Mathf.Max(2, Screen.width);
            var screenHeight = Mathf.Max(2, Screen.height);

            width = maxWidth > 0 ? Mathf.Min(screenWidth, maxWidth) : screenWidth;
            height = Mathf.RoundToInt(screenHeight * (width / (float)screenWidth));

            // Video encoders reject odd dimensions, and the rejection surfaces at track creation
            // rather than here, which puts it a long way from the aspect-ratio maths that caused it.
            width = Mathf.Max(2, width - (width % 2));
            height = Mathf.Max(2, height - (height % 2));
        }
    }
}
