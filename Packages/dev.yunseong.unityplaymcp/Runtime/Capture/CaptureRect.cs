using UnityPlayMcp.Protocol.Dto;
using UnityEngine;

namespace UnityPlayMcp.Capture
{
    /// <summary>The screen pixels a capture reads, and whether the screen clipped them.</summary>
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
    /// Kept apart from the pixel path so it can be tested without a screen. A wrong rectangle
    /// yields a plausible image of the wrong thing, not an error.
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

            // Use the canvas camera, not the scene camera. An overlay canvas has none, and the
            // scene camera would skew the projection by the whole view transform.
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
                // Reported, not a failure: a half-visible button is a defect the agent wants to see.
                Clipped = visible != requested,
                Requested = requested
            };
            return true;
        }

        /// <summary>Unity 화면 좌표(좌하단 기준)의 영역을 좌상단 기준으로 옮긴다.</summary>
        /// <remarks>
        /// 뒤집기는 보고할 때만 한다. <c>move_mouse</c> 와 scene 의 rect 가 좌상단 기준이라
        /// agent 가 이 값을 그대로 쓸 수 있다.
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
        /// The stored size: same aspect, longest edge capped. Never enlarges.
        /// </summary>
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
