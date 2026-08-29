using System;
using System.Globalization;
using UnityEngine;

namespace Artel.Auth
{
    /// <summary>
    /// 브라우저 로그인으로 받은 SDK 토큰과, 그 토큰으로 고른 프로젝트·인스턴스를 담아 둔다.
    /// </summary>
    /// <remarks>
    /// 두 토큰만 <see cref="ArtelSecretStore"/>로 간다. 만료 시각·표시 이름·프로젝트·인스턴스는
    /// 그 자체로 아무것도 열지 못하므로 PlayerPrefs에 그대로 둔다 — 옮기면 값 하나마다 키체인
    /// 왕복이 붙기만 하고 지키는 것은 없다.
    /// </remarks>
    internal static class ArtelSdkSession
    {
        private const string TokenSecretKey = ArtelOwnedPlayerPrefs.SdkTokenSecret;
        private const string ExpiresAtPlayerPrefsKey = ArtelOwnedPlayerPrefs.SdkTokenExpiresAt;
        private const string RefreshTokenSecretKey = ArtelOwnedPlayerPrefs.SdkRefreshTokenSecret;
        private const string RefreshExpiresAtPlayerPrefsKey = ArtelOwnedPlayerPrefs.SdkRefreshTokenExpiresAt;
        private const string DisplayNamePlayerPrefsKey = ArtelOwnedPlayerPrefs.SdkDisplayName;
        private const string ProjectIdPlayerPrefsKey = ArtelOwnedPlayerPrefs.ProjectId;
        private const string InstanceIdPlayerPrefsKey = ArtelOwnedPlayerPrefs.InstanceId;
        private const string GameBuildIdPlayerPrefsKey = ArtelOwnedPlayerPrefs.GameBuildId;

        /// <summary>로그인한 사람의 표시 이름. 없으면 빈 문자열.</summary>
        public static string DisplayName
        {
            get { return PlayerPrefs.GetString(DisplayNamePlayerPrefsKey, string.Empty); }
        }

        /// <summary>
        /// 저장된 토큰을 읽는다. 만료 시각이 지났으면 없는 것으로 본다.
        /// </summary>
        /// <remarks>
        /// 만료를 여기서 걸러야 하는 이유: 만료된 토큰으로도 요청은 나가고 401만 돌아온다.
        /// 그러면 사용자는 "등록 실패"를 한 번 본 뒤에야 로그인 화면으로 돌아온다.
        ///
        /// 만료됐더라도 refresh 토큰이 살아 있으면 세션을 지우지 않는다. 지우면 다시 받을
        /// 수단까지 함께 사라져 브라우저 로그인 외에는 길이 없다.
        /// </remarks>
        public static bool TryLoadToken(out string token)
        {
            if (!ArtelSecretStore.TryLoad(TokenSecretKey, out var storedToken))
            {
                token = string.Empty;
                return false;
            }

            if (HasExpired(ExpiresAtPlayerPrefsKey))
            {
                if (!TryLoadRefreshToken(out _))
                {
                    Clear();
                }

                token = string.Empty;
                return false;
            }

            token = storedToken.Trim();
            return true;
        }

        /// <summary>토큰이 없으면 빈 문자열. 코루틴 밖에서 헤더를 채울 때 쓴다.</summary>
        public static string LoadToken()
        {
            return TryLoadToken(out var token) ? token : string.Empty;
        }

        public static void SaveToken(string token, string expiresAt, string displayName)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("SDK token is required.", nameof(token));
            }

