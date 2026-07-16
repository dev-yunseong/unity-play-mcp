using System;
using global::UnityEngine;

namespace Artel
{
    public static class ArtelInput
    {
        private static readonly VirtualKeyboardState VirtualKeyboard = new VirtualKeyboardState();

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

        internal static void ClickKey(KeyCode key, float durationSeconds)
        {
            VirtualKeyboard.Click(key, durationSeconds, Time.frameCount);
        }

        internal static void AdvanceFrame()
        {
            VirtualKeyboard.Refresh(Time.frameCount, Time.unscaledTime);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetVirtualKeyboard()
        {
            VirtualKeyboard.Clear();
        }

        private static bool TryParseKeyCode(string value, out KeyCode key)
        {
            return Enum.TryParse(value, true, out key) && key != KeyCode.None;
        }
    }
}
