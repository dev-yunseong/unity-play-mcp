using System;
using global::UnityEngine;

namespace Artel
{
    public static class ArtelInput
    {
        private static readonly VirtualKeyboardState VirtualKeyboard = new VirtualKeyboardState();
        private static readonly VirtualMouseState VirtualMouse = new VirtualMouseState();

        public static bool GetKeyDown(KeyCode key)
        {
            return global::UnityEngine.Input.GetKeyDown(key) ||
                   VirtualKeyboard.GetKeyDown(key, Time.frameCount, Time.unscaledTime);
        }

        public static bool GetKeyDown(string name)
        {
            return global::UnityEngine.Input.GetKeyDown(name) ||
                   TryParseKeyCode(name, out var key) &&
                   VirtualKeyboard.GetKeyDown(key, Time.frameCount, Time.unscaledTime);
        }

        public static bool GetKey(KeyCode key)
        {
            return global::UnityEngine.Input.GetKey(key) ||
                   VirtualKeyboard.GetKey(key, Time.frameCount, Time.unscaledTime);
        }

        public static bool GetKey(string name)
        {
            return global::UnityEngine.Input.GetKey(name) ||
                   TryParseKeyCode(name, out var key) &&
                   VirtualKeyboard.GetKey(key, Time.frameCount, Time.unscaledTime);
        }

        public static bool GetKeyUp(KeyCode key)
        {
            return global::UnityEngine.Input.GetKeyUp(key) ||
                   VirtualKeyboard.GetKeyUp(key, Time.frameCount, Time.unscaledTime);
        }

        public static bool GetKeyUp(string name)
        {
            return global::UnityEngine.Input.GetKeyUp(name) ||
                   TryParseKeyCode(name, out var key) &&
                   VirtualKeyboard.GetKeyUp(key, Time.frameCount, Time.unscaledTime);
        }

        public static bool anyKey
        {
            get
            {
                return global::UnityEngine.Input.anyKey ||
                       VirtualKeyboard.AnyKey(Time.frameCount, Time.unscaledTime);
            }
        }

        public static bool anyKeyDown
        {
            get
            {
                return global::UnityEngine.Input.anyKeyDown ||
                       VirtualKeyboard.AnyKeyDown(Time.frameCount, Time.unscaledTime);
            }
        }

        /// <summary>
        /// The agent's pointer once it has been moved, and the real one until then. There is no
        /// combining the two the way the key calls do: a position is one value, not a vote.
        /// </summary>
        public static Vector3 mousePosition
        {
            get
            {
                var physical = global::UnityEngine.Input.mousePosition;
                if (!VirtualMouse.OwnsPointer(physical))
                {
                    return physical;
                }

                var position = VirtualMouse.Position;
                return new Vector3(position.x, position.y, 0f);
            }
        }

        public static bool GetMouseButton(int button)
        {
            return global::UnityEngine.Input.GetMouseButton(button) ||
                   VirtualMouse.GetButton(button, Time.frameCount);
        }

        public static bool GetMouseButtonDown(int button)
        {
            return global::UnityEngine.Input.GetMouseButtonDown(button) ||
                   VirtualMouse.GetButtonDown(button, Time.frameCount);
        }

        public static bool GetMouseButtonUp(int button)
        {
            return global::UnityEngine.Input.GetMouseButtonUp(button) ||
                   VirtualMouse.GetButtonUp(button, Time.frameCount);
        }

        internal static void ClickKey(KeyCode key, float durationSeconds)
        {
            VirtualKeyboard.Click(key, durationSeconds, Time.frameCount);
        }

        internal static void PressKey(KeyCode key)
        {
            VirtualKeyboard.Press(key, Time.frameCount);
        }

        internal static void ReleaseKey(KeyCode key)
        {
            VirtualKeyboard.Release(key, Time.frameCount);
        }

        internal static void MoveMouse(Vector2 screenPosition)
        {
            VirtualMouse.MoveTo(screenPosition, global::UnityEngine.Input.mousePosition);
        }

        internal static void PressMouseButton(int button)
        {
            VirtualMouse.Press(button, Time.frameCount);
        }

        internal static void ReleaseMouseButton(int button)
        {
            VirtualMouse.Release(button, Time.frameCount);
        }

        internal static bool IsMouseButtonHeld(int button)
        {
            return VirtualMouse.GetButton(button, Time.frameCount);
        }

        internal static bool HasVirtualMousePosition
        {
            get { return VirtualMouse.HasPosition; }
        }

        /// <summary>
        /// Lets go of everything the agent was holding. A run that ends mid-drag would otherwise
        /// leave the game with a key or a button held down for the rest of the session — and with
        /// a pointer position frozen where the agent left it, which is worse: the game keeps
        /// reading it forever and the person at the machine cannot move the mouse anywhere.
        /// </summary>
        internal static void ReleaseAllVirtualInput()
        {
            VirtualKeyboard.ReleaseAll(Time.frameCount);
            VirtualMouse.ReleaseAll(Time.frameCount);
            VirtualMouse.ReleasePointer();
        }

        internal static void AdvanceFrame()
        {
            VirtualKeyboard.Refresh(Time.frameCount, Time.unscaledTime);
            VirtualMouse.Refresh(Time.frameCount);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetVirtualKeyboard()
        {
            VirtualKeyboard.Clear();
            VirtualMouse.Clear();
        }

        private static bool TryParseKeyCode(string value, out KeyCode key)
        {
            return Enum.TryParse(value, true, out key) && key != KeyCode.None;
        }
    }
}
