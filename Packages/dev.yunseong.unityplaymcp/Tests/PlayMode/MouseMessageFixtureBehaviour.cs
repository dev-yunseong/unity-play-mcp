using System.Collections.Generic;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 엔진이 커서 아래 오브젝트에 보내는 <c>OnMouse</c> 메시지를 도착 순서대로 기록한다.
    /// </summary>
    /// <remarks>
    /// 엔진은 <c>SendMessage</c> 로 이름을 보고 부르므로 handler 를 private 으로 두어 SDK 가 같은 방식으로
    /// 부르는지 검증한다.
    /// <para>
    /// <c>OnMouseOver</c> 는 매 프레임 오므로 기록하지 않는다. 개수가 runner 속도에 따라 달라진다.
    /// </para>
    /// </remarks>
    public sealed class MouseMessageFixtureBehaviour : MonoBehaviour
    {
        public List<string> Messages { get; } = new List<string>();

        public int OverCount { get; private set; }

        private void OnMouseEnter()
        {
            Messages.Add("enter");
        }

        private void OnMouseOver()
        {
            OverCount++;
        }

        private void OnMouseExit()
        {
            Messages.Add("exit");
        }

        private void OnMouseDown()
        {
            Messages.Add("down");
        }

        private void OnMouseDrag()
        {
            Messages.Add("drag");
        }

        private void OnMouseUp()
        {
            Messages.Add("up");
        }

        private void OnMouseUpAsButton()
        {
            Messages.Add("upAsButton");
        }
    }
}
