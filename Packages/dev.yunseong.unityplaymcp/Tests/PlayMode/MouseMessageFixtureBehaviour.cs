using System.Collections.Generic;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// 엔진이 커서 아래 오브젝트에 보내는 <c>OnMouse</c> 계열 메시지를 도착한 순서대로 적는다.
    /// </summary>
    /// <remarks>
    /// 핸들러는 관례상 private 이고, 엔진도 <c>SendMessage</c> 로 이름을 보고 부른다. 여기서도
    /// 그대로 private 으로 두어야 SDK 가 엔진과 같은 자리를 두드리는지가 실제로 검증된다.
    /// <para>
    /// <c>OnMouseOver</c> 는 커서가 머무는 매 프레임 오므로 세지 않는다. 프레임 수에 따라 개수가
    /// 달라지는 것을 단언하면 테스트가 러너 속도에 흔들린다.
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
