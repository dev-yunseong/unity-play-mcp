using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Artel
{
    /// <summary>
    /// Turns the agent's pointer into the events uGUI listens for. Unity's own input module reads
    /// the physical mouse and nothing else, so a game's <see cref="IDragHandler"/> would never hear
    /// from a virtual pointer without this. The virtual mouse state covers the other half — games
    /// that poll <c>Input</c> directly and never touch the EventSystem.
    /// </summary>
    internal sealed class PointerEventDispatcher
    {
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private readonly PointerEventData[] pointers =
            new PointerEventData[VirtualMouseState.ButtonCount];

        private PointerEventData hoverData;
        private EventSystem hoverEventSystem;
        private GameObject hovered;
        private Vector2 position;

        public void MoveTo(Vector2 screenPosition)
        {
            var delta = screenPosition - position;
            position = screenPosition;

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            var target = Raycast(eventSystem, screenPosition, out var hit);
            UpdateHover(target);

            foreach (var data in pointers)
            {
                if (data == null)
                {
                    continue;
                }

                data.position = screenPosition;
                data.delta = delta;
                data.pointerCurrentRaycast = hit;

                if (data.pointerDrag == null)
                {
                    continue;
                }

                if (!data.dragging)
                {
                    if (!HasClearedDragThreshold(eventSystem, data, screenPosition))
                    {
                        continue;
                    }

                    data.dragging = true;
                    // A pointer that travelled is no longer a click, which is what keeps a drag
                    // from also firing the click handler of whatever it started on.
                    data.eligibleForClick = false;
                    ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                }

                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
            }
        }

        public void Press(int button)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                // Worth saying out loud: from the outside this is indistinguishable from a press
                // that landed on nothing, and a game with a canvas is expected to have one.
                Debug.LogWarning("[Artel] mouse_down found no EventSystem, so no uGUI element can answer it.");
                return;
            }

            if (!VirtualMouseState.IsButton(button) || pointers[button] != null)
            {
                return;
            }

            var target = Raycast(eventSystem, position, out var hit);
            UpdateHover(target);

            var data = new PointerEventData(eventSystem)
            {
                // Unity reserves the negative ids for mouse buttons: -1 left, -2 right, -3 middle.
                pointerId = -1 - button,
                button = (PointerEventData.InputButton)button,
                position = position,
                pressPosition = position,
                pointerCurrentRaycast = hit,
                pointerPressRaycast = hit,
                pointerEnter = hovered,
                eligibleForClick = true,
                useDragThreshold = true
            };

            if (target != null)
            {
                data.rawPointerPress = target;
                // The object that answers the press is rarely the one the ray hit — a Button's
                // handler sits on the parent of the graphic that was actually under the pointer.
                data.pointerPress = ExecuteEvents.ExecuteHierarchy(
                    target, data, ExecuteEvents.pointerDownHandler)
                    ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
                data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            }

            if (data.pointerDrag != null)
            {
                // The handler may answer this by clearing useDragThreshold — a ScrollRect does —
                // and the threshold check below has to respect that.
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
            }

            // One line per press, and a press is something the agent asked for, so this is not
            // chatter. Without it a drag that does nothing is indistinguishable from a drag that
            // was never delivered, and the difference is the whole diagnosis.
            Debug.Log(string.Format(
                "[Artel] mouse_down at ({0}, {1}) over {2} hits: {3}. press={4} drag={5}",
                position.x,
                position.y,
                raycastResults.Count,
                DescribeHits(),
                Describe(data.pointerPress),
                Describe(data.pointerDrag)));

            if (data.pointerDrag == null)
            {
                LogCollidersUnder(position);
            }

            pointers[button] = data;
        }

        public void Release(int button)
        {
            if (!VirtualMouseState.IsButton(button))
            {
                return;
            }

            var data = pointers[button];
            pointers[button] = null;

            var eventSystem = EventSystem.current;
            if (data == null || eventSystem == null)
            {
                return;
            }

            var target = Raycast(eventSystem, position, out var hit);
            data.position = position;
            data.pointerCurrentRaycast = hit;

            if (data.pointerPress != null)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            }

            if (data.dragging)
            {
                data.dragging = false;
                if (data.pointerDrag != null)
                {
                    ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
                }

                if (target != null)
                {
                    ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.dropHandler);
                }
            }
            else if (data.eligibleForClick && data.pointerPress != null && target != null &&
                     data.pointerPress == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target))
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
            }

            data.pointerPress = null;
            data.pointerDrag = null;
            UpdateHover(target);
        }

        /// <summary>
        /// Lets go of every button, dispatching the events that go with letting go. A run that ends
        /// mid-drag would otherwise leave the game's handler waiting for an end that never comes.
        /// </summary>
        public void ReleaseAll()
        {
            for (var button = 0; button < VirtualMouseState.ButtonCount; button++)
            {
                Release(button);
            }

            UpdateHover(null);
        }

        /// <summary>
        /// Every 2D collider actually under the pointer, whether or not a raycast would return it.
        /// A press that reaches nothing draggable is either aimed away from the target or aimed at
        /// one the query skipped, and only the collider list tells the two apart — a trigger is
        /// invisible to ray queries while <c>Physics2D.queriesHitTriggers</c> is off, and a drag
        /// handler sitting on a collider nobody can hit looks exactly like no handler at all.
        /// </summary>
        private static void LogCollidersUnder(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.Log("[Artel] no Camera.main, so the pointer cannot be turned into a world point.");
                return;
            }

            var world = camera.ScreenToWorldPoint(screenPosition);
            var found = new List<Collider2D>();
            Physics2D.OverlapPoint(
                world,
                new ContactFilter2D { useTriggers = true, useLayerMask = false, useDepth = false },
                found);

            var description = new StringBuilder();
            foreach (var collider in found)
            {
                if (description.Length > 0)
                {
                    description.Append(", ");
                }

                description.Append(collider.name);
                description.Append(collider.isTrigger ? " (trigger" : " (solid");
                description.Append(collider.enabled ? "" : ", disabled");
                description.Append(", z=");
                description.Append(collider.transform.position.z.ToString("0.##"));
                description.Append(", layer=");
                description.Append(LayerMask.LayerToName(collider.gameObject.layer));
                description.Append(
                    ExecuteEvents.GetEventHandler<IDragHandler>(collider.gameObject) == null
                        ? ", no drag handler)"
                        : ", HAS drag handler)");
            }

            // The overlap test above only looks at x and y. The raycaster shoots a ray from the
            // camera instead, so a collider it skips is one the ray never reached: outside the clip
            // range, off the event mask, or past a hit limit. These are the values that say which.
            var ray = camera.ScreenPointToRay(screenPosition);
            var alongTheRay = Physics2D.GetRayIntersectionAll(ray, Mathf.Infinity);
            var rayHits = new StringBuilder();
            foreach (var hit in alongTheRay)
            {
                if (rayHits.Length > 0)
                {
                    rayHits.Append(", ");
                }

                rayHits.Append(hit.collider.name);
                rayHits.Append('@');
                rayHits.Append(hit.distance.ToString("0.##"));
            }

            Debug.Log(string.Format(
                "[Artel] colliders under the pointer: {0}. queriesHitTriggers={1}\n" +
                "  camera={2} near={3} far={4} cullingMask={5}\n" +
                "  along the ray: {6}",
                description.Length == 0 ? "none" : description.ToString(),
                Physics2D.queriesHitTriggers,
                camera.name,
                camera.nearClipPlane,
                camera.farClipPlane,
                camera.cullingMask,
                rayHits.Length == 0 ? "none" : rayHits.ToString()));
        }

        /// <summary>
        /// Every hit and the raycaster that produced it. Which raycaster answered is the thing worth
        /// knowing: a Canvas only brings a GraphicRaycaster, which cannot see a SpriteRenderer at
        /// all, so a sprite needs a Physics2DRaycaster on the camera before any of this can reach it.
        /// </summary>
        private string DescribeHits()
        {
            if (raycastResults.Count == 0)
            {
                return "none";
            }

            var description = new StringBuilder();
            foreach (var result in raycastResults)
            {
                if (description.Length > 0)
                {
                    description.Append(", ");
                }

                description.Append(result.gameObject == null ? "<null>" : result.gameObject.name);
                description.Append(" via ");
                description.Append(result.module == null ? "<none>" : result.module.GetType().Name);
            }

            return description.ToString();
        }

        private static string Describe(GameObject target)
        {
            return target == null ? "none" : target.name;
        }

        private static bool HasClearedDragThreshold(
            EventSystem eventSystem, PointerEventData data, Vector2 screenPosition)
        {
            if (!data.useDragThreshold)
            {
                return true;
            }

            var threshold = eventSystem.pixelDragThreshold;
            return (screenPosition - data.pressPosition).sqrMagnitude >= threshold * threshold;
        }

        private void UpdateHover(GameObject target)
        {
            if (hovered == target)
            {
                return;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                hovered = null;
                return;
            }

            var data = HoverData(eventSystem);
            if (hovered != null)
            {
                ExecuteEvents.ExecuteHierarchy(hovered, data, ExecuteEvents.pointerExitHandler);
            }

            hovered = target;
            if (hovered != null)
            {
                ExecuteEvents.ExecuteHierarchy(hovered, data, ExecuteEvents.pointerEnterHandler);
            }
        }

        private GameObject Raycast(EventSystem eventSystem, Vector2 screenPosition, out RaycastResult hit)
        {
            var data = HoverData(eventSystem);
            data.position = screenPosition;

            raycastResults.Clear();
            eventSystem.RaycastAll(data, raycastResults);

            foreach (var result in raycastResults)
            {
                if (result.gameObject != null)
                {
                    hit = result;
                    return result.gameObject;
                }
            }

            hit = default;
            return null;
        }

        /// <summary>
        /// One reusable payload for the raycasts and hover transitions, since those happen every
        /// frame of a drag. It is rebuilt only when the scene brings in a different EventSystem,
        /// which the payload carries and cannot be told about after construction.
        /// </summary>
        private PointerEventData HoverData(EventSystem eventSystem)
        {
            if (hoverData == null || hoverEventSystem != eventSystem)
            {
                hoverData = new PointerEventData(eventSystem);
                hoverEventSystem = eventSystem;
            }

            return hoverData;
        }
    }
}
