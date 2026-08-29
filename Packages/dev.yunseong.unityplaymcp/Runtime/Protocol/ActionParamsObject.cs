using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Artel.Protocol
{
    /// <summary>
    /// 액션 params 안의 options 오브젝트를 필드 사전으로 읽는다.
    /// </summary>
    /// <remarks>
    /// 두 가지 모양을 모두 받는 것이 이 클래스가 존재하는 이유다.
    ///
    /// 실제 wire 에서는 <c>JObject</c> 가 온다. <c>NewtonsoftJsonCodec</c> 이 기본
    /// <c>JsonSerializerSettings</c> 를 쓰고 <c>ActionRequestDto.Parameters</c> 가
    /// <c>List&lt;object&gt;</c> 이므로, Newtonsoft 는 그 자리에 <c>JObject</c> 를 넣는다.
    /// <c>JObject</c> 는 <c>IDictionary&lt;string, JToken&gt;</c> 를 구현하지
    /// <c>IDictionary&lt;string, object&gt;</c> 를 구현하지 않는다. 그래서 후자로 캐스트하면
    /// 서버가 무엇을 보냈든 언제나 null 이 되고, options 는 통째로 사라진다.
    ///
    /// 테스트에서는 손으로 만든 <c>Dictionary&lt;string, object&gt;</c> 가 온다. 코덱을 거치지
    /// 않고 리더만 부르는 테스트가 그렇게 쓰고 있고, 그 입구도 계속 통해야 리더 하나를 두
    /// 방향에서 같은 규칙으로 검사할 수 있다.
    /// </remarks>
    internal static class ActionParamsObject
    {
        public static bool TryRead(object value, out IReadOnlyDictionary<string, object> fields)
        {
            // Dictionary<string, object> 는 읽기 전용 인터페이스도 함께 구현하므로 그대로 쓴다.
            if (value is IReadOnlyDictionary<string, object> readOnlyFields)
            {
                fields = readOnlyFields;
                return true;
            }

            if (value is JObject jsonObject)
            {
                var readFields = new Dictionary<string, object>(jsonObject.Count);
                foreach (var property in jsonObject.Properties())
                {
                    // JValue 는 스칼라이므로 벗겨서 bool/long/double/string 을 그대로 넘긴다.
                    // 배열이나 중첩 오브젝트는 토큰인 채로 두고, 그것을 읽을 리더가 판정한다.
                    readFields[property.Name] = property.Value is JValue jsonValue
                        ? jsonValue.Value
                        : property.Value;
                }

                fields = readFields;
                return true;
            }

            fields = null;
            return false;
        }
    }
}
