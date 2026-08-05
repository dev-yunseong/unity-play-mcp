using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// reset_game reloads the scene the run started in. The one thing it must never do is reload
    /// some other scene, because the scene it aims at is the whole meaning of the action.
    /// </summary>
    public sealed class ResetGameTests
    {
        /// <summary>
        /// The test runner's scene is not in Build Settings, which is the same position a game
        /// launched from an unlisted scene is in: there is no index to go back to.
        /// </summary>
        [Test]
        public void ResetFailsWhenTheStartupSceneIsNotInBuildSettings()
        {
            var executor = new ActionExecutor(null, null, new PointerEventDispatcher());

            var result = Run(executor, 1, "reset_game");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("Build Settings"));
        }

        /// <summary>
        /// A refused reset must leave a paused game paused: it did not reload anything, so the run
        /// still owns the freeze and resume_time still has to work.
        /// </summary>
        [Test]
        public void ARefusedResetLeavesThePauseAlone()
        {
            var originalTimeScale = Time.timeScale;
            try
            {
                var executor = new ActionExecutor(null, null, new PointerEventDispatcher());
                Run(executor, 1, "pause_time");

                Run(executor, 2, "reset_game");

                Assert.That(Time.timeScale, Is.EqualTo(0f));
                Assert.That(Run(executor, 3, "resume_time").IsSuccess, Is.True);
            }
            finally
            {
                Time.timeScale = originalTimeScale;
            }
        }

        private static ActionResultDto Run(ActionExecutor executor, int actionId, string method)
        {
            ActionResultDto result = null;
            Drain(executor.Execute(actionId, method, new List<object>(), value => result = value));
            return result;
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }
    }
}
