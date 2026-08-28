using System.Collections.Generic;
using Artel.Domain;

namespace Artel.Tracking
{
    internal sealed class SceneScanResult
    {
        public SceneSnapshot Scene { get; }
        public IReadOnlyList<ActionBatchCommit> ActionCommits { get; }

        public SceneScanResult(SceneSnapshot scene, IReadOnlyList<ActionBatchCommit> actionCommits)
        {
            Scene = scene;
            ActionCommits = actionCommits;
        }

        public void CommitActions()
        {
            foreach (var actionCommit in ActionCommits)
            {
                actionCommit.Commit();
            }
        }
    }
}
