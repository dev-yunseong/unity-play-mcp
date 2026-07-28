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
                Transform = ToDto(block.Transform),
                Components = components,
                Children = children
            };
        }

        /// <summary>
        /// How many decimal places a coordinate keeps.
        /// </summary>
        /// <remarks>
        /// The poller decides whether to send GAME_STATE by hashing this whole payload, so a raw
        /// float turns a breathing idle animation or a one-pixel layout jitter into a scene change
        /// and the state goes out again every tick. Four places is roughly a fifth of a pixel of
        /// normalized screen space on a 1080p screen — finer than anything worth pointing at, and
        /// coarse enough that a still scene stays still.
        /// </remarks>
        private const int CoordinateDecimals = 4;

        private static BlockTransformDto ToDto(BlockTransform transform)
        {
            return new BlockTransformDto
            {
                World = new WorldPositionDto
                {
                    X = Quantize(transform.World.x),
                    Y = Quantize(transform.World.y),
                    Z = Quantize(transform.World.z)
                },
                Rect = new ScreenRectDto
                {
                    X = Quantize(transform.ScreenRect.x),
                    Y = Quantize(transform.ScreenRect.y),
                    W = Quantize(transform.ScreenRect.width),
                    H = Quantize(transform.ScreenRect.height)
                },
                OnScreen = transform.OnScreen
            };
        }

        /// <summary>
        /// Rounds a coordinate, and flattens the values JSON cannot carry.
        /// </summary>
        /// <remarks>
        /// A degenerate projection — a zero-scaled RectTransform, a camera with a collapsed
        /// frustum — hands back NaN or an infinity, and Newtonsoft writes those as bare literals
        /// that a strict parser on the other end rejects. The whole payload would be lost over one
        /// bad object.
        /// </remarks>
        private static float Quantize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return (float)System.Math.Round(value, CoordinateDecimals);
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
                dto = new ButtonComponentDto
                {
                    Interactable = button.Interactable,
                    OnClick = ToDto(button.ClickHandlers)
                };
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

        // Null rather than an empty list: a scan that did not collect handlers and a button with
        // none both end up here, and neither is worth a field in the payload.
        private static List<ButtonClickHandlerDto> ToDto(IReadOnlyList<ButtonClickHandler> handlers)
        {
            if (handlers.Count == 0)
            {
                return null;
            }

            var dtos = new List<ButtonClickHandlerDto>(handlers.Count);
            foreach (var handler in handlers)
            {
                dtos.Add(new ButtonClickHandlerDto
                {
                    Target = handler.Target,
                    TargetType = handler.TargetType,
                    Method = handler.Method
                });
            }

            return dtos;
        }
    }
}
