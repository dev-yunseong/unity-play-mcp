using System.Collections.Generic;
using System.Globalization;

namespace Artel
{
    internal static class SceneSnapshotMapper
    {
        public static SceneDto ToDto(SceneSnapshot scene)
        {
            var children = new List<SceneBlockDto>(scene.Children.Count);
            foreach (var child in scene.Children)
            {
                children.Add(ToDto(child));
            }

            return new SceneDto
            {
                Id = scene.Id,
                Type = "scene",
                Name = scene.Name,
                Children = children
            };
        }

        private static SceneBlockDto ToDto(SceneBlock block)
        {
            var components = new List<SceneComponentDto>(block.Components.Count);
            foreach (var component in block.Components)
            {
                components.Add(ToDto(component));
            }

            var children = new List<SceneBlockDto>(block.Children.Count);
            foreach (var child in block.Children)
            {
                children.Add(ToDto(child));
            }

            return new SceneBlockDto
            {
                Id = block.Id,
                Type = "block",
                Name = block.Name,
                Components = components,
                Children = children
            };
        }

        private static SceneComponentDto ToDto(SceneComponent component)
        {
            var states = new List<StateDto>(component.States.Count);
            foreach (var state in component.States)
            {
                states.Add(new StateDto
                {
                    Tag = state.Tag,
                    Name = state.Name,
                    Type = state.Type,
                    Value = state.Value
                });
            }

            var actions = new List<ActionInvocationDto>(component.Actions.Count);
            foreach (var action in component.Actions)
            {
                actions.Add(new ActionInvocationDto
                {
                    Tag = action.Tag,
                    Name = action.Name,
                    ReturnValue = action.ReturnValue,
                    Timestamp = action.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                });
            }

            return new SceneComponentDto
            {
                Type = component.Type,
                Name = component.Name,
                Content = component.Content,
                Placeholder = component.Placeholder,
                States = states,
                Actions = actions
            };
        }
    }
}
