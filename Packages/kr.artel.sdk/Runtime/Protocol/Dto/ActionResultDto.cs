using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class ActionResultDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("success")]
        public bool IsSuccess { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        public static ActionResultDto Success(int id)
        {
            return new ActionResultDto { Id = id, IsSuccess = true, Error = string.Empty };
        }

        public static ActionResultDto Failure(int id, string error)
        {
            return new ActionResultDto { Id = id, IsSuccess = false, Error = error };
        }
    }
}
