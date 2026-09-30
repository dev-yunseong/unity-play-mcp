using global::UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// <c>KeyCode.Mouse0</c>~<c>Mouse2</c> 와 마우스 버튼 번호의 대응. 이 대응은 여기에만 둔다.
    /// </summary>
    /// <remarks>
    /// <see cref="VirtualInput"/> 과 <c>ActionExecutor</c> 가 각자 대응표를 두면 한쪽만 고쳐져
    /// 눌러도 반응이 없게 된다.
    /// </remarks>
    internal static class MouseButtonKeyCode
    {
        /// <summary>
        /// <see cref="VirtualMouseState.ButtonCount"/> 까지만 다룬다. <c>KeyCode.Mouse3</c> 이후는 가상 마우스가 지원하지 않는다.
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
