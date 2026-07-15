using System;

namespace Artel.Domain
{
    public sealed class ActionInvocation
    {
        public string Tag { get; }
        public string Name { get; }
        public object ReturnValue { get; }
        public DateTimeOffset Timestamp { get; }

        public ActionInvocation(string tag, string name, object returnValue, DateTimeOffset timestamp)
        {
            Tag = tag ?? string.Empty;
            Name = name ?? string.Empty;
            ReturnValue = returnValue;
            Timestamp = timestamp;
        }
    }
}
