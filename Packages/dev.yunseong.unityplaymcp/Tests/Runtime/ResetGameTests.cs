using System.Collections;
using System.Collections.Generic;
using UnityPlayMcp.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// reset_game reloads the scene the run started in, and must never reload any other scene.
    /// </summary>
    public sealed class ResetGameTests
    {
        /// <summary>게임이 쓴 것처럼 쓰는 키다. 이 suite 가 PlayerPrefs 에 남기는 유일한 값이다.</summary>
        private const string GameKey = "unityplaymcp.tests.gameKey";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(GameKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The test runner's scene is not in Build Settings, like a game launched from an unlisted
        /// scene: there is no index to go back to.
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
        /// A refused reset reloads nothing, so a paused game stays paused and resume_time still works.
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

        /// <summary>
        /// options 가 오브젝트가 아니면 강제 변환하지 않고 거절한다.
        /// </summary>
        [Test]
        public void ResetRejectsAParamThatIsNotAnObject()
        {
            var executor = new ActionExecutor(null, null, new PointerEventDispatcher());

            var result = Run(executor, 1, "reset_game", "true");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("object"));
        }

        /// <summary>
        /// 문자열 "true" 는 true 가 아니다. truthy 로 해석하면 실수로 보낸 "false" 도 저장소를 비운다.
        /// </summary>
        [Test]
        public void ResetRejectsANonBooleanClearFlag()
        {
            var executor = new ActionExecutor(null, null, new PointerEventDispatcher());

            var result = Run(
                executor,
                1,
                "reset_game",
                new Dictionary<string, object> { { "clearPlayerPrefs", "true" } });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("clearPlayerPrefs"));
        }

        [Test]
        public void ResetRejectsMoreThanOneParam()
        {
            var executor = new ActionExecutor(null, null, new PointerEventDispatcher());

            var result = Run(
                executor,
                1,
                "reset_game",
                new Dictionary<string, object> { { "clearPlayerPrefs", true } },
                new Dictionary<string, object>());

            Assert.That(result.IsSuccess, Is.False);

            // Build Settings 가드도 실패를 돌려주므로, 메시지를 확인해야 개수 검사가 빠진 것을 잡는다.
            Assert.That(result.Error, Does.Contain("params are [] or [options]"));
        }

        /// <summary>
        /// 거절된 리셋은 <c>PlayerPrefs</c> 를 포함해 아무것도 바꾸지 않는다.
        /// </summary>
        /// <remarks>
        /// 지우기는 Build Settings 가드 뒤에 있어야 한다. 순서가 바뀌면 실패한 리셋이 세이브만 지운다.
        /// </remarks>
        [Test]
        public void ARefusedResetDoesNotTouchPlayerPrefs()
        {
            PlayerPrefs.SetString(GameKey, "kept");
            var executor = new ActionExecutor(null, null, new PointerEventDispatcher());

            var result = Run(
                executor,
                1,
                "reset_game",
                new Dictionary<string, object> { { "clearPlayerPrefs", true } });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("Build Settings"));
            Assert.That(PlayerPrefs.HasKey(GameKey), Is.True);
            Assert.That(PlayerPrefs.GetString(GameKey), Is.EqualTo("kept"));
        }

        /// <summary>
        /// params 를 보내지 않는 서버는 이 flag 이전과 같이 동작해 Build Settings 실패에 도달해야 한다.
        /// </summary>
        [Test]
        public void ResetWithNoParamsStillWorks()
        {
            var executor = new ActionExecutor(null, null, new PointerEventDispatcher());

            var result = Run(executor, 1, "reset_game");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("Build Settings"));
        }

        private static ActionResultDto Run(
            ActionExecutor executor, int actionId, string method, params object[] parameters)
        {
            ActionResultDto result = null;
            Drain(executor.Execute(
                actionId, method, new List<object>(parameters), value => result = value));
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
