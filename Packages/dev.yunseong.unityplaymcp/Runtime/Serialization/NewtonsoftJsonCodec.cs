using Newtonsoft.Json;

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
