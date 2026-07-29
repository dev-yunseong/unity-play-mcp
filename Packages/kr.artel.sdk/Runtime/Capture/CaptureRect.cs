using UnityEngine;

namespace Artel.Capture
{
    /// <summary>
    /// The screen pixels a capture reads, and whether the screen cut them short.
    /// </summary>
    internal struct CaptureRegion
    {
        public Rect PixelRect;

        /// <summary>True when the screen clipped the requested area away.</summary>
        public bool Clipped;
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
                Clipped = visible != requested
            };
            return true;
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
