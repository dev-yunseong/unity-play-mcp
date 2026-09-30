using System;
using global::UnityEngine;

namespace UnityPlayMcp
{
    public static class VirtualInput
    {
        private static readonly VirtualKeyboardState VirtualKeyboard = new VirtualKeyboardState();
        private static readonly VirtualMouseState VirtualMouse = new VirtualMouseState();
        private static readonly VirtualAxisState VirtualAxes = new VirtualAxisState();
        private static readonly VirtualMouseMessenger MouseMessenger = new VirtualMouseMessenger();

        /// <summary>
        /// 실제 입력, 가상 키보드, 가상 마우스 버튼을 OR 로 합친다. <c>KeyCode.Mouse0</c> 같은 키는
        /// 마우스 상태도 봐야 <c>mouse_down</c> 으로 들어온 클릭이 보인다.
        /// </summary>
        public static bool GetKeyDown(KeyCode key)
        {
            return global::UnityEngine.Input.GetKeyDown(key) ||
                   VirtualKeyboard.GetKeyDown(key, Time.frameCount, Time.unscaledTime) ||
                   MouseButtonKeyCode.TryGetButton(key, out var button) &&
                   VirtualMouse.GetButtonDown(button, Time.frameCount);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKeyDown(string name)
        {
            return global::UnityEngine.Input.GetKeyDown(name) ||
                   TryParseKeyCode(name, out var key) && GetKeyDown(key);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKey(KeyCode key)
        {
            return global::UnityEngine.Input.GetKey(key) ||
                   VirtualKeyboard.GetKey(key, Time.frameCount, Time.unscaledTime) ||
                   MouseButtonKeyCode.TryGetButton(key, out var button) &&
                   VirtualMouse.GetButton(button, Time.frameCount);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKey(string name)
        {
            return global::UnityEngine.Input.GetKey(name) ||
                   TryParseKeyCode(name, out var key) && GetKey(key);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKeyUp(KeyCode key)
        {
            return global::UnityEngine.Input.GetKeyUp(key) ||
                   VirtualKeyboard.GetKeyUp(key, Time.frameCount, Time.unscaledTime) ||
                   MouseButtonKeyCode.TryGetButton(key, out var button) &&
                   VirtualMouse.GetButtonUp(button, Time.frameCount);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKeyUp(string name)
        {
            return global::UnityEngine.Input.GetKeyUp(name) ||
                   TryParseKeyCode(name, out var key) && GetKeyUp(key);
        }

        public static bool anyKey
        {
            get
            {
                return global::UnityEngine.Input.anyKey ||
                       VirtualKeyboard.AnyKey(Time.frameCount, Time.unscaledTime) ||
                       VirtualMouse.IsAnyButtonHeld(Time.frameCount);
            }
        }

        public static bool anyKeyDown
        {
            get
            {
                return global::UnityEngine.Input.anyKeyDown ||
                       VirtualKeyboard.AnyKeyDown(Time.frameCount, Time.unscaledTime) ||
                       VirtualMouse.IsAnyButtonDown(Time.frameCount);
            }
        }

        /// <summary>
        /// The agent's pointer once it has been moved, otherwise the real one. Positions cannot be combined.
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

        /// <summary>
        /// A held axis replaces the real one instead of combining, so stray real input cannot
        /// override what the agent asked for.
        /// </summary>
        public static float GetAxis(string axisName)
        {
            if (VirtualAxes.TryGetValue(axisName, Time.frameCount, out var value))
            {
                return value;
            }

            return global::UnityEngine.Input.GetAxis(axisName);
        }

        /// <summary>
        /// The held value as given, not snapped to -1/0/1.
        /// </summary>
        /// <remarks>
        /// Same as <see cref="GetAxis"/> except for the fallback; both exist because the weaver
        /// matches call sites by signature.
        /// </remarks>
        public static float GetAxisRaw(string axisName)
        {
            if (VirtualAxes.TryGetValue(axisName, Time.frameCount, out var value))
            {
                return value;
            }

            return global::UnityEngine.Input.GetAxisRaw(axisName);
        }

        public static bool GetButton(string axisName)
        {
            return global::UnityEngine.Input.GetButton(axisName) ||
                   VirtualAxes.GetButton(axisName, Time.frameCount);
        }

        public static bool GetButtonDown(string axisName)
        {
            return global::UnityEngine.Input.GetButtonDown(axisName) ||
                   VirtualAxes.GetButtonDown(axisName, Time.frameCount);
        }

        public static bool GetButtonUp(string axisName)
        {
            return global::UnityEngine.Input.GetButtonUp(axisName) ||
                   VirtualAxes.GetButtonUp(axisName, Time.frameCount);
        }

        internal static void SetAxis(string axisName, float value)
        {
            VirtualAxes.Set(axisName, value, Time.frameCount);
        }

        internal static void ReleaseAxis(string axisName)
        {
            VirtualAxes.Release(axisName, Time.frameCount);
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
        /// Lets go of everything the agent was holding. Otherwise a run ending mid-drag leaves keys
        /// held and the pointer frozen where the agent left it.
        /// </summary>
        internal static void ReleaseAllVirtualInput()
        {
            VirtualKeyboard.ReleaseAll(Time.frameCount);
            VirtualMouse.ReleaseAll(Time.frameCount);
            VirtualMouse.ReleasePointer();
            VirtualAxes.ReleaseAll(Time.frameCount);
            MouseMessenger.Clear();
        }

        internal static void AdvanceFrame()
        {
            VirtualKeyboard.Refresh(Time.frameCount, Time.unscaledTime);
            VirtualMouse.Refresh(Time.frameCount);
            VirtualAxes.Refresh(Time.frameCount);

            // Only while the agent owns the pointer; the engine already sends OnMouse* for the real cursor.
            if (VirtualMouse.OwnsPointer(global::UnityEngine.Input.mousePosition))
            {
                MouseMessenger.Tick(VirtualMouse.Position, VirtualMouse.GetButton(0, Time.frameCount));
            }
            else
            {
                MouseMessenger.Clear();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetVirtualKeyboard()
        {
            VirtualKeyboard.Clear();
            VirtualMouse.Clear();
            VirtualAxes.Clear();
            MouseMessenger.Clear();
        }

        private static bool TryParseKeyCode(string value, out KeyCode key)
        {
            return Enum.TryParse(value, true, out key) && key != KeyCode.None;
        }
    }
}
