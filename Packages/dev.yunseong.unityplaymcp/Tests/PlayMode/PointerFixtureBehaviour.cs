using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Artel.Tests
{
    /// <summary>
    /// Records the uGUI pointer events it receives, in the order they arrive. The order is the
    /// point: a drag that reports its steps out of sequence is not a drag any game would follow.
    /// </summary>
    public sealed class PointerFixtureBehaviour :
        MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler
    {
        public List<string> Events { get; } = new List<string>();

        public List<Vector2> DragPositions { get; } = new List<Vector2>();

        public void OnPointerDown(PointerEventData eventData)
        {
            Events.Add("down");
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Events.Add("up");
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Events.Add("click");
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            Events.Add("beginDrag");
        }

        public void OnDrag(PointerEventData eventData)
        {
            Events.Add("drag");
            DragPositions.Add(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Events.Add("endDrag");
        }

        public void OnDrop(PointerEventData eventData)
        {
            Events.Add("drop");
        }
    }
}
