using System;

namespace Artel.Tracking
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class ArtelStateAttribute : Attribute
    {
        public string Tag { get; }

        public ArtelStateAttribute(string tag)
        {
            Tag = tag ?? string.Empty;
        }
    }
}
