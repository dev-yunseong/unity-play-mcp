namespace UnityPlayMcp.Serialization
{
    internal interface IJsonCodec
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json);
    }
}
