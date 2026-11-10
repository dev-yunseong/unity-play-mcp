using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityPlayMcp.Serialization
{
    internal sealed class NewtonsoftJsonCodec : IJsonCodec
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
            Formatting = Formatting.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include
        };

        internal static JToken ToToken(object value) => JToken.FromObject(value, JsonSerializer.Create(Settings));

        public string Serialize<T>(T value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}
