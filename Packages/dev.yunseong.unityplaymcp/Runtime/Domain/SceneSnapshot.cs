using System;
using System.Collections.Generic;
using UnityEngine;

namespace Artel.Domain
{
    public sealed class SceneSnapshot
    {
        public int Id { get; }
        public string Name { get; }

        /// <summary>
        /// The screen the blocks' rects were measured against.
        /// </summary>
        /// <remarks>
        /// Rects are pixels, so they mean nothing without the screen they were measured on. It
        /// rides on the scene rather than on every block because one scan measures against one
        /// screen.
        /// </remarks>
        public Vector2Int Screen { get; }

        public IReadOnlyList<SceneBlock> Children { get; }

        public SceneSnapshot(int id, string name, Vector2Int screen, IReadOnlyList<SceneBlock> children)
        {
            Id = id;
            Name = name ?? string.Empty;
            Screen = screen;
            Children = children ?? throw new ArgumentNullException(nameof(children));
        }
    }
}
