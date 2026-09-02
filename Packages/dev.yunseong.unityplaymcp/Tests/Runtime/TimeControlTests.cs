using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// pause_time and resume_time, which are the only actions that change state the game keeps
    /// after the SDK is done with it. What they must never do is leave it changed.
    /// </summary>
    public sealed class TimeControlTests
    {
        private float originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            originalTimeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = originalTimeScale;
        }

        [Test]
        public void ResumeRestoresTheScaleThePauseFound()
        {
            // Not 1: a game already in slow motion has to come back to its own speed.
            Time.timeScale = 0.5f;
            var executor = NewExecutor();

            Run(executor, 1, "pause_time");
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            Run(executor, 2, "resume_time");
            Assert.That(Time.timeScale, Is.EqualTo(0.5f));
        }

        [Test]
        public void PausingTwiceStillResumesToTheOriginalScale()
        {
            Time.timeScale = 1f;
            var executor = NewExecutor();

            Run(executor, 1, "pause_time");
            Run(executor, 2, "pause_time");
            Run(executor, 3, "resume_time");

            // The second pause must not have recorded the frozen 0 as the scale to go back to.
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void ResumingWithoutAPauseFailsRatherThanGuessingAScale()
        {
            Time.timeScale = 0.25f;
            var executor = NewExecutor();

            var result = Run(executor, 1, "resume_time");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("was not paused"));
            Assert.That(Time.timeScale, Is.EqualTo(0.25f));
        }

        [Test]
        public void RestoringUnfreezesAGameLeftPaused()
        {
            Time.timeScale = 1f;
            var executor = NewExecutor();
            Run(executor, 1, "pause_time");

            executor.RestoreTimeScale();

            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        /// <summary>
        /// Null scanner and cursor: no time action touches the scene or the pointer.
        /// </summary>
        private static ActionExecutor NewExecutor()
        {
            return new ActionExecutor(null, null, new PointerEventDispatcher());
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
