using UnityPlayMcp.Protocol.Dto;
using UnityEngine;

namespace UnityPlayMcp.Capture
{
    /// <summary>
    /// The screen pixels a capture reads, and whether the screen cut them short.
    /// </summary>
    internal struct CaptureRegion
    {
        public Rect PixelRect;

        /// <summary>True when the screen clipped the requested area away.</summary>
        public bool Clipped;

        /// <summary>화면에 잘리기 전의 영역. Unity 화면 좌표다.</summary>
        public Rect Requested;
    }

    /// <summary>
    /// Turns a UI element into the screen rectangle a capture should read.
    /// </summary>
    /// <remarks>
    /// Kept apart from the pixel path so it can be tested without a screen: the projection is
    /// where a crop actually goes wrong, and a wrong rectangle produces a plausible-looking image
    /// of the wrong thing rather than an error.
    /// </remarks>
    internal static class CaptureRect
    {
        /// <summary>
        /// The screen rectangle for <paramref name="target"/>, grown by <paramref name="padding"/>
        /// pixels and clamped to the screen. Returns false when nothing of the target is on screen.
        /// </summary>
        public static bool TryResolve(
            RectTransform target,
            float padding,
            Rect screen,
            out CaptureRegion region)
        {
            region = default;
            if (target == null)
            {
                return false;
            }

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            // The camera is the canvas's, not the scene's. An overlay canvas has none, and handing
            // one the scene camera throws the projection off by the whole view transform.
            var camera = CanvasCamera.For(target);
            var min = (Vector2)RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = min;
            for (var i = 1; i < corners.Length; i++)
            {
                var point = (Vector2)RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            var requested = Rect.MinMaxRect(
                min.x - padding,
                min.y - padding,
                max.x + padding,
                max.y + padding);

            var visible = Intersect(requested, screen);
            if (visible.width < 1f || visible.height < 1f)
            {
                return false;
            }

            region = new CaptureRegion
            {
                PixelRect = visible,
                // Reported rather than treated as failure: a half-visible button is exactly the
                // kind of defect the agent is looking at the screen to find.
                Clipped = visible != requested,
                Requested = requested
            };
            return true;
        }

        /// <summary>Unity 화면 좌표(좌하단 기준)의 영역을 좌상단 기준으로 옮긴다.</summary>
        /// <remarks>
        /// 뒤집기는 보고하는 이 자리에서만 한다. <c>move_mouse</c> 와 scene 의 rect 가 좌상단 기준이라 agent 는 이 값을 그대로
        /// 되쓴다.
        /// </remarks>
        public static CaptureAreaDto TopLeft(Rect area, int screenHeight)
        {
            return new CaptureAreaDto
            {
                X = area.xMin,
                Y = screenHeight - area.yMax,
                Width = area.width,
                Height = area.height
            };
        }

        /// <summary>화면 픽셀 하나가 이미지에서 차지하는 픽셀 수.</summary>
        public static CaptureScaleDto Scale(Rect source, int imageWidth, int imageHeight)
        {
            return new CaptureScaleDto
            {
                X = source.width <= 0f ? 0f : imageWidth / source.width,
                Y = source.height <= 0f ? 0f : imageHeight / source.height
            };
        }

        /// <summary>
        /// The size a capture is stored at: the same shape, with the longest edge capped.
        /// </summary>
        /// <remarks>
        /// Never enlarges. A 200px button upscaled to the cap costs bytes and adds no detail.
        /// </remarks>
        public static Vector2Int Downscale(int width, int height, int maxEdge)
        {
            if (maxEdge <= 0 || (width <= maxEdge && height <= maxEdge))
            {
                return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
            }

            var scale = maxEdge / (float)Mathf.Max(width, height);
            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(width * scale)),
                Mathf.Max(1, Mathf.RoundToInt(height * scale)));
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            var xMin = Mathf.Max(a.xMin, b.xMin);
            var yMin = Mathf.Max(a.yMin, b.yMin);
            var xMax = Mathf.Min(a.xMax, b.xMax);
            var yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax <= xMin || yMax <= yMin
                ? new Rect(xMin, yMin, 0f, 0f)
                : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
