using System;
using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Protocol.Dto;
using UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// ID 로 받은 대상을 실제 입력 경로로 클릭하고 드래그한다.
    /// </summary>
    /// <remarks>
    /// <c>Button.onClick</c> 을 직접 부르지 않고 가상 마우스를 옮겨 버튼을 눌렀다 놓는다.
    /// 그래서 <see cref="VirtualMouseMessenger"/> 의 <c>OnMouse*</c> 와 <see cref="PointerEventDispatcher"/>
    /// 의 uGUI 이벤트가 모두 나간다. collider 로만 입력을 받는 게임은 이 경로로만 id 로 조작할 수 있다.
    /// </remarks>
    internal sealed class PointerActions
    {
        /// <summary>
        /// <c>OnMouse*</c> 는 왼쪽 버튼만 보낸다 (<see cref="VirtualMouseMessenger"/>).
        /// 다른 버튼은 collider 대상에서 아무 일도 하지 않으므로 받지 않는다.
        /// </summary>
        private const int DrivingButton = 0;

        private readonly TargetLookup targetLookup;
        private readonly CursorController cursorController;
        private readonly PointerEventDispatcher pointerEvents;
        private readonly Action<int, bool> setButton;
        private readonly Action<Vector2> pointerMoved;

        public PointerActions(
            TargetLookup targetLookup,
            CursorController cursorController,
            PointerEventDispatcher pointerEvents,
            Action<int, bool> setButton,
            Action<Vector2> pointerMoved)
        {
            this.targetLookup = targetLookup;
            this.cursorController = cursorController;
            this.pointerEvents = pointerEvents;
            this.setButton = setButton;
            this.pointerMoved = pointerMoved;
        }

        /// <summary>
        /// 대상 위로 포인터를 옮기고 왼쪽 버튼을 눌렀다 놓는다.
        /// </summary>
        /// <remarks>
        /// uGUI 이벤트는 <c>setButton</c> 에서 동기로 나가지만 <c>OnMouse*</c> 는 host <c>Update</c> 의
        /// <c>VirtualInput.AdvanceFrame</c> 에서 나가고, <c>VirtualMouseState.Press</c> 는 다음 프레임부터
        /// 눌린 것으로 답한다. 누름과 놓음 사이에 프레임을 두지 않으면 <c>OnMouseDown</c> 이 빠진다.
        /// </remarks>
        public IEnumerator Click(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (!ActionExecutor.TryReadId(parameters, 0, out var targetId))
            {
                completed(ActionResultDto.Failure(
                    actionId, "pointer_click requires params [targetId]."));
                yield break;
            }

            if (!TryAim("pointer_click", targetId, out var aim, out var error))
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            yield return cursorController.MoveTo(aim.ScreenPosition, pointerMoved);
            yield return null;

            // 누름과 놓음 사이에 던질 코드가 없어 try/finally 를 두지 않는다. 중단은 host 의
            // ReleaseAgentInput 이 처리한다.
            setButton(DrivingButton, true);
            yield return null;

            setButton(DrivingButton, false);
            yield return null;

            completed(ActionResultDto.Success(actionId, HitOf(targetId, aim)));
        }

        /// <summary>
        /// 버튼을 누르지 않고 포인터를 대상 위에 올린다.
        /// </summary>
        /// <remarks>
        /// click 과 같은 <see cref="TryAim"/> 과 <c>pointerMoved</c> 를 쓴다. uGUI enter/exit 는
        /// <c>PointerEventDispatcher.MoveTo</c> 가, <c>OnMouseEnter</c>/<c>OnMouseOver</c> 는
        /// <c>VirtualMouseMessenger</c> 가 보낸다.
        ///
        /// 옮긴 뒤 한 프레임 기다렸다가 다시 확인한다. 그 사이 대상이 가려지거나 파괴되면 실패로 보고한다.
        /// 포인터는 다음 입력이 옮길 때까지 그 자리에 남는다.
        /// </remarks>
        public IEnumerator Hover(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (!ActionExecutor.TryReadId(parameters, 0, out var targetId))
            {
                completed(ActionResultDto.Failure(
                    actionId, "pointer_hover requires params [targetId]."));
                yield break;
            }

            if (!TryAim("pointer_hover", targetId, out var aim, out var error))
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            yield return cursorController.MoveTo(aim.ScreenPosition, pointerMoved);
            yield return null;

            if (!targetLookup.TryGetGameObject(targetId, out var target))
            {
                completed(ActionResultDto.Failure(
                    actionId,
                    "pointer_hover: target #" + targetId +
                    " was destroyed while the pointer moved onto it."));
                yield break;
            }

            if (!target.activeInHierarchy)
            {
                completed(ActionResultDto.Failure(
                    actionId,
                    "pointer_hover: target " + PointerTargeting.Describe(target, targetId) +
                    " was deactivated while the pointer moved onto it."));
                yield break;
            }

            if (!PointerTargeting.StillReaches(target, aim.ScreenPosition, pointerEvents, out var hovered))
            {
                completed(ActionResultDto.Failure(
                    actionId,
                    string.Format(
                        "pointer_hover: the pointer rests on {0} instead of {1} at ({2:0}, {3:0}). "
                        + "The target moved or something was drawn on top of it after it was aimed at.",
                        hovered == null ? "nothing" : PointerTargeting.Describe(hovered, hovered.GetInstanceID()),
                        PointerTargeting.Describe(target, targetId),
                        aim.ScreenPosition.x,
                        Screen.height - aim.ScreenPosition.y)));
                yield break;
            }

            completed(ActionResultDto.Success(actionId, HitOf(
                targetId, new PointerAim(aim.ScreenPosition, hovered.GetInstanceID(), hovered.name))));
        }

        /// <summary>
        /// 원본 위에서 버튼을 누르고 목적지까지 이동한 뒤 놓는다.
        /// </summary>
        /// <remarks>
        /// 버튼을 누른 뒤 실패하지 않도록 두 대상을 먼저 확인한다. 누름과 놓음 사이는 <c>finally</c> 로
        /// 감싸 버튼이 눌린 채 끝나지 않게 한다.
        /// <para>
        /// 연결 끊김은 host 가 처리한다. <c>UnityPlayMcpHost.OnDisable</c> 이 <c>ReleaseAgentInput</c> 을
        /// 부르고, 해제는 멱등이라 뒤늦은 <c>finally</c> 와 겹쳐도 된다. 파괴로 멈춘 coroutine 의
        /// <c>finally</c> 실행에 기대지 않는다.
        /// </para>
        /// <para>
        /// 이동 중 매 프레임 <see cref="PointerEventDispatcher"/> 가 <c>beginDrag</c>/<c>drag</c> 를,
        /// messenger 가 <c>OnMouseDrag</c> 를 원본에 보낸다 (pointer capture).
        /// </para>
        /// </remarks>
        public IEnumerator Drag(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (!ActionExecutor.TryReadId(parameters, 0, out var sourceId) ||
                !ActionExecutor.TryReadId(parameters, 1, out var targetId))
            {
                completed(ActionResultDto.Failure(
                    actionId, "pointer_drag requires params [sourceId, targetId]."));
                yield break;
            }

            if (!TryAim("pointer_drag", sourceId, out var from, out var error) ||
                !TryAim("pointer_drag", targetId, out var to, out error))
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            yield return cursorController.MoveTo(from.ScreenPosition, pointerMoved);
            yield return null;

            setButton(DrivingButton, true);
            try
            {
                yield return null;
                yield return cursorController.MoveTo(to.ScreenPosition, pointerMoved, glide: true);
                yield return null;
            }
            finally
            {
                setButton(DrivingButton, false);
            }

            yield return null;

            completed(ActionResultDto.Success(actionId, new PointerDragResultDto
            {
                From = HitOf(sourceId, from),
                To = HitOf(targetId, to)
            }));
        }

        /// <summary>
        /// id 를 살아 있고, 활성이고, 포인터가 닿을 수 있는 대상으로 푼다.
        /// </summary>
        /// <remarks>
        /// 실패는 파괴/미지, 비활성, 겨눌 면적 없음, 불일치의 네 경우다. agent 가 각각 다르게 대응해야 하므로
        /// 구분해 보고한다.
        /// <para>
        /// 비활성은 <c>activeInHierarchy</c> 로만 판정한다. 꺼진 <c>Canvas</c> 나 <c>raycastTarget = false</c>
        /// 는 오브젝트가 살아 있으므로 불일치로 보고된다.
        /// </para>
        /// </remarks>
        private bool TryAim(string method, int targetId, out PointerAim aim, out string error)
        {
            aim = default;

            if (!targetLookup.TryGetGameObject(targetId, out var target))
            {
                error = method + ": no live object has id " + targetId +
                        ". It was never scanned, or the game destroyed it.";
                return false;
            }

            if (!target.activeInHierarchy)
            {
                error = method + ": target " + PointerTargeting.Describe(target, targetId) +
                        " is not active in the scene.";
                return false;
            }

            return PointerTargeting.TryAim(
                method, targetId, target, pointerEvents, out aim, out error);
        }

        /// <summary>
        /// 겨눈 위치를 결과로 만든다. y 좌표는 scan 과 <c>move_mouse</c> 가 쓰는 좌상단 기준으로 뒤집는다.
        /// </summary>
        /// <remarks>
        /// 그 사이 대상이 파괴될 수 있으므로 <c>GameObject</c> 대신 <see cref="PointerAim"/> 에 복사해 둔 값만 읽는다.
        /// </remarks>
        private static PointerHitDto HitOf(int targetId, PointerAim aim)
        {
            return new PointerHitDto
            {
                TargetId = targetId,
                HitId = aim.HitId,
                Hit = aim.HitName,
                X = aim.ScreenPosition.x,
                Y = Screen.height - aim.ScreenPosition.y
            };
        }
    }
}
