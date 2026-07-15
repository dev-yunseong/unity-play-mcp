using System.Collections.Generic;

namespace Artel.Domain
{
    public sealed class ButtonComponent : SceneComponent
    {
        public ButtonComponent(
            string name,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
            : base(name, states, actions)
        {
        }
    }
}
