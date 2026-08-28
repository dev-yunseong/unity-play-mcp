using Artel.Domain;
using UnityEngine;

namespace Artel.Tracking
{
    /// <summary>
    /// Reads a block's world position and the area it covers on screen.
    /// </summary>
    internal sealed class BlockTransformReader
    {
        private static readonly Rect NoRect = new Rect(0f, 0f, 0f, 0f);

        // GetWorldCorners fills a caller-owned array. One instance reused across a whole scan
        // keeps a per-block allocation off the polling path.
        private readonly Vector3[] corners = new Vector3[4];

        private Camera sceneCamera;

        /// <summary>
        /// Resolves the camera the non-UI blocks project through, once per scan rather than once
        /// per block.
        /// </summary>
        public void BeginScan()
        {
            sceneCamera = Camera.main;
        }

        public BlockTransform Read(Transform transform)
        {
            var rectTransform = transform as RectTransform;
            if (rectTransform != null)
            {
                return ReadRect(rectTransform);
            }

            // A sprite is not a RectTransform, so without this it would report a point and nothing
            // could be aimed at it — the whole area it covers would be zero wide.
            var renderer = transform.GetComponent<Renderer>();
            return renderer != null ? ReadBounds(transform, renderer) : ReadPoint(transform);
        }

        private BlockTransform ReadBounds(Transform transform, Renderer renderer)
        {
            var world = transform.position;
            if (sceneCamera == null)
            {
                return new BlockTransform(world, NoRect, false);
            }

            var bounds = renderer.bounds;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            // All eight corners rather than four: the bounds are a world-space box, and a rotated
            // or perspective-projected one does not keep its front face as the outermost edge.
            for (var corner = 0; corner < 8; corner++)
            {
                var point = sceneCamera.WorldToScreenPoint(new Vector3(
                    (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (corner & 4) == 0 ? bounds.min.z : bounds.max.z));

                if (IsBehind(sceneCamera, point))
                {
                    return new BlockTransform(world, NoRect, false);
                }

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Build(world, min, max);
        }

        private BlockTransform ReadRect(RectTransform rectTransform)
        {
            var camera = CanvasCamera.For(rectTransform);
            var world = rectTransform.TransformPoint(rectTransform.rect.center);

            rectTransform.GetWorldCorners(corners);

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < corners.Length; i++)
            {
                var point = ToScreenPoint(camera, corners[i]);

                // One corner behind the camera mirrors the whole projection, and the other three
                // land somewhere plausible-looking. The rect is unusable as soon as that happens.
                if (IsBehind(camera, point))
                {
                    return new BlockTransform(world, NoRect, false);
                }

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Build(world, min, max);
        }

        private BlockTransform ReadPoint(Transform transform)
        {
            var world = transform.position;

            // A scene with no camera tagged MainCamera cannot project anything. World stays valid
            // and the screen half reports itself as unusable rather than throwing.
            if (sceneCamera == null)
            {
                return new BlockTransform(world, NoRect, false);
            }

            var point = sceneCamera.WorldToScreenPoint(world);
            if (IsBehind(sceneCamera, point))
            {
                return new BlockTransform(world, NoRect, false);
            }

            // A plain Transform is a point, so the rect it reports has no extent.
            return Build(world, point, point);
        }

        private static Vector3 ToScreenPoint(Camera camera, Vector3 world)
        {
            // An overlay canvas measures its world in screen pixels already, which is exactly what
            // RectTransformUtility does with a null camera.
            return camera == null ? world : camera.WorldToScreenPoint(world);
        }

        /// <summary>
        /// Unity reports a point behind the camera with a negative depth and a mirrored x and y,
        /// and nothing about the x and y alone gives it away.
        /// </summary>
        private static bool IsBehind(Camera camera, Vector3 screenPoint)
        {
            // An overlay canvas has no camera and no depth to test; its points are always in front.
            return camera != null && screenPoint.z <= 0f;
        }

        private static BlockTransform Build(Vector3 world, Vector2 min, Vector2 max)
        {
            // Unity counts screen y from the bottom while every consumer of a video frame counts
            // it from the top, so the top edge comes from the larger y.
            var rect = new Rect(
                min.x,
                Screen.height - max.y,
                max.x - min.x,
                max.y - min.y);

            return new BlockTransform(world, rect, IsOnScreen(rect));
        }

        /// <summary>
        /// Overlap with the frame, not containment: a half-scrolled panel is still worth pointing
        /// at, while one entirely off the side is not.
        /// </summary>
        private static bool IsOnScreen(Rect rect)
        {
            return rect.xMax > 0f
                && rect.xMin < Screen.width
                && rect.yMax > 0f
                && rect.yMin < Screen.height;
        }
    }
}
