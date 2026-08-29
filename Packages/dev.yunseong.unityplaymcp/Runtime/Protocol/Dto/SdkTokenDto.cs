using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 브라우저가 돌려준 일회용 code를 SDK 토큰으로 바꿔 달라는 요청.
    /// </summary>
    internal sealed class SdkTokenRequestDto
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("codeVerifier")]
        public string CodeVerifier { get; set; }
    }

    /// <summary>
    /// 만료된 SDK 토큰을 refresh 토큰으로 다시 받아 달라는 요청.
    /// </summary>
    internal sealed class SdkRefreshRequestDto
    {
        [JsonProperty("refreshToken")]
        public string RefreshToken { get; set; }
    }

    /// <summary>
    /// 로그인과 재발급이 함께 쓴다. 재발급 응답에는 토큰과 만료 시각만 있고 나머지는 비어 온다.
    /// </summary>
    internal sealed class SdkTokenResponseDto
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("expiresAt")]
        public string ExpiresAt { get; set; }

        [JsonProperty("refreshToken")]
        public string RefreshToken { get; set; }

        [JsonProperty("refreshExpiresAt")]
        public string RefreshExpiresAt { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }
}
