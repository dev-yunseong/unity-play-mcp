using System;
using System.Collections.Generic;

namespace Artel
{
    public sealed class SceneSnapshot
    {
        public int Id { get; }
        public string Name { get; }
        public IReadOnlyList<SceneBlock> Children { get; }

        public SceneSnapshot(int id, string name, IReadOnlyList<SceneBlock> children)
        {
            Id = id;
            Name = name ?? string.Empty;
            Children = children ?? throw new ArgumentNullException(nameof(children));
        }
    }

    public sealed class SceneBlock
    {
        public int Id { get; }
        public string Name { get; }
        public IReadOnlyList<SceneComponent> Components { get; }
        public IReadOnlyList<SceneBlock> Children { get; }

        public SceneBlock(
            int id,
            string name,
            IReadOnlyList<SceneComponent> components,
            IReadOnlyList<SceneBlock> children)
        {
            Id = id;
            Name = name ?? string.Empty;
            Components = components ?? throw new ArgumentNullException(nameof(components));
            Children = children ?? throw new ArgumentNullException(nameof(children));
        }
    }

    public sealed class SceneComponent
    {
        public string Type { get; }
        public string Name { get; }
        public string Content { get; }
        public string Placeholder { get; }
        public IReadOnlyList<TrackedState> States { get; }
        public IReadOnlyList<ActionInvocation> Actions { get; }

        public SceneComponent(
            string type,
            string name,
            string content,
            string placeholder,
            IReadOnlyList<TrackedState> states,
            IReadOnlyList<ActionInvocation> actions)
        {
            Type = string.IsNullOrWhiteSpace(type)
                ? throw new ArgumentException("Component type is required.", nameof(type))
                : type;
            Name = name;
            Content = content;
            Placeholder = placeholder;
            States = states ?? throw new ArgumentNullException(nameof(states));
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }
    }

    public sealed class TrackedState
    {
        public string Tag { get; }
        public string Name { get; }
        public string Type { get; }
        public object Value { get; }

        public TrackedState(string tag, string name, string type, object value)
        {
            Tag = tag ?? string.Empty;
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            Value = value;
        }
    }

    public sealed class ActionInvocation
    {
        public string Tag { get; }
        public string Name { get; }
        public object ReturnValue { get; }
        public DateTimeOffset Timestamp { get; }

        public ActionInvocation(string tag, string name, object returnValue, DateTimeOffset timestamp)
        {
            Tag = tag ?? string.Empty;
            Name = name ?? string.Empty;
            ReturnValue = returnValue;
            Timestamp = timestamp;
        }
    }
}
