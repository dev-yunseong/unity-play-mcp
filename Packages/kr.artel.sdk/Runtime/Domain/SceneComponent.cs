using System;
using System.Collections.Generic;

namespace Artel.Domain
{
    public abstract class SceneComponent
    {
        public string Name { get; }
        public IReadOnlyList<TrackedState> States { get; }
        public IReadOnlyList<ActionInvocation> Actions { get; }

        protected SceneComponent(
            string name,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
        {
            Name = name;
            States = states ?? throw new ArgumentNullException(nameof(states));
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }
    }
}
