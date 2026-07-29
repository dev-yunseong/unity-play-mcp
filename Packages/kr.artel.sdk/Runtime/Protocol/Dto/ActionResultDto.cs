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

        /// <summary>
        /// What the action produced, for the actions that produce something.
        /// </summary>
        /// <remarks>
        /// Omitted from the wire when absent, so results with nothing to return keep exactly the
        /// shape they had before this field existed. The relay parses the payload as a tree and
        /// passes it through untouched, which is what lets one action add a field without a
        /// protocol version.
        /// </remarks>
        [JsonProperty("returnValue", NullValueHandling = NullValueHandling.Ignore)]
        public object ReturnValue { get; set; }

        public static ActionResultDto Success(int id)
        {
            return new ActionResultDto { Id = id, IsSuccess = true, Error = string.Empty };
        }

        public static ActionResultDto Success(int id, object returnValue)
        {
            return new ActionResultDto
            {
                Id = id,
                IsSuccess = true,
                Error = string.Empty,
                ReturnValue = returnValue
            };
        }

        public static ActionResultDto Failure(int id, string error)
        {
            return new ActionResultDto { Id = id, IsSuccess = false, Error = error };
        }
    }
}
