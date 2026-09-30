using global::UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// The agent's pointer position and held buttons. Unlike a key press, a button never expires;
    /// only an explicit release ends a drag.
    /// </summary>
    internal sealed class VirtualMouseState
    {
        public const int ButtonCount = 3;

        private readonly ButtonPressState[] buttons = new ButtonPressState[ButtonCount];

        /// <summary>
        /// Real mouse drift that hands the pointer back to the person. Small but above resting jitter.
        /// </summary>
        private const float ReclaimPixels = 4f;

        private Vector2 physicalWhenClaimed;

        /// <summary>
        /// False until the agent first moves the pointer, so an undriven session reports the real pointer.
        /// </summary>
        public bool HasPosition { get; private set; }

        public Vector2 Position { get; private set; }

        public static bool IsButton(int button)
        {
            return button >= 0 && button < ButtonCount;
        }

        public void MoveTo(Vector2 screenPosition, Vector2 physicalPosition)
        {
            Position = screenPosition;
            physicalWhenClaimed = physicalPosition;
            HasPosition = true;
        }

        /// <summary>
        /// Whether the agent's pointer is the one to report. Moving the real mouse hands it back to the person.
        /// </summary>
        /// <remarks>
        /// Releases the claim as a side effect; the read is the only moment the positions are compared.
        /// </remarks>
        public bool OwnsPointer(Vector2 physicalPosition)
        {
            if (!HasPosition)
            {
                return false;
            }

            if ((physicalPosition - physicalWhenClaimed).sqrMagnitude > ReclaimPixels * ReclaimPixels)
            {
                ReleasePointer();
                return false;
            }

            return true;
        }

        /// <summary>Hands the pointer back without touching the buttons.</summary>
        public void ReleasePointer()
        {
            HasPosition = false;
        }

        public void Press(int button, int currentFrame)
        {
            if (!IsButton(button))
            {
                return;
            }

            // 눌린 버튼을 다시 누르면 GetButtonDown 이 한 번 더 참이 된다. mouse_down 과
            // KeyCode.Mouse0 의 key_down 이 겹쳐 올 수 있어 무시한다. 놓기가 예약된 버튼은 다시 누를 수 있다.
            var held = buttons[button];
            if (held != null && !held.ReleaseFrame.HasValue)
            {
                return;
            }

            // Starts next frame, like the virtual keyboard, so a consumer polling in Update does
            // not miss it due to script execution order.
            buttons[button] = new ButtonPressState(currentFrame + 1);
        }

        public void Release(int button, int currentFrame)
        {
            if (!IsButton(button))
            {
                return;
            }

            Release(buttons[button], currentFrame);
        }

        public void ReleaseAll(int currentFrame)
        {
            foreach (var state in buttons)
            {
                Release(state, currentFrame);
            }
        }

        public bool GetButtonDown(int button, int frame)
        {
            var state = StateOf(button);
            return state != null && state.StartFrame == frame && IsHeldOn(state, frame);
        }

        public bool GetButton(int button, int frame)
        {
            var state = StateOf(button);
            return state != null && frame >= state.StartFrame && IsHeldOn(state, frame);
        }

        public bool GetButtonUp(int button, int frame)
        {
            var state = StateOf(button);
            return state != null && state.ReleaseFrame == frame;
        }

        public bool IsAnyButtonHeld(int frame)
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                if (GetButton(button, frame))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Unity 의 <c>Input.anyKeyDown</c> 은 마우스 버튼도 세므로 가상 입력도 같게 맞춘다.
        /// </summary>
        public bool IsAnyButtonDown(int frame)
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                if (GetButtonDown(button, frame))
                {
                    return true;
                }
            }

            return false;
        }

        public void Refresh(int frame)
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                var state = buttons[button];
                if (state != null && state.ReleaseFrame.HasValue && state.ReleaseFrame.Value < frame)
                {
                    buttons[button] = null;
                }
            }
        }

        public void Clear()
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                buttons[button] = null;
            }

            Position = Vector2.zero;
            HasPosition = false;
        }

        private static void Release(ButtonPressState state, int currentFrame)
        {
            if (state == null || state.ReleaseFrame.HasValue)
            {
                return;
            }

            state.ReleaseFrame = currentFrame + 1;
        }

        private static bool IsHeldOn(ButtonPressState state, int frame)
        {
            return !state.ReleaseFrame.HasValue || frame < state.ReleaseFrame.Value;
        }

        private ButtonPressState StateOf(int button)
        {
            return IsButton(button) ? buttons[button] : null;
        }

        private sealed class ButtonPressState
        {
            public ButtonPressState(int startFrame)
            {
                StartFrame = startFrame;
            }

            public int StartFrame { get; }
            public int? ReleaseFrame { get; set; }
        }
    }
}
