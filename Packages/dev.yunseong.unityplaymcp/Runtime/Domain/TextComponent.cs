using System.Collections.Generic;

namespace Artel.Domain
{
    public sealed class TextComponent : SceneComponent
    {
        public string Content { get; }

        public TextComponent(
            string name,
            string content,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
            : base(name, states, actions)
        {
            Content = content ?? string.Empty;
        }
    }
}
