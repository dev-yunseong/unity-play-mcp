using System;
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

        /// <summary>
        /// Calls wired into onClick. Empty unless the scan asked for them, which only the full
        /// all-scene walk does.
        /// </summary>
        public IReadOnlyList<ButtonClickHandler> ClickHandlers { get; }

        public ButtonComponent(
            string name,
            bool interactable,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions,
            IReadOnlyList<ButtonClickHandler> clickHandlers)
            : base(name, states, actions)
        {
            Interactable = interactable;
            ClickHandlers = clickHandlers ?? throw new ArgumentNullException(nameof(clickHandlers));
        }
    }
}
