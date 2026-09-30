using System;
using System.Collections;
using UnityEngine;

namespace UnityPlayMcp.Capture
{
    /// <summary>One encoded screen image, or the error.</summary>
    internal struct CapturedImage
    {
        public byte[] Bytes;
        public int Width;
        public int Height;

        /// <summary>캡처 시점의 <c>Screen.width</c>/<c>Screen.height</c>. 입력 좌표의 기준 공간이다.</summary>
        public int ScreenWidth;
        public int ScreenHeight;

        /// <summary>이미지가 담은 화면 영역. Unity 화면 좌표(좌하단 기준)다.</summary>
        public Rect Source;

        /// <summary>back buffer 를 읽은 프레임의 <c>Time.frameCount</c>.</summary>
        public int Frame;

        /// <summary>그 순간의 활성 씬 이름. 없으면 null.</summary>
        public string Scene;

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
    /// An interface so the executor's cases (unknown target, off-screen target, upload refused)
    /// can be tested with a fake, without a framebuffer.
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
