using System;
using System.Collections.Generic;
using Artel.Domain;

namespace Artel.Tracking
{
    public sealed class ActionInvocationBuffer
    {
        private const int DefaultCapacity = 256;
        private readonly object gate = new object();
        private readonly List<ActionInvocation> actions = new List<ActionInvocation>();
        private readonly int capacity;
        private long nextSequence = 1;

        public ActionInvocationBuffer() : this(DefaultCapacity)
        {
        }

        public ActionInvocationBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.capacity = capacity;
        }

        public void Record(string tag, string name, bool success, object returnValue, string errorType, string errorMessage)
        {
            lock (gate)
            {
                if (actions.Count == capacity)
                {
                    actions.RemoveAt(0);
                }

                actions.Add(new ActionInvocation(
                    nextSequence++, tag, name, success, returnValue, errorType, errorMessage, DateTimeOffset.UtcNow));
            }
        }

        public ActionBatchSnapshot Snapshot()
        {
            lock (gate)
            {
                var copy = new List<ActionInvocation>(actions);
                var watermark = copy.Count == 0 ? 0 : copy[copy.Count - 1].Sequence;
                return new ActionBatchSnapshot(copy, watermark);
            }
        }

        public void Commit(long watermark)
        {
            if (watermark <= 0)
            {
                return;
            }

            lock (gate)
            {
                actions.RemoveAll(action => action.Sequence <= watermark);
            }
        }
    }
}
