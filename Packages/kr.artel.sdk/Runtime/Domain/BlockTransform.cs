using UnityEngine;

namespace Artel.Domain
{
    /// <summary>
    /// Where a block sits, in the two frames a reader needs.
    /// </summary>
    /// <remarks>
    /// <see cref="World"/> is the object's own position and does not move when the camera does.
    /// <see cref="ScreenRect"/> is that position projected onto what the player is looking at,
    /// normalized to 0..1 with the origin at the top left. Neither covers the other: a UI element
    /// under a ScreenSpaceOverlay canvas has a world position measured in screen pixels, and a
    /// world-space object off the side of the frame still has a real position.
    /// </remarks>
    public readonly struct BlockTransform
    {
        public Vector3 World { get; }

        /// <summary>
        /// The area the block covers on screen, normalized against the screen's own width and
        /// height, with y growing downwards. Width and height are divided by different numbers, so
        /// equal values are not a square.
        /// </summary>
        public Rect ScreenRect { get; }

        /// <summary>
        /// Whether <see cref="ScreenRect"/> can be believed: the block projects in front of the
        /// camera and lands somewhere inside the frame.
        /// </summary>
        /// <remarks>
        /// This is not the same as being visible. A block clipped away by a RectMask2D, hidden
        /// behind another object, or drawn by a fully transparent CanvasGroup still reports true.
        /// Answering those costs a mask walk or a raycast per block, and this runs on the polling
        /// path.
        /// </remarks>
        public bool OnScreen { get; }

        public BlockTransform(Vector3 world, Rect screenRect, bool onScreen)
        {
            World = world;
            ScreenRect = screenRect;
            OnScreen = onScreen;
        }
    }
}
