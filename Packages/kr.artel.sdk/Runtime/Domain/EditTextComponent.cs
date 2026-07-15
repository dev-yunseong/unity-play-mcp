using System.Collections.Generic;

namespace Artel.Domain
{
    public sealed class EditTextComponent : SceneComponent
    {
        public string Content { get; }
        public string Placeholder { get; }

        public EditTextComponent(
            string name,
            string content,
            string placeholder,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
            : base(name, states, actions)
        {
            Content = content ?? string.Empty;
            Placeholder = placeholder;
        }
    }
}
