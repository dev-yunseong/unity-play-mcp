using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace UnityPlayMcp.Protocol.Dto
{
    public sealed class ActionResultDto
    {
        [Preserve, JsonProperty("id")]
        public int Id { get; set; }

        [Preserve, JsonProperty("success")]
        public bool IsSuccess { get; set; }

        [Preserve, JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>
        /// What the action produced, for the actions that produce something.
        /// </summary>
        /// <remarks>
        /// Omitted when absent. The relay passes the payload through untouched, so an action can
        /// add a field without a protocol version.
        /// </remarks>
        [Preserve, JsonProperty("returnValue", NullValueHandling = NullValueHandling.Ignore)]
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
