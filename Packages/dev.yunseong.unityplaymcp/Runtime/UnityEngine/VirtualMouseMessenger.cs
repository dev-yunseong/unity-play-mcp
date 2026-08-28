using global::UnityEngine;

namespace Artel
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

        private readonly RaycastHit[] spatialHits = new RaycastHit[8];

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
        /// A ray rather than a 2D overlap test, even though an overlap would find sprites a ray can
        /// miss. Matching the engine matters more than reaching more: something the engine cannot
        /// pick is something a person cannot click, and an agent that clicks it anyway reports a
        /// game working when it does not.
        /// <para>
        /// One target, not everything under the pointer — the engine picks a single hit and sends
        /// to it, which is why a game with overlapping sprites at the same depth resolves the
        /// ambiguity itself. Only <c>Camera.main</c> is consulted; the engine walks every camera,
        /// so a scene that renders interactive objects through a second one is not covered.
        /// </para>
        /// </remarks>
        private GameObject Pick(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            var ray = camera.ScreenPointToRay(screenPosition);
            var flat = Physics2D.GetRayIntersection(ray, camera.farClipPlane, camera.eventMask);

            var hitCount = Physics.RaycastNonAlloc(
                ray, spatialHits, camera.farClipPlane, camera.eventMask);

            var closest = flat.collider == null ? float.MaxValue : flat.distance;
            var nearest = flat.collider == null ? null : flat.collider.gameObject;
            for (var index = 0; index < hitCount; index++)
            {
                if (spatialHits[index].distance < closest)
                {
                    closest = spatialHits[index].distance;
                    nearest = spatialHits[index].collider.gameObject;
                }
            }

            return nearest;
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
