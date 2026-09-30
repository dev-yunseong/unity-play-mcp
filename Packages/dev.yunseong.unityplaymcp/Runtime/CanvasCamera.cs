using UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// The camera Unity's screen-point helpers need for a RectTransform.
    /// </summary>
    /// <remarks>
    /// A ScreenSpaceOverlay canvas has no camera; passing the scene camera skews the result.
    /// All RectTransform-to-screen conversions go through here so the cursor and scan coordinates agree.
    /// </remarks>
    internal static class CanvasCamera
    {
        public static Camera For(RectTransform target)
        {
            if (target == null)
            {
                return null;
            }

            // A nested canvas reports its root's render mode and camera.
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            return canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
    }
}
