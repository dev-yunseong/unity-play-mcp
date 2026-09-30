using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 받은 uGUI hover 와 누름 이벤트를 도착 순서대로 적는다.
    /// </summary>
    /// <remarks>
    /// <see cref="PointerFixtureBehaviour"/> 에 enter/exit 를 더하지 않는 것은 그것을 쓰는 test 들이 이벤트 순서를 통째로 비교하기
    /// 때문이다. 거기에 hover 가 끼면 클릭 test 가 hover 때문에 깨진다.
    /// </remarks>
    public sealed class HoverFixtureBehaviour :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerClickHandler
    {
        public List<string> Events { get; } = new List<string>();

        /// <summary>enter 를 받은 그 순간 게임이 하는 일. 포인터가 도착하는 사이 대상이 바뀌는 경우를 만든다.</summary>
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
