using System;
using System.Collections.Generic;

namespace Artel.Domain
{
    public sealed class SceneBlock
    {
        public int Id { get; }
        public string Name { get; }

        /// <summary>
        /// Whether the GameObject is active in the hierarchy. Only a full scan reports blocks that
        /// are not; the default scan never walks into them.
        /// </summary>
        public bool Active { get; }

        /// <summary>
        /// Where the block sits, in world space and on screen.
        /// </summary>
        public BlockTransform Transform { get; }

        public IReadOnlyList<SceneComponent> Components { get; }
        public IReadOnlyList<SceneBlock> Children { get; }

        public SceneBlock(
            int id,
            string name,
            bool active,
            BlockTransform transform,
            IReadOnlyList<SceneComponent> components,
            IReadOnlyList<SceneBlock> children)
        {
            Id = id;
            Name = name ?? string.Empty;
            Active = active;
            Transform = transform;
            Components = components ?? throw new ArgumentNullException(nameof(components));
            Children = children ?? throw new ArgumentNullException(nameof(children));
        }
    }
}
