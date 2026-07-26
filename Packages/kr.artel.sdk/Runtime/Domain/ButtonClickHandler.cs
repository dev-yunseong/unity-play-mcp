namespace Artel.Domain
{
    /// <summary>
    /// One call wired into a Button's onClick from the inspector: which object it runs on and
    /// which method it calls. Listeners added in code with AddListener are not here — Unity
    /// keeps those in a delegate it never exposes.
    /// </summary>
    public sealed class ButtonClickHandler
    {
        /// <summary>Name of the object the call runs on, or null when the reference is missing.</summary>
        public string Target { get; }

        /// <summary>Full type name of that object, or null when the reference is missing.</summary>
        public string TargetType { get; }

        /// <summary>Name of the method the call invokes.</summary>
        public string Method { get; }

        public ButtonClickHandler(string target, string targetType, string method)
        {
            Target = target;
            TargetType = targetType;
            Method = method;
        }
    }
}
