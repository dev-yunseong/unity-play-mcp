using System.Collections.Generic;
using System.Globalization;
using Artel.Domain;
using Artel.Protocol.Dto;

namespace Artel.Protocol.Mapping
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
                Active = block.Active,
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
                    Sequence = action.Sequence,
                    Tag = action.Tag,
                    Name = action.Name,
                    Success = action.Success,
                    ReturnValue = action.ReturnValue,
                    Error = action.Success
                        ? null
                        : new ActionErrorDto { Type = action.ErrorType, Message = action.ErrorMessage },
                    Timestamp = action.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                });
            }

            SceneComponentDto dto;
            if (component is ButtonComponent button)
            {
                dto = new ButtonComponentDto { Interactable = button.Interactable };
            }
            else if (component is TextComponent text)
            {
                dto = new TextComponentDto { Content = text.Content };
            }
            else if (component is EditTextComponent editText)
            {
                dto = new EditTextComponentDto
                {
                    Content = editText.Content,
                    Placeholder = editText.Placeholder,
                    Interactable = editText.Interactable
                };
            }
            else if (component is TrackedComponent tracked)
            {
                dto = new TrackedComponentDto { ComponentType = tracked.ComponentType };
            }
            else
            {
                throw new System.ArgumentOutOfRangeException(nameof(component), component.GetType(), "Unsupported scene component.");
            }

            dto.Name = component.Name;
            dto.States = states;
            dto.Actions = actions;
            return dto;
        }
    }
}
