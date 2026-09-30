using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp
{
    /// <summary>
    /// Sends uGUI pointer events for the agent's pointer. Unity's input module reads only the
    /// physical mouse; games that poll <c>Input</c> are covered by the virtual mouse state instead.
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
                    // Keeps a drag from also firing the click handler it started on.
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
                // Otherwise indistinguishable from a press that hit nothing.
                Debug.LogWarning("[Unity Play MCP] mouse_down found no EventSystem, so no uGUI element can answer it.");
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
                // The handler is often a parent of the hit graphic, e.g. a Button.
                data.pointerPress = ExecuteEvents.ExecuteHierarchy(
                    target, data, ExecuteEvents.pointerDownHandler)
                    ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
                data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            }

            if (data.pointerDrag != null)
            {
                // The handler may clear useDragThreshold (ScrollRect does); the check below respects it.
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
            }

            // One line per press tells a drag that did nothing apart from one never delivered.
            Debug.Log(string.Format(
                "[Unity Play MCP] mouse_down at ({0}, {1}) over {2} hits: {3}. press={4} drag={5}",
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
        /// 화면 좌표에서 uGUI raycast 의 맨 위 오브젝트다. EventSystem 이나 hit 이 없으면 null 이다.
        /// </summary>
        /// <remarks>
        /// hover 상태를 바꾸지 않아야 한다. targeting 은 커서를 옮기기 전에 여러 후보 좌표를 시험하므로,
        /// 그때마다 <c>pointerEnter</c>/<c>pointerExit</c> 가 나가면 게임이 가짜 hover 를 본다.
        /// </remarks>
        public GameObject GraphicUnder(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return null;
            }

            return Raycast(eventSystem, screenPosition, out _);
        }

        /// <summary>
        /// Releases every button with its release events, so a run ending mid-drag still sends endDrag.
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
        /// Logs every 2D collider under the pointer, whether or not a raycast returns it. This tells a
        /// missed aim apart from a collider the query skipped, e.g. a trigger while
        /// <c>Physics2D.queriesHitTriggers</c> is off.
        /// </summary>
        private static void LogCollidersUnder(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.Log("[Unity Play MCP] no Camera.main, so the pointer cannot be turned into a world point.");
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

            // The overlap test ignores depth; the raycaster's ray can miss a collider through clip
            // range, event mask, or hit limit. These values show which.
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
                "[Unity Play MCP] colliders under the pointer: {0}. queriesHitTriggers={1}\n" +
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
        /// Every hit and its raycaster. A GraphicRaycaster cannot see a SpriteRenderer; sprites need a
        /// Physics2DRaycaster on the camera.
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
        /// Reused payload for per-frame raycasts and hover transitions. Rebuilt only when the
        /// EventSystem changes, since the payload is bound to it at construction.
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
