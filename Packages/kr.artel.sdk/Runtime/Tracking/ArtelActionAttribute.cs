using System;

namespace Artel.Tracking
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ArtelActionAttribute : Attribute
    {
        public string Tag { get; }

        public ArtelActionAttribute(string tag)
        {
            Tag = tag ?? string.Empty;
        }
    }
}
