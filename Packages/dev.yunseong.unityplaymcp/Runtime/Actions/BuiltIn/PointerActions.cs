using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityPlayMcp.Protocol.Dto;
using static UnityPlayMcp.ActionParameters;

namespace UnityPlayMcp
{
    [UnityAction(BuiltInAction.MoveMouse, "move_mouse", "Move the pointer in screen coordinates.", "[x, y]")]
    internal sealed class MoveMouseAction : BuiltInUnityAction
    {
        // Unity 좌표의 원점은 좌하단이므로 화면에서 받은 y를 뒤집는다.
        public override IEnumerator Execute(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadNumber(parameters[0], out var x) ||
                !TryReadNumber(parameters[1], out var y))
            {
                completed(ActionResultDto.Failure(actionId, "move_mouse requires params [x, y]."));
                yield break;
            }

            yield return Context.CursorController.MoveTo(
                new Vector2(x, Screen.height - y), Context.PointerMoved, glide: true);
            completed(ActionResultDto.Success(actionId));
        }
    }

    [UnityAction(BuiltInAction.MouseDown, "mouse_down", "Press a mouse button.", "[button?]")]
    internal sealed class MouseDownAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            return PointerButtonInput.Execute(Context, actionId, "mouse_down", parameters, true, completed);
        }
    }

    [UnityAction(BuiltInAction.MouseUp, "mouse_up", "Release a mouse button.", "[button?]")]
    internal sealed class MouseUpAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            return PointerButtonInput.Execute(Context, actionId, "mouse_up", parameters, false, completed);
        }
    }

    internal static class PointerButtonInput
    {
        // 누르거나 놓은 뒤 한 프레임을 기다려 같은 배치의 다음 입력이 Unity 이벤트를 앞지르지 않게 한다.
        internal static IEnumerator Execute(
            ActionContext context,
            int actionId,
            string method,
            List<object> parameters,
            bool press,
            Action<ActionResultDto> completed)
        {
            if (!TryReadMouseButton(parameters, out var button))
            {
                completed(ActionResultDto.Failure(
                    actionId,
                    method + " requires params [] or [button], where button is 0, 1, or 2."));
                yield break;
            }

            context.SetMouseButton(button, press);
            yield return null;
            completed(ActionResultDto.Success(actionId));
        }
    }
}
