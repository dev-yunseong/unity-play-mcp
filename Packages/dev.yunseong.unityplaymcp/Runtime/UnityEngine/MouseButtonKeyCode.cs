using global::UnityEngine;

namespace Artel
{
    /// <summary>
    /// <c>KeyCode.Mouse0</c> 은 Unity 에서 마우스 왼쪽 버튼 그 자체다. 키로 온 요청과 버튼으로 온
    /// 요청이 같은 것을 가리킨다는 사실을 여기 한 곳에만 적는다.
    /// </summary>
    /// <remarks>
    /// 읽는 쪽(<see cref="ArtelInput"/>)과 미는 쪽(<c>ActionExecutor</c>)이 각자 대응표를 들면
    /// 언젠가 한쪽만 고쳐져 갈라진다. 그 갈라짐이 곧 "눌렀는데 아무 일도 안 일어난다" 라서,
    /// 대응은 함수 하나로만 존재한다.
    /// </remarks>
    internal static class MouseButtonKeyCode
    {
        /// <summary>
        /// <see cref="VirtualMouseState.ButtonCount"/> 까지만 다룬다. <c>KeyCode.Mouse3</c> 이후는
        /// 가상 마우스에 자리가 없고, 엔진의 <c>OnMouse</c> 계열도 세 개까지만 보낸다.
        /// </summary>
        public static bool TryGetButton(KeyCode key, out int button)
        {
            switch (key)
            {
                case KeyCode.Mouse0:
                    button = 0;
                    return true;

                case KeyCode.Mouse1:
                    button = 1;
                    return true;

                case KeyCode.Mouse2:
                    button = 2;
                    return true;

                default:
                    button = -1;
                    return false;
            }
        }
    }
}
