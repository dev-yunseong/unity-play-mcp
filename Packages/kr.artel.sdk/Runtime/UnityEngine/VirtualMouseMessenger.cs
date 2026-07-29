using System.Collections.Generic;
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

        private readonly List<Collider2D> overlapping = new List<Collider2D>();
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
        /// The nearest collider under the pointer, in the layers the camera answers events for.
        /// </summary>
        /// <remarks>
        /// <see cref="Camera.eventMask"/> rather than the culling mask, and rather than any
        /// raycaster component: it is the mask the engine itself filters these messages by. The 2D
        /// overlap runs first because a 2D collider sits on a plane a ray can miss entirely when the
        /// sprite is nearer than the camera's near clip.
        /// </remarks>
        private GameObject Pick(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            var world = camera.ScreenToWorldPoint(screenPosition);
            overlapping.Clear();
            Physics2D.OverlapPoint(
                world,
                new ContactFilter2D
                {
                    useTriggers = Physics2D.queriesHitTriggers,
                    useLayerMask = true,
                    layerMask = camera.eventMask,
                    useDepth = false
                },
                overlapping);

            var nearest = Nearest(overlapping, camera);
            if (nearest != null)
            {
                return nearest;
            }

            var hitCount = Physics.RaycastNonAlloc(
                camera.ScreenPointToRay(screenPosition),
                spatialHits,
                camera.farClipPlane,
                camera.eventMask);

            var closest = float.MaxValue;
            GameObject spatial = null;
            for (var index = 0; index < hitCount; index++)
            {
                if (spatialHits[index].distance < closest)
                {
                    closest = spatialHits[index].distance;
                    spatial = spatialHits[index].collider.gameObject;
                }
            }

            return spatial;
        }

        /// <summary>
        /// Nearest to the camera, since overlap results come in no particular order and the engine
        /// delivers to whatever is in front.
        /// </summary>
        private static GameObject Nearest(List<Collider2D> candidates, Camera camera)
        {
            var closest = float.MaxValue;
            GameObject nearest = null;
            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.enabled)
                {
                    continue;
                }

                var distance = camera.transform.InverseTransformPoint(candidate.transform.position).z;
                if (distance < closest)
                {
                    closest = distance;
                    nearest = candidate.gameObject;
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
