using System.Collections.Generic;

namespace Artel.Domain
{
    public sealed class EditTextComponent : SceneComponent
    {
        public string Content { get; }
        public string Placeholder { get; }

        /// <summary>
        /// Whether a person could actually type into this field right now. False covers a disabled
        /// field, a blocking CanvasGroup, a disabled component, and an inactive object.
        /// </summary>
        public bool Interactable { get; }

        public EditTextComponent(
            string name,
            string content,
            string placeholder,
            bool interactable,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
            : base(name, states, actions)
        {
            Content = content ?? string.Empty;
            Placeholder = placeholder;
            Interactable = interactable;
        }
    }
}
