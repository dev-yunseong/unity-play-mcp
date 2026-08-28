using System;
using System.Collections;
using UnityEngine;

namespace Artel.Capture
{
    /// <summary>
    /// One encoded still of the screen, or the reason there is none.
    /// </summary>
    internal struct CapturedImage
    {
        public byte[] Bytes;
        public int Width;
        public int Height;

        /// <summary>Null when the capture succeeded.</summary>
        public string Error;

        public bool IsSuccess { get { return Error == null; } }

        public static CapturedImage Failed(string error)
        {
            return new CapturedImage { Error = error };
        }
    }

    /// <summary>
    /// Reads the composited screen into encoded bytes.
    /// </summary>
    /// <remarks>
    /// An interface because everything below it needs a real framebuffer. With a fake in its place
    /// the executor's branching — unknown target, off-screen target, upload refused — is testable
    /// without a screen, which is the part that has decisions in it.
    /// </remarks>
    internal interface IScreenCapturer
    {
        /// <param name="pixelRect">Null captures the whole screen.</param>
        IEnumerator Capture(
            CaptureRequest request,
            Rect? pixelRect,
            Action<CapturedImage> completed);
    }
}
