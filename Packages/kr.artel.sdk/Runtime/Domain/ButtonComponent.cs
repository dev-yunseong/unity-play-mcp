using System.Collections.Generic;

namespace Artel.Domain
{
    public sealed class ButtonComponent : SceneComponent
    {
        /// <summary>
        /// Whether a person could actually press this button right now. False covers a disabled
        /// button, a blocking CanvasGroup, a disabled component, and an inactive object.
        /// </summary>
        public bool Interactable { get; }

        public ButtonComponent(
            string name,
            bool interactable,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
            : base(name, states, actions)
        {
            Interactable = interactable;
        }
    }
}
