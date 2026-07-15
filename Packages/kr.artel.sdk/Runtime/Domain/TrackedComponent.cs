using System.Collections.Generic;

namespace Artel.Domain
{
    public sealed class TrackedComponent : SceneComponent
    {
        public string ComponentType { get; }

        public TrackedComponent(
            string componentType,
            string name,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
            : base(name, states, actions)
        {
            ComponentType = componentType;
        }
    }
}
