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
        /// <summary>게임이 쓴 것처럼 굴 키. 이 스위트가 PlayerPrefs 에 남기는 유일한 흔적이다.</summary>
        private const string GameKey = "artel.tests.gameKey";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(GameKey);
            PlayerPrefs.Save();
        }

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

        /// <summary>
        /// options 자리에 오브젝트가 아닌 것이 오면 거절한다. 강제 변환하지 않는다.
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
        /// 문자열 "true" 는 true 가 아니다. 파괴적인 flag 를 truthy 에서 만들어 내면,
        /// 서버가 실수로 보낸 "false" 조차 저장소를 비우는 명령이 된다.
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

            // Build Settings 실패가 아니라 params 실패여야 한다. 메시지를 보지 않으면 개수
            // 검사를 지워도 테스트가 그대로 통과한다 — 가드가 어차피 실패를 돌려주기 때문이다.
            Assert.That(result.Error, Does.Contain("params are [] or [options]"));
        }

        /// <summary>
        /// 거절된 리셋은 아무것도 바꾸지 않는다. <c>PlayerPrefs</c> 도 마찬가지다.
        /// </summary>
        /// <remarks>
        /// 지우기는 Build Settings 가드보다 뒤에 있어야 한다는 것을 못 박는 테스트다.
        /// 순서가 뒤집히면 씬으로 돌아가지도 못하는 리셋이 게임의 세이브만 날리고 실패를
        /// 돌려준다 — 되돌릴 수 없는 쪽으로만 반쯤 실행된 액션이다.
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
        /// params 를 보내지 않는 서버는 이 flag 가 생기기 전과 똑같이 동작해야 한다.
        /// Build Settings 실패까지 도달하는 것이 그 증거다 — params 실패가 아니다.
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
