using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityPlayMcp.Protocol.Dto;
using static UnityPlayMcp.ActionParameters;
using static UnityPlayMcp.AxisParameters;

namespace UnityPlayMcp
{
    [UnityAction(BuiltInAction.SetAxis, "set_axis", "Set a legacy Input Manager axis.", "[axisName, valueBetweenMinusOneAndOne]")]
    internal sealed class SetAxisAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            completed(ExecuteSetAxis(actionId, parameters));
            yield break;
        }

        private static ActionResultDto ExecuteSetAxis(int actionId, List<object> parameters)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadAxisName(parameters[0], out var axisName) ||
                !TryReadAxisValue(parameters[1], out var value))
            {
                return ActionResultDto.Failure(
                    actionId, "set_axis requires params [axisName, valueBetweenMinusOneAndOne].");
            }

            if (!TryConfirmAxisExists(axisName, out var error))
            {
                return ActionResultDto.Failure(actionId, error);
            }

            VirtualInput.SetAxis(axisName, value);
            return ActionResultDto.Success(actionId);
        }
    }

    [UnityAction(BuiltInAction.SetButton, "set_button", "Set a legacy Input Manager button.", "[axisName, pressed]")]
    internal sealed class SetButtonAction : BuiltInUnityAction
    {
        // 버튼을 놓을 때 축을 실제 입력에 돌려줘 GetButtonUp이 변화를 읽게 한다.
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            completed(ExecuteSetButton(actionId, parameters));
            yield break;
        }

        private static ActionResultDto ExecuteSetButton(int actionId, List<object> parameters)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadAxisName(parameters[0], out var axisName) ||
                !TryReadFlag(parameters[1], out var pressed))
            {
                return ActionResultDto.Failure(
                    actionId, "set_button requires params [axisName, pressed].");
            }

            if (!TryConfirmAxisExists(axisName, out var error))
            {
                return ActionResultDto.Failure(actionId, error);
            }

            if (pressed)
            {
                VirtualInput.SetAxis(axisName, 1f);
            }
            else
            {
                VirtualInput.ReleaseAxis(axisName);
            }

            return ActionResultDto.Success(actionId);
        }
    }

    internal static class AxisParameters
    {
        // Input Manager에 없는 이름은 실제 Input API가 예외로 알려 준다.
        internal static bool TryConfirmAxisExists(string axisName, out string error)
        {
            try
            {
                UnityEngine.Input.GetAxis(axisName);
                error = null;
                return true;
            }
            catch (ArgumentException)
            {
                error = "No input axis named '" + axisName + "' is set up in the Input Manager.";
                return false;
            }
        }

        internal static bool TryReadAxisName(object value, out string axisName)
        {
            axisName = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            return !string.IsNullOrEmpty(axisName);
        }

        internal static bool TryReadAxisValue(object value, out float axisValue)
        {
            return TryReadNumber(value, out axisValue) && axisValue >= -1f && axisValue <= 1f;
        }

        internal static bool TryReadFlag(object value, out bool flag)
        {
            if (value is bool booleanValue)
            {
                flag = booleanValue;
                return true;
            }

            flag = false;
            return value != null && bool.TryParse(value.ToString(), out flag);
        }
    }
}
