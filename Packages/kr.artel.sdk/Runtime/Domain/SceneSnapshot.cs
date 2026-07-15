using System;
using System.Collections.Generic;

namespace Artel.Domain
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
}
