using System;
using System.Collections.Generic;

namespace Artel.Domain
{
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
}
