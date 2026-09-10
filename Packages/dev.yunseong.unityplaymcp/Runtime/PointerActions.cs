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
    /// <c>button_click</c> 처럼 <c>Button.onClick</c> 을 직접 부르지 않는다. 가상 마우스를 대상
    /// 위로 옮기고 버튼을 눌렀다 놓아, <see cref="VirtualMouseMessenger"/> 의 <c>OnMouse*</c> 와
    /// <see cref="PointerEventDispatcher"/> 의 uGUI 이벤트가 게임에 닿게 한다. collider 로만
    /// 입력을 받는 2D 게임이 ID 로 조작될 수 있는 유일한 경로다.
    /// <para>
    /// <see cref="ActionExecutor"/> 에서 갈라 둔 것은 그 클래스가 이미 800줄이 넘고, 여기 있는
    /// 것은 프레임 순서라는 하나의 관심사이기 때문이다.
    /// </para>
    /// </remarks>
    internal sealed class PointerActions
    {
        /// <summary>
        /// 엔진이 <c>OnMouse*</c> 를 보내는 버튼은 왼쪽뿐이다
        /// (<see cref="VirtualMouseMessenger"/>). 다른 버튼을 받으면 uGUI 대상에서는 눌리고
        /// collider 대상에서는 조용히 아무 일도 안 하는 비대칭이 생기므로, 받지 않는다.
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
        /// 프레임을 세 번 넘기는 것이 이 코루틴의 전부다. uGUI 이벤트는 <c>setButton</c> 을 부른
        /// 그 자리에서 동기로 나가지만 <c>OnMouse*</c> 는 그렇지 않다 —
        /// <see cref="VirtualMouseMessenger"/> 를 미는 것은 host 의 <c>Update</c> 안
        /// <c>VirtualInput.AdvanceFrame</c> 이고, <c>VirtualMouseState.Press</c> 는 눌린 프레임의
        /// <b>다음</b> 프레임부터 눌린 것으로 답한다. 누름과 놓음 사이에 프레임을 두지 않으면
        /// messenger 는 눌린 적이 없는 것으로 보고 <c>OnMouseDown</c> 이 통째로 빠진다.
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

            setButton(DrivingButton, true);
            yield return null;

            setButton(DrivingButton, false);
            yield return null;

            completed(ActionResultDto.Success(actionId, HitOf(targetId, aim)));
        }

        /// <summary>
        /// 원본 위에서 버튼을 누르고, 목적지까지 활강한 뒤 놓는다.
        /// </summary>
        /// <remarks>
        /// 두 자리를 모두 누르기 전에 확인한다. 목적지를 못 찾는 것이 흔한 실패이고, 그것을 누른
        /// 뒤에 알게 되면 이미 쥔 버튼을 풀어야 하기 때문이다. 확인이 끝난 뒤로는 실패할 자리가
        /// 없지만, 누름과 놓음 사이는 <c>finally</c> 로 감싼다 — 그 사이에서 무엇이 튀어나오든
        /// 버튼을 쥔 채로 끝나서는 안 된다.
        /// <para>
        /// 연결이 끊기는 경우는 여기가 아니라 host 가 처리한다.
        /// <c>UnityPlayMcpHost.OnDisable</c> 이 <c>StopTransport</c> 를 거쳐
        /// <c>ReleaseAgentInput</c> 을 부르고, 그 해제는 멱등이라 뒤늦은 <c>finally</c> 가 겹쳐
        /// 돌아도 아무 일도 하지 않는다. 파괴로 멈춘 코루틴의 <c>finally</c> 가 도는지에 기대지
        /// 않는 이유가 그것이다.
        /// </para>
        /// <para>
        /// 활강이 매 프레임 위치를 보고하므로 그 프레임마다
        /// <see cref="PointerEventDispatcher"/> 가 <c>beginDrag</c> 와 <c>drag</c> 를 내고
        /// messenger 가 <c>OnMouseDrag</c> 를 낸다. 포인터가 원본을 떠난 뒤에도 둘 다 원본에
        /// 계속 보내는 것이 pointer capture 이고, 그것은 양쪽 모두 이미 하고 있다.
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
        /// id 를 살아 있고, 켜져 있고, 포인터가 닿을 수 있는 대상으로 푼다.
        /// </summary>
        /// <remarks>
        /// 실패는 네 가지로 갈린다: 파괴/미지, 비활성, 겨눌 면적 없음, 불일치. 그 넷이 에이전트가
        /// 서로 다르게 대응해야 하는 것들이다 — 다시 읽어야 하는지, 게임을 먼저 움직여야 하는지,
        /// 가린 것을 치워야 하는지.
        /// <para>
        /// 비활성 판정은 <c>activeInHierarchy</c> 하나로 한다. 자신이 꺼졌든 부모가 꺼졌든
        /// 포인터가 닿지 못하는 것은 같다. 꺼진 <c>Canvas</c> 나 <c>raycastTarget = false</c> 는
        /// 여기서 걸리지 않고 불일치로 나타나는데, 그것이 맞다 — 오브젝트는 살아 있고 포인터가
        /// 닿지 못할 뿐이다.
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
        /// 겨눈 자리를 보고할 모양으로. 좌표는 여기서 좌상단 기준으로 뒤집는다 — scan 이 보고하고
        /// <c>move_mouse</c> 가 받는 그 좌표계라야 호출자가 그대로 되쓸 수 있다.
        /// </summary>
        private static PointerHitDto HitOf(int targetId, PointerAim aim)
        {
            return new PointerHitDto
            {
                TargetId = targetId,
                HitId = aim.Hit.GetInstanceID(),
                Hit = aim.Hit.name,
                X = aim.ScreenPosition.x,
                Y = Screen.height - aim.ScreenPosition.y
            };
        }
    }
}