            ArtelSecretStore.Save(TokenSecretKey, token.Trim());
            PlayerPrefs.SetString(ExpiresAtPlayerPrefsKey, expiresAt ?? string.Empty);
            PlayerPrefs.SetString(DisplayNamePlayerPrefsKey, displayName ?? string.Empty);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 재발급용 토큰. 이것도 만료되면 남은 길은 브라우저 재로그인뿐이다.
        /// </summary>
        public static bool TryLoadRefreshToken(out string refreshToken)
        {
            if (!ArtelSecretStore.TryLoad(RefreshTokenSecretKey, out refreshToken))
            {
                return false;
            }

            refreshToken = refreshToken.Trim();

            if (HasExpired(RefreshExpiresAtPlayerPrefsKey))
            {
                refreshToken = string.Empty;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 로그인 응답의 refresh 토큰. 회전하지 않으므로 이후 재발급에서 다시 저장할 일은 없다.
        /// </summary>
        public static void SaveRefreshToken(string refreshToken, string expiresAt)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException("Refresh token is required.", nameof(refreshToken));
            }

            ArtelSecretStore.Save(RefreshTokenSecretKey, refreshToken.Trim());
            PlayerPrefs.SetString(RefreshExpiresAtPlayerPrefsKey, expiresAt ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static bool TryLoadProjectId(out string projectId)
        {
            return TryLoadNonEmpty(ProjectIdPlayerPrefsKey, out projectId);
        }

        public static void SaveProjectId(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id is required.", nameof(projectId));
            }

            PlayerPrefs.SetString(ProjectIdPlayerPrefsKey, projectId.Trim());
            PlayerPrefs.Save();
        }

        public static bool TryLoadInstanceId(out string instanceId)
        {
            return TryLoadNonEmpty(InstanceIdPlayerPrefsKey, out instanceId);
        }

        public static string LoadInstanceId()
        {
            return TryLoadInstanceId(out var instanceId) ? instanceId : string.Empty;
        }

        /// <summary>
        /// 등록 응답의 instanceId. WebSocket 핸드셰이크와 캡처 티켓이 이 값을 싣는다.
        /// </summary>
        public static void SaveInstanceId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("Instance id is required.", nameof(instanceId));
            }

            PlayerPrefs.SetString(InstanceIdPlayerPrefsKey, instanceId.Trim());
            PlayerPrefs.Save();
        }

        public static bool TryLoadGameBuildId(out string gameBuildId)
        {
            return TryLoadNonEmpty(GameBuildIdPlayerPrefsKey, out gameBuildId);
        }

        public static string LoadGameBuildId()
        {
            return TryLoadGameBuildId(out var gameBuildId) ? gameBuildId : string.Empty;
        }

        /// <summary>
        /// 등록 응답의 gameBuildId. 근거 문서가 어느 빌드에 붙는지를 이 값이 정한다.
        /// </summary>
        /// <remarks>
        /// instanceId 와 짝이지만 축이 다르다. WebSocket 세션은 살아 있는 인스턴스로 묶이고 근거 문서는 빌드로 묶이는데,
        /// 빌드에서 인스턴스로 가는 길이 서버에 없다. 그래서 등록 응답을 받은 이 자리가 두 축을 함께 쥐고 있는 유일한 순간이고,
        /// 여기서 붙들지 않으면 SDK 는 제 문서를 어디에 올릴지 영영 모른다.
        /// </remarks>
        public static void SaveGameBuildId(string gameBuildId)
        {
            if (string.IsNullOrWhiteSpace(gameBuildId))
            {
                throw new ArgumentException("Game build id is required.", nameof(gameBuildId));
            }

            PlayerPrefs.SetString(GameBuildIdPlayerPrefsKey, gameBuildId.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 로그아웃. 프로젝트와 인스턴스도 함께 지운다 — 토큰이 바뀌면 다른 사용자일 수 있고,
        /// 그 사람이 접근할 수 없는 프로젝트를 그대로 들고 있으면 404만 반복한다.
        /// </summary>
        public static void Clear()
        {
            ArtelSecretStore.Delete(TokenSecretKey);
            ArtelSecretStore.Delete(RefreshTokenSecretKey);
            PlayerPrefs.DeleteKey(ExpiresAtPlayerPrefsKey);
            PlayerPrefs.DeleteKey(RefreshExpiresAtPlayerPrefsKey);
            PlayerPrefs.DeleteKey(DisplayNamePlayerPrefsKey);
            PlayerPrefs.DeleteKey(ProjectIdPlayerPrefsKey);
            PlayerPrefs.DeleteKey(InstanceIdPlayerPrefsKey);
            PlayerPrefs.DeleteKey(GameBuildIdPlayerPrefsKey);
            PlayerPrefs.Save();
        }

        // 읽지 못하는 만료 시각은 만료로 치지 않는다. 서버가 형식을 바꾸면 로그인이 통째로
        // 막히는 쪽보다, 401을 한 번 받고 로그인 화면으로 돌아가는 쪽이 낫다.
        private static bool HasExpired(string playerPrefsKey)
        {
            var storedExpiresAt = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            if (!DateTimeOffset.TryParse(
                    storedExpiresAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var expiresAt))
            {
                return false;
            }

            return expiresAt <= DateTimeOffset.UtcNow;
        }

        private static bool TryLoadNonEmpty(string playerPrefsKey, out string value)
        {
            var storedValue = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(storedValue))
            {
                value = string.Empty;
                return false;
            }

            value = storedValue.Trim();
            return true;
        }
    }
}
