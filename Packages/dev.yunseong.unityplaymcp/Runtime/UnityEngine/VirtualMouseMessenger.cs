using global::UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// Calls the <c>OnMouse*</c> handlers the engine would call, for the agent's pointer instead of
    /// the real one.
    /// </summary>
    /// <remarks>
    /// These are not EventSystem events and no amount of input mocking reaches them: the engine
    /// picks a collider from the OS cursor every frame and invokes the handler itself, and the
    /// legacy input backend takes no injected values. A game built on <c>OnMouseDown</c> — which
    /// most 2D Unity games are — is otherwise entirely unreachable.
    /// <para>
    /// The handlers are private by convention, so they are reached the way the engine reaches them:
    /// by name, on every component of the object.
    /// </para>
    /// </remarks>
    internal sealed class VirtualMouseMessenger
    {
        /// <summary>The engine sends these for the left button only, so this follows.</summary>
        private const int DrivingButton = 0;

        private const string MouseEnter = "OnMouseEnter";
        private const string MouseOver = "OnMouseOver";
        private const string MouseExit = "OnMouseExit";
        private const string MouseDown = "OnMouseDown";
        private const string MouseDrag = "OnMouseDrag";
        private const string MouseUp = "OnMouseUp";
        private const string MouseUpAsButton = "OnMouseUpAsButton";

        private GameObject hovered;
        private GameObject pressed;

        /// <summary>
        /// One tick of what the engine does every frame: work out what the pointer is over, tell it
        /// so, and keep telling whatever is being dragged.
        /// </summary>
        public void Tick(Vector2 screenPosition, bool buttonHeld)
        {
            var target = Pick(screenPosition);
            UpdateHover(target);

            if (pressed != null)
            {
                if (buttonHeld)
                {
                    // The engine keeps sending this to the object the press started on, even after
                    // the pointer has left it. That is what makes dragging past the edge work.
                    Send(pressed, MouseDrag);
                }
                else
                {
                    Release(target);
                }

                return;
            }

            if (buttonHeld && target != null)
            {
                pressed = target;
                Send(pressed, MouseDown);
            }
        }

        /// <summary>
        /// Ends a press without a release of its own. The connection dropping mid-drag has to look
        /// to the game like the button coming up, or its handler waits forever.
        /// </summary>
        public void Clear()
        {
            if (pressed != null)
            {
                Release(null);
            }

            UpdateHover(null);
        }

        private void Release(GameObject target)
        {
            var wasPressed = pressed;
            pressed = null;

            Send(wasPressed, MouseUp);
            if (wasPressed != null && wasPressed == target)
            {
                Send(wasPressed, MouseUpAsButton);
            }
        }

        private void UpdateHover(GameObject target)
        {
            if (hovered != target)
            {
                Send(hovered, MouseExit);
                hovered = target;
                Send(hovered, MouseEnter);
            }

            // Every frame it stays there, not once on arrival.
            Send(hovered, MouseOver);
        }

        /// <summary>
        /// The one object the engine would deliver to: the nearest hit along a ray from the camera,
        /// 2D and 3D compared on the same distance, filtered by <see cref="Camera.eventMask"/>.
        /// </summary>
        /// <remarks>
        /// 고르는 규칙 자체는 <see cref="PointerTargeting.ColliderUnder"/> 에 있고, 왜 그 규칙인지도
        /// 거기 적혀 있다. ID 로 겨누는 쪽이 커서를 옮기기 전에 같은 규칙으로 확인해야 하기
        /// 때문이다 — 두 벌이 되면 "확인할 때는 맞았는데 배달은 딴 데로 간" 클릭이 생긴다.
        /// </remarks>
        private static GameObject Pick(Vector2 screenPosition)
        {
            return PointerTargeting.ColliderUnder(screenPosition);
        }

        /// <summary>
        /// The null check is Unity's, so an object destroyed while the pointer was on it is simply
        /// not told anything.
        /// </summary>
        private static void Send(GameObject target, string message)
        {
            if (target != null)
            {
                target.SendMessage(message, SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
