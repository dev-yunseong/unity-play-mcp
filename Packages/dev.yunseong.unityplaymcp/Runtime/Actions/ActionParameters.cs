using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UnityPlayMcp
{
    internal static class ActionParameters
    {
        internal static bool TryReadId(List<object> parameters, int index, out int id)
        {
            id = 0;
            if (parameters == null || index < 0 || parameters.Count <= index ||
                parameters[index] == null)
            {
                return false;
            }

            if (parameters[index] is long longValue)
            {
                id = (int)longValue;
                return true;
            }

            if (parameters[index] is int intValue)
            {
                id = intValue;
                return true;
            }

            return int.TryParse(parameters[index].ToString(), out id);
        }

        internal static bool TryReadNumber(object value, out float number)
        {
            number = 0f;
            if (value == null ||
                !float.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return false;
            }

            return !float.IsInfinity(number) && !float.IsNaN(number);
        }

        internal static bool TryReadMouseButton(List<object> parameters, out int button)
        {
            button = 0;
            if (parameters == null || parameters.Count == 0)
            {
                return true;
            }

            return TryReadId(parameters, 0, out button) && VirtualMouseState.IsButton(button);
        }

        internal static bool TryReadKeyCode(object value, out KeyCode key)
        {
            key = KeyCode.None;
            if (value == null)
            {
                return false;
            }

            if (value is long longValue)
            {
                if (longValue < int.MinValue || longValue > int.MaxValue)
                {
                    return false;
                }

                return TryReadKeyCode((int)longValue, out key);
            }

            if (value is int intValue)
            {
                if (!Enum.IsDefined(typeof(KeyCode), intValue))
                {
                    return false;
                }

                key = (KeyCode)intValue;
                return key != KeyCode.None;
            }

            return Enum.TryParse(value.ToString(), true, out key) &&
                   key != KeyCode.None &&
                   Enum.IsDefined(typeof(KeyCode), key);
        }

        internal static bool TryReadDuration(object value, out float durationSeconds)
        {
            return TryReadNumber(value, out durationSeconds) && durationSeconds > 0f;
        }

        internal static string UnknownTarget(int targetId)
        {
            return "Unknown target id: " + targetId
                + ". Ids stop being valid when the scene reloads or the object is destroyed or recreated; "
                + "read again with get_visible_elements or get_scene_state to get a new id.";
        }

        internal static string NotInteractable(int targetId)
        {
            return "Target is not interactable: " + targetId;
        }
    }
}
