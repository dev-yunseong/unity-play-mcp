using UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// Unity Play MCP 의 theme 키만 남기고 <c>PlayerPrefs</c> 를 비운다.
    /// </summary>
    /// <remarks>
    /// <c>reset_game</c> 의 <c>clearPlayerPrefs</c> 만 사용한다.
    /// </remarks>
    internal static class OwnedPlayerPrefs
    {
        /// <summary>커서와 키보드 표시가 공유하는 theme 키다.</summary>
        public const string DarkTheme = "UnityPlayMcp.DarkTheme";

        /// <summary>
        /// SDK 자신의 키만 남기고 <c>PlayerPrefs</c> 를 비운다.
        /// </summary>
        /// <remarks>
        /// <c>PlayerPrefs</c> 는 키를 열거하지 못하므로 theme 값을 보관했다가 <c>DeleteAll()</c> 뒤 되쓴다.
        ///
        /// <c>HasKey</c> 를 먼저 확인한다. 없던 키를 기본값으로 되쓰면 라이트 테마 사용자가 다크 테마에 고정된다.
        ///
        /// <c>DeleteAll()</c> 은 Unity 가 쓴 키(<c>Screenmanager Resolution Width</c> 등)도 지우므로
        /// 다음 실행의 창 크기와 전체화면 설정도 초기화된다. 키 이름이 Unity 버전마다 달라 보존 목록을 두지 않는다.
        ///
        /// 동기 메서드여야 한다. 보관과 되쓰기 사이에 프레임이 지나면 매 프레임 theme 을 읽는
        /// <c>CursorController</c> 와 <c>KeyboardStatusController</c> 가 잠깐 다크로 바뀌고 GUI 를 다시 만든다.
        /// </remarks>
        public static void DeleteAllExceptOwn()
        {
            var hadDarkTheme = PlayerPrefs.HasKey(DarkTheme);
            var darkTheme = hadDarkTheme ? PlayerPrefs.GetInt(DarkTheme) : default;

            PlayerPrefs.DeleteAll();

            if (hadDarkTheme)
            {
                PlayerPrefs.SetInt(DarkTheme, darkTheme);
            }

            PlayerPrefs.Save();
        }
    }
}
