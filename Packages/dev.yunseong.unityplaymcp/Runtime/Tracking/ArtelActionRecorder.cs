using System;
using System.ComponentModel;
using System.Threading;

namespace Artel.Tracking
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ArtelActionRecorder
    {
        public static ActionInvocationBuffer GetOrCreate(ref ActionInvocationBuffer buffer)
        {
            var current = buffer;
            if (current != null)
            {
                return current;
            }

            var created = new ActionInvocationBuffer();
            return Interlocked.CompareExchange(ref buffer, created, null) ?? created;
        }

        public static void RecordSuccess(IArtelActionSource source, string tag, string methodName, object returnValue)
        {
            source.ArtelActionBuffer.Record(tag, methodName, true, returnValue, null, null);
        }

        public static void RecordFailure(IArtelActionSource source, string tag, string methodName, Exception exception)
        {
            source.ArtelActionBuffer.Record(
                tag, methodName, false, null, exception.GetType().FullName, exception.Message);
        }
    }
}
