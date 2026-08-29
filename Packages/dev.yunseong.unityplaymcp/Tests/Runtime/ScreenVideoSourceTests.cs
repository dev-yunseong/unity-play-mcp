using System.Collections;
using Artel.Streaming;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Streaming
{
    /// <summary>
    /// The capture loop itself needs a running frame loop and a back buffer, so it is not driven
    /// here. What is covered is the allocation lifetime: a capture target that outlives its
    /// session is a leak the game pays for with no peer attached.
    /// </summary>
    public sealed class ScreenVideoSourceTests
    {
        [Test]
        public void Stop_ReleasesTheRenderTextureAndTheCaptureLoop()
        {
            var captureLoop = new RecordingCaptureLoopRunner();
            var source = new ScreenVideoSource(captureLoop, 640, 30);

            var frame = source.Start();

            Assert.That(frame, Is.Not.Null);
            Assert.That(source.IsCapturing, Is.True);
            Assert.That(captureLoop.IsRunning, Is.True);

            source.Stop();

            Assert.That(captureLoop.IsRunning, Is.False);
            Assert.That(source.IsCapturing, Is.False);
            Assert.That(source.Frame, Is.Null);

            // A destroyed UnityEngine.Object is not a null reference, so this is the assertion that
            // actually says the native texture is gone rather than merely dereferenced.
            Assert.That(frame == null, Is.True);
        }

        [Test]
        public void Stop_IsSafeWithoutAStart()
        {
            var source = new ScreenVideoSource(new RecordingCaptureLoopRunner(), 640, 30);

            Assert.DoesNotThrow(source.Stop);
        }

        [Test]
        public void Start_KeepsTheFrameWithinMaxWidthAndEvenSided()
        {
            var captureLoop = new RecordingCaptureLoopRunner();
            var source = new ScreenVideoSource(captureLoop, 320, 30);

            var frame = source.Start();

            Assert.That(frame.width, Is.LessThanOrEqualTo(320));
            Assert.That(frame.width % 2, Is.Zero);
            Assert.That(frame.height % 2, Is.Zero);

            source.Stop();
        }

        [Test]
        public void Start_IsIdempotentSoOneSessionNeverHoldsTwoTextures()
        {
            var captureLoop = new RecordingCaptureLoopRunner();
            var source = new ScreenVideoSource(captureLoop, 640, 30);

            var frame = source.Start();

            Assert.That(source.Start(), Is.SameAs(frame));
            Assert.That(captureLoop.BeginCount, Is.EqualTo(1));

            source.Stop();
        }

        private sealed class RecordingCaptureLoopRunner : ICaptureLoopRunner
        {
            public bool IsRunning { get; private set; }
            public int BeginCount { get; private set; }

            public void Begin(IEnumerator loop)
            {
                BeginCount++;
                IsRunning = true;
            }

            public void End()
            {
                IsRunning = false;
            }
        }
    }
}
