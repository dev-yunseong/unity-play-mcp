using System;

namespace Artel.Domain
{
    public sealed class ActionInvocation
    {
        public long Sequence { get; }
        public string Tag { get; }
        public string Name { get; }
        public bool Success { get; }
        public object ReturnValue { get; }
        public string ErrorType { get; }
        public string ErrorMessage { get; }
        public DateTimeOffset Timestamp { get; }

        public ActionInvocation(
            long sequence,
            string tag,
            string name,
            bool success,
            object returnValue,
            string errorType,
            string errorMessage,
            DateTimeOffset timestamp)
        {
            Sequence = sequence;
            Tag = tag ?? string.Empty;
            Name = name ?? string.Empty;
            Success = success;
            ReturnValue = returnValue;
            ErrorType = errorType;
            ErrorMessage = errorMessage;
            Timestamp = timestamp;
        }
    }
}
