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
                Screen = new ScreenSizeDto { W = scene.Screen.x, H = scene.Screen.y },
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
        /// How many decimal places a world coordinate keeps.
        /// </summary>
        /// <remarks>
        /// The poller decides whether to send GAME_STATE by hashing this whole payload, so a raw
        /// float turns a breathing idle animation into a scene change and the state goes out again
        /// every tick. Screen rects avoid this by being whole pixels; world positions are in the
        /// game's own units and need a place to round to.
        /// </remarks>
        private const int WorldDecimals = 4;

        private static BlockTransformDto ToDto(BlockTransform transform)
        {
            return new BlockTransformDto
            {
                World = new WorldPositionDto
                {
                    X = QuantizeWorld(transform.World.x),
                    Y = QuantizeWorld(transform.World.y),
                    Z = QuantizeWorld(transform.World.z)
                },
                Rect = new ScreenRectDto
                {
                    X = ToPixels(transform.ScreenRect.x),
                    Y = ToPixels(transform.ScreenRect.y),
                    W = ToPixels(transform.ScreenRect.width),
                    H = ToPixels(transform.ScreenRect.height)
                },
                OnScreen = transform.OnScreen
            };
        }

        private static float QuantizeWorld(float value)
        {
            return IsWritable(value) ? (float)System.Math.Round(value, WorldDecimals) : 0f;
        }

        /// <summary>
        /// Rounds to a whole pixel, which is both the finest thing worth pointing at and a quantum
        /// coarse enough that a still scene keeps hashing the same.
        /// </summary>
        private static int ToPixels(float value)
        {
            if (!IsWritable(value))
            {
                return 0;
            }

            // A rect measured against a huge world-space canvas can project past what an int
            // holds, and an unchecked cast wraps it to a plausible-looking negative.
            var rounded = System.Math.Round(value);
            if (rounded > int.MaxValue)
            {
                return int.MaxValue;
            }

            return rounded < int.MinValue ? int.MinValue : (int)rounded;
        }

        /// <summary>
        /// Whether JSON can carry the value at all.
        /// </summary>
        /// <remarks>
        /// A degenerate projection — a zero-scaled RectTransform, a camera with a collapsed
        /// frustum — hands back NaN or an infinity, and Newtonsoft writes those as bare literals
        /// that a strict parser on the other end rejects. The whole payload would be lost over one
        /// bad object.
        /// </remarks>
        private static bool IsWritable(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
            else if (component is VisualComponent visual)
            {
                dto = visual.Kind == VisualKind.Sprite
                    ? (SceneComponentDto)new SpriteComponentDto { Sprite = visual.SpriteName }
                    : new ImageComponentDto { Sprite = visual.SpriteName };
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
