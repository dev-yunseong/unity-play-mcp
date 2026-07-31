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

    internal sealed class SdkTokenResponseDto
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("expiresAt")]
        public string ExpiresAt { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }
}
