using System.Collections.Generic;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// SDK 가 <c>PlayerPrefs</c> 에 쓰는 키를 한곳에 모아 두고, 그것들만 남기고 저장소를 비운다.
    /// </summary>
    /// <remarks>
    /// <c>reset_game</c> 의 <c>clearPlayerPrefs</c> 가 이 목록의 유일한 소비자다. 저장소를
    /// 통째로 지우면 게임의 세이브뿐 아니라 SDK 의 로그인·프로젝트 선택·인스턴스 등록까지
    /// 함께 사라지고, 리셋을 시킨 세션이 자기 자신을 끊는다.
    ///
    /// Unity <c>PlayerPrefs</c> 는 키를 열거하지 못한다. 그래서 "SDK 가 쓰는 모든 키를 이
    /// 목록이 담고 있다"를 코드로 확인할 방법이 없고, 여기에 키를 하나 빠뜨리면 조용히
    /// 지워진다. 새 키를 쓰는 코드를 추가할 때는 이 목록에도 함께 적는다.
    /// </remarks>
    internal static class ArtelOwnedPlayerPrefs
    {
        /// <summary>ArtelSdkIdentity 가 만드는 이 설치본의 식별자.</summary>
        public const string SdkId = "Artel.SdkId";

        /// <summary>오버레이·커서·키보드 표시가 공유하는 테마 스위치. 유일한 int 키다.</summary>
        public const string DarkTheme = "Artel.DarkTheme";

        // 아래 여덟 개는 ArtelSdkSession 이 쓴다. 두 개는 보안 저장소를 거치는 값이라
        // 이름이 Secret 으로 끝난다.
        public const string SdkTokenSecret = "Artel.SdkToken";
        public const string SdkTokenExpiresAt = "Artel.SdkTokenExpiresAt";
        public const string SdkRefreshTokenSecret = "Artel.SdkRefreshToken";
        public const string SdkRefreshTokenExpiresAt = "Artel.SdkRefreshTokenExpiresAt";
        public const string SdkDisplayName = "Artel.SdkDisplayName";
        public const string ProjectId = "Artel.ProjectId";
        public const string InstanceId = "Artel.InstanceId";
        public const string GameBuildId = "Artel.GameBuildId";

        /// <summary>문자열로 저장되는 키 전부.</summary>
        /// <remarks>
        /// 두 secret 키를 플랫폼 <c>#if</c> 없이 무조건 적어 둔다.
        /// <c>ArtelSecretStore.CreatePlatformStore()</c> 는 macOS 에서 Keychain, Windows 에서
        /// DPAPI, 나머지에서 <c>PlayerPrefsSecretStore</c> 를 고른다. 조건부로 적으면 컴파일
        /// 플랫폼과 실행 플랫폼이 갈리는 순간 목록이 어긋나는데, 무조건 적어 두면
        /// <c>HasKey</c> 가 판정한다 — macOS/Windows 에서는 그냥 없는 키이고,
        /// <c>PlayerPrefs.DeleteAll()</c> 은 Keychain 이나 DPAPI 에 닿지도 못한다.
        /// </remarks>
        public static readonly IReadOnlyList<string> StringKeys = new[]
        {
            SdkId,
            SdkTokenSecret,
            SdkTokenExpiresAt,
            SdkRefreshTokenSecret,
            SdkRefreshTokenExpiresAt,
            SdkDisplayName,
            ProjectId,
            InstanceId,
            GameBuildId
        };

        /// <summary>정수로 저장되는 키 전부.</summary>
        /// <remarks>
        /// <c>PlayerPrefs</c> 는 int/float/string 이 하나의 이름 공간을 공유하지만 타입은
        /// 저장된 대로 남는다. <c>Artel.DarkTheme</c> 을 <c>GetString</c> 으로 읽으면 왕복하지
        /// 않으므로, 문자열 목록에 섞지 않고 따로 둔다.
        /// </remarks>
        public static readonly IReadOnlyList<string> IntKeys = new[]
        {
            DarkTheme
        };

        /// <summary>
        /// SDK 자신의 키만 남기고 <c>PlayerPrefs</c> 를 비운다.
        /// </summary>
        /// <remarks>
        /// 담아 두기 → <c>DeleteAll()</c> → 되쓰기 순서다. 게임의 키를 하나씩 지우는 길은
        /// 없다 — <c>PlayerPrefs</c> 는 키를 열거하지 못하므로, 게임이 무엇을 썼는지 SDK 는
        /// 알 수 없다.
        ///
        /// 읽기마다 <c>HasKey</c> 를 먼저 묻는다. <c>GetInt(DarkTheme, 1)</c> 로 읽고
        /// <c>SetInt</c> 로 되쓰면 사용자가 한 번도 만든 적 없는 키가 생기고, 라이트 테마를
        /// 쓰던 사람이 그 순간부터 영영 다크 테마에 고정된다. 나머지 키도 같은 이유로
        /// 빈 문자열을 새로 만들어 두면 안 된다.
        ///
        /// <c>DeleteAll()</c> 은 게임의 키만이 아니라 Unity 자신이 쓴 키도 함께 가져간다 —
        /// Standalone 의 <c>Screenmanager Resolution Width</c>/<c>Height</c>,
        /// <c>Screenmanager Fullscreen mode</c>, 분석용 <c>unity.*</c> 항목 같은 것들이다.
        /// 그래서 <c>clearPlayerPrefs</c> 리셋은 다음 실행의 창 크기와 전체화면 선택도 되돌린다.
        /// 이것들을 목록에 넣어 지키지 않는 이유는 이름이 Unity 버전에 묶여 있기 때문이다 —
        /// 낡은 허용 목록은 아무것도 지키지 못하면서 지킨다고 주장하므로, 여기 적어 두는 쪽이 낫다.
        ///
        /// 코루틴이 아니라 동기 메서드인 것도 의도다. 담아 두기와 되쓰기 사이에 프레임
        /// 경계가 생기면 안 된다 — <c>CursorController.Update</c> 와
        /// <c>KeyboardStatusController.Update</c> 는 매 프레임 <c>Artel.DarkTheme</c> 을 읽으므로,
        /// 그 틈에 오버레이가 다크로 번쩍이고 GUI 캔버스를 두 번 다시 만든다.
        /// </remarks>
        public static void DeleteAllExceptOwn()
        {
            var stringValues = new string[StringKeys.Count];
            var hadStringKey = new bool[StringKeys.Count];
            for (var index = 0; index < StringKeys.Count; index++)
            {
                hadStringKey[index] = PlayerPrefs.HasKey(StringKeys[index]);
                if (hadStringKey[index])
                {
                    stringValues[index] = PlayerPrefs.GetString(StringKeys[index]);
                }
            }

            var intValues = new int[IntKeys.Count];
            var hadIntKey = new bool[IntKeys.Count];
            for (var index = 0; index < IntKeys.Count; index++)
            {
                hadIntKey[index] = PlayerPrefs.HasKey(IntKeys[index]);
                if (hadIntKey[index])
                {
                    intValues[index] = PlayerPrefs.GetInt(IntKeys[index]);
                }
            }

            PlayerPrefs.DeleteAll();

            for (var index = 0; index < StringKeys.Count; index++)
            {
                if (hadStringKey[index])
                {
                    PlayerPrefs.SetString(StringKeys[index], stringValues[index]);
                }
            }

            for (var index = 0; index < IntKeys.Count; index++)
            {
                if (hadIntKey[index])
                {
                    PlayerPrefs.SetInt(IntKeys[index], intValues[index]);
                }
            }

            PlayerPrefs.Save();
        }
    }
}
