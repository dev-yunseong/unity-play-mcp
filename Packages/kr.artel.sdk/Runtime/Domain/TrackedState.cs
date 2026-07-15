namespace Artel.Domain
{
    public sealed class TrackedState
    {
        public string Tag { get; }
        public string Name { get; }
        public string Type { get; }
        public object Value { get; }

        public TrackedState(string tag, string name, string type, object value)
        {
            Tag = tag ?? string.Empty;
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            Value = value;
        }
    }
}
