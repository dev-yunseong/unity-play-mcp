using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityPlayMcp.Protocol.Dto;
using static UnityPlayMcp.ActionParameters;

namespace UnityPlayMcp
{
    [UnityAction(BuiltInAction.EnterText, "enter_text", "Set text on an input object.", "[targetId, value]")]
    internal sealed class EnterTextAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (!TryReadId(parameters, 0, out var targetId) || parameters.Count < 2)
            {
                completed(ActionResultDto.Failure(actionId, "enter_text requires params [targetId, value]."));
                yield break;
            }

            if (!Context.TargetLookup.TryGetTarget(targetId, out var target))
            {
                completed(ActionResultDto.Failure(actionId, UnknownTarget(targetId)));
                yield break;
            }

            if (!target.CanEnterText)
            {
                completed(ActionResultDto.Failure(actionId, "Target is not an EditText: " + targetId));
                yield break;
            }

            if (!target.IsTextEntryInteractable)
            {
                completed(ActionResultDto.Failure(actionId, NotInteractable(targetId)));
                yield break;
            }

            yield return Context.CursorController.MoveTo(target.RectTransform, Context.CursorMoved);
            var value = parameters[1] == null ? string.Empty : parameters[1].ToString();
            completed(target.EnterText(value)
                ? ActionResultDto.Success(actionId)
                : ActionResultDto.Failure(actionId, NotInteractable(targetId)));
        }
    }

    [UnityAction(BuiltInAction.KeyClick, "key_click", "Press and release a key after a duration.", "[keyCode, positiveDurationSeconds]")]
    internal sealed class KeyClickAction : BuiltInUnityAction
    {
        // 마우스 버튼은 가상 키보드처럼 자동 만료되지 않으므로 unscaled time으로 기다린 뒤 직접 놓는다.
        public override IEnumerator Execute(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadKeyCode(parameters[0], out var key) ||
                !TryReadDuration(parameters[1], out var durationSeconds))
            {
                completed(ActionResultDto.Failure(
                    actionId,
                    "key_click requires params [keyCode, positiveDurationSeconds]."));
                yield break;
            }

            if (!MouseButtonKeyCode.TryGetButton(key, out var button))
            {
                VirtualInput.ClickKey(key, durationSeconds);
                completed(ActionResultDto.Success(actionId));
                yield break;
            }

            Context.SetMouseButton(button, true);
            yield return new WaitForSecondsRealtime(durationSeconds);
            Context.SetMouseButton(button, false);
            completed(ActionResultDto.Success(actionId));
        }
    }

    [UnityAction(BuiltInAction.KeyDown, "key_down", "Hold a key.", "[keyCode]")]
    internal sealed class KeyDownAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            return KeyHoldInput.Execute(Context, actionId, "key_down", parameters, true, completed);
        }
    }

    [UnityAction(BuiltInAction.KeyUp, "key_up", "Release a held key.", "[keyCode]")]
    internal sealed class KeyUpAction : BuiltInUnityAction
    {
        public override IEnumerator Execute(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            return KeyHoldInput.Execute(Context, actionId, "key_up", parameters, false, completed);
        }
    }

    internal static class KeyHoldInput
    {
        internal static IEnumerator Execute(ActionContext context, int actionId, string method,
            List<object> parameters, bool press, Action<ActionResultDto> completed)
        {
            completed(Apply(context, actionId, method, parameters, press));
            yield break;
        }

        private static ActionResultDto Apply(
            ActionContext context,
            int actionId, string method, List<object> parameters, bool press)
        {
            if (parameters == null || parameters.Count == 0 ||
                !TryReadKeyCode(parameters[0], out var key))
            {
                return ActionResultDto.Failure(actionId, method + " requires params [keyCode].");
            }

            // KeyCode.Mouse0 을 가상 키보드에만 넣으면 GetKey 폴링에만 보이고 OnMouseDown 과 uGUI handler 는 부르지 않는다.
            if (MouseButtonKeyCode.TryGetButton(key, out var button))
            {
                context.SetMouseButton(button, press);
                return ActionResultDto.Success(actionId);
            }

            if (press)
            {
                VirtualInput.PressKey(key);
            }
            else
            {
                VirtualInput.ReleaseKey(key);
            }

            return ActionResultDto.Success(actionId);
        }
    }
}
