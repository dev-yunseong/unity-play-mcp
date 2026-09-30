using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityPlayMcp.Protocol
{
    /// <summary>
    /// 액션 params 안의 options 오브젝트를 필드 사전으로 읽는다.
    /// </summary>
    /// <remarks>
    /// 실제 wire 에서는 <c>JObject</c> 가 온다. <c>List&lt;object&gt;</c> 인 params 에 Newtonsoft 가
    /// <c>JObject</c> 를 넣는데, 이는 <c>IDictionary&lt;string, object&gt;</c> 가 아니라서 그 캐스트는
    /// 언제나 null 이 된다.
    ///
    /// 테스트는 코덱을 거치지 않고 <c>Dictionary&lt;string, object&gt;</c> 를 넘긴다. 두 모양을 모두 받는다.
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
                    // JValue 는 벗겨서 bool/long/double/string 으로 넘긴다.
                    // 배열과 중첩 오브젝트는 토큰으로 두고 읽는 쪽이 판정한다.
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
