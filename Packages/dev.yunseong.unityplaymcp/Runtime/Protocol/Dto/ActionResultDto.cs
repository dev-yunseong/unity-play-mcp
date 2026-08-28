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
        /// 이 결과가 어느 액션의 것인지.
        /// </summary>
        /// <remarks>
        /// <c>id</c> 가 이미 요청을 가리키지만, 서버가 스스로 보낸 명령의 결과를 기다리는 경우 — 화면이 스캔 완료를 기다리는
        /// <c>scan_evidence</c> 가 그렇다 — 그쪽은 액션 이름으로 짝을 맞춘다.
        ///
        /// <c>returnValue</c> 와 같은 이유로 <c>Ignore</c> 다. 채우지 않는 액션의 결과는 이 필드가 생기기 전과 바이트 하나
        /// 다르지 않다.
        /// </remarks>
        [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]
        public string Action { get; set; }

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

        /// <summary>어느 액션의 결과인지 밝힌 성공.</summary>
        public static ActionResultDto Success(int id, string action, object returnValue)
        {
            return new ActionResultDto
            {
                Id = id,
                IsSuccess = true,
                Error = string.Empty,
                Action = action,
                ReturnValue = returnValue
            };
        }

        /// <summary>어느 액션의 결과인지 밝힌 실패.</summary>
        public static ActionResultDto Failure(int id, string action, string error)
        {
            return new ActionResultDto { Id = id, IsSuccess = false, Error = error, Action = action };
        }
    }
}
