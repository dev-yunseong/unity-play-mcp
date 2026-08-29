using System.Collections.Generic;
using Artel.Domain;

namespace Artel.Tracking
{
    public sealed class ActionBatchSnapshot
    {
        public IReadOnlyList<ActionInvocation> Actions { get; }
        public long Watermark { get; }

        public ActionBatchSnapshot(IReadOnlyList<ActionInvocation> actions, long watermark)
        {
            Actions = actions;
            Watermark = watermark;
        }
    }
}
