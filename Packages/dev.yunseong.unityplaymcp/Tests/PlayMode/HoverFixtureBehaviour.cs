using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 받은 uGUI hover 와 누름 이벤트를 도착 순서대로 기록한다.
    /// </summary>
    /// <remarks>
    /// <see cref="PointerFixtureBehaviour"/> 를 쓰는 test 는 이벤트 순서를 통째로 비교하므로 enter/exit 를 따로 둔다.
    /// </remarks>
    public sealed class HoverFixtureBehaviour :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerClickHandler
    {
        public List<string> Events { get; } = new List<string>();

        /// <summary>enter 를 받을 때 게임이 할 일이다. 포인터 이동 중 대상이 바뀌는 경우를 만든다.</summary>
        public System.Action Entered { get; set; }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Events.Add("enter");
            Entered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Events.Add("exit");
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Events.Add("down");
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Events.Add("click");
        }
    }
}
