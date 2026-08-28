using System.Collections.Generic;

namespace Artel.Domain
{
    public enum VisualKind
    {
        /// <summary>A uGUI <c>Image</c>.</summary>
        Image,

        /// <summary>A <c>SpriteRenderer</c>, which lives in the world rather than on a canvas.</summary>
        Sprite
    }

    /// <summary>
    /// Something the player can see. It carries no interaction of its own — a scan reports it so
    /// that what is on screen is not limited to what happens to be a button, and so the agent can
    /// aim the pointer at it.
    /// </summary>
    public sealed class VisualComponent : SceneComponent
    {
        public VisualKind Kind { get; }

        /// <summary>The sprite asset's name, or null when nothing is assigned — a flat-colour panel.</summary>
        public string SpriteName { get; }

        public VisualComponent(
            string name,
            VisualKind kind,
            string spriteName,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
            : base(name, states, actions)
        {
            Kind = kind;
            SpriteName = spriteName;
        }
    }
}
