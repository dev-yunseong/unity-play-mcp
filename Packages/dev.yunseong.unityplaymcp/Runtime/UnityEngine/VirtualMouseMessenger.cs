using global::UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// Calls the <c>OnMouse*</c> handlers the engine would call, for the agent's pointer.
    /// </summary>
    /// <remarks>
    /// These are not EventSystem events: the engine picks a collider from the OS cursor and
    /// invokes them itself, and the legacy input backend takes no injected values. Without this a
    /// game built on <c>OnMouseDown</c> is unreachable.
    /// <para>
    /// The handlers are usually private, so they are sent by name like the engine does.
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
        /// One frame of engine behavior: hover the object under the pointer and keep dragging the pressed one.
        /// </summary>
        public void Tick(Vector2 screenPosition, bool buttonHeld)
        {
            // 대상 선택 규칙은 PointerTargeting.ColliderUnder 하나에 둔다. ID 로 겨누는 쪽도 같은
            // 규칙으로 확인해야 확인과 전달 대상이 어긋나지 않는다.
            var target = PointerTargeting.ColliderUnder(screenPosition);
            UpdateHover(target);

            if (pressed != null)
            {
                if (buttonHeld)
                {
                    // Sent to the pressed object even after the pointer leaves it, like the engine.
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
        /// Ends a press without a release. A dropped connection mid-drag must look like the button
        /// coming up, or the game's handler waits forever.
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
        /// Unity's null check skips an object destroyed under the pointer.
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
