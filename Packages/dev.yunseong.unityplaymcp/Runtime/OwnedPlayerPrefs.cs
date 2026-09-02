using UnityEngine;

namespace UnityPlayMcp
{
    /// <summary>
    /// Unity Play MCP가 <c>PlayerPrefs</c>에 쓰는 theme 키를 남기고 저장소를 비운다.
    /// </summary>
    /// <remarks>
    /// <c>reset_game</c>의 <c>clearPlayerPrefs</c>가 이 메서드의 유일한 소비자다.
    /// </remarks>
    internal static class OwnedPlayerPrefs
    {
        /// <summary>커서와 키보드 표시가 공유하는 theme switch.</summary>
        public const string DarkTheme = "UnityPlayMcp.DarkTheme";

        /// <summary>
        /// SDK 자신의 키만 남기고 <c>PlayerPrefs</c> 를 비운다.
        /// </summary>
        /// <remarks>
        /// theme 값을 담아 두고 <c>DeleteAll()</c> 뒤 되쓴다. 게임의 키를 하나씩 지우는 길은
        /// 없다 — <c>PlayerPrefs</c> 는 키를 열거하지 못하므로, 게임이 무엇을 썼는지 SDK 는
        /// 알 수 없다.
        ///
        /// <c>HasKey</c>를 먼저 묻는다. <c>GetInt(DarkTheme, 1)</c>로 읽고
        /// <c>SetInt</c> 로 되쓰면 사용자가 한 번도 만든 적 없는 키가 생기고, 라이트 테마를
        /// 쓰던 사람이 그 순간부터 영영 다크 테마에 고정된다. 나머지 키도 같은 이유로
        /// 빈 문자열을 새로 만들어 두면 안 된다.
        ///
        /// <c>DeleteAll()</c>은 게임의 키만이 아니라 Unity 자신이 쓴 키도 함께 가져간다 —
        /// Standalone 의 <c>Screenmanager Resolution Width</c>/<c>Height</c>,
        /// <c>Screenmanager Fullscreen mode</c>, 분석용 <c>unity.*</c> 항목 같은 것들이다.
        /// 그래서 <c>clearPlayerPrefs</c> 리셋은 다음 실행의 창 크기와 전체화면 선택도 되돌린다.
        /// 이것들을 목록에 넣어 지키지 않는 이유는 이름이 Unity 버전에 묶여 있기 때문이다 —
        /// 낡은 허용 목록은 아무것도 지키지 못하면서 지킨다고 주장하므로, 여기 적어 두는 쪽이 낫다.
        ///
        /// 코루틴이 아니라 동기 메서드인 것도 의도다. 담아 두기와 되쓰기 사이에 프레임
        /// 경계가 생기면 안 된다 — <c>CursorController.Update</c> 와
        /// <c>KeyboardStatusController.Update</c> 는 매 프레임 <c>UnityPlayMcp.DarkTheme</c> 을 읽으므로,
        /// 그 틈에 오버레이가 다크로 번쩍이고 GUI 캔버스를 두 번 다시 만든다.
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
