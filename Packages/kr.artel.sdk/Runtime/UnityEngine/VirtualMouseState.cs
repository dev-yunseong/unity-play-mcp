using global::UnityEngine;

namespace Artel
{
    /// <summary>
    /// Where the agent's pointer is and which of its buttons are down. Unlike a key press, a button
    /// never expires on its own — a drag lasts as long as the agent needs, so only an explicit
    /// release ends it.
    /// </summary>
    internal sealed class VirtualMouseState
    {
        public const int ButtonCount = 3;

        private readonly ButtonPressState[] buttons = new ButtonPressState[ButtonCount];

        /// <summary>
        /// False until the agent moves the pointer for the first time, which is what lets the proxy
        /// keep reporting the real pointer in a session nobody is driving.
        /// </summary>
        public bool HasPosition { get; private set; }

        public Vector2 Position { get; private set; }

        public static bool IsButton(int button)
        {
            return button >= 0 && button < ButtonCount;
        }

        public void MoveTo(Vector2 screenPosition)
        {
            Position = screenPosition;
            HasPosition = true;
        }

        public void Press(int button, int currentFrame)
        {
            if (!IsButton(button))
            {
                return;
            }

            // The frame after the request, matching the virtual keyboard: the action is handled in
            // the manager's Update, and a consumer polling in its own Update must not miss it
            // because of script execution order.
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
