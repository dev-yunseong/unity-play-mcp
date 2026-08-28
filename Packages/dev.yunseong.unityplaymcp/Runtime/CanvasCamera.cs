using UnityEngine;

namespace Artel
{
    /// <summary>
    /// The camera Unity's screen-point helpers need for a RectTransform.
    /// </summary>
    /// <remarks>
    /// A ScreenSpaceOverlay canvas draws straight onto the screen and has no camera; handing one
    /// the scene's camera throws the result off by the whole projection. Every caller that turns a
    /// RectTransform into a screen point goes through here, so the cursor the player sees and the
    /// coordinates a scan reports cannot drift apart.
    /// </remarks>
    internal static class CanvasCamera
    {
        public static Camera For(RectTransform target)
        {
            if (target == null)
            {
                return null;
            }

            // A nested canvas reports its root's render mode and camera, so the first one above the
            // target answers for the whole chain.
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            return canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
    }
}
