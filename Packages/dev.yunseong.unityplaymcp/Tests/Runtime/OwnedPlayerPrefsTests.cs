using System.Collections.Generic;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// <c>ArtelOwnedPlayerPrefs.DeleteAllExceptOwn()</c> 이 SDK 의 키만 남기는지 본다.
    /// </summary>
    /// <remarks>
    /// 이 스위트는 진짜로 <c>PlayerPrefs.DeleteAll()</c> 을 부른다. 흉내가 아니라 실제
    /// 저장소를 비우므로, 이것을 실행하는 Unity 프로젝트의 <c>PlayerPrefs</c> 는 통째로
    /// 날아간다. <c>project.md</c> 의 `## Running package tests` 가 말하는 대로,
    /// <c>.github/scripts/setup-unity-test-project.sh</c> 가 만드는 버리는 프로젝트에서
    /// 돌린다 — 개인 프로젝트를 열어 두고 실행하면 그 프로젝트의 설정이 사라진다.
    ///
    /// 각 테스트는 자기가 건드리는 키를 <c>[SetUp]</c> 에서 기억했다가 <c>[TearDown]</c> 에서
    /// 되돌린다. <c>WebSocketTransportTests</c> 와 같은 모양이다.
    ///
    /// "레지스트리가 SDK 가 쓰는 모든 키를 담고 있다" 를 일반적으로 확인하는 테스트는 쓸 수
    /// 없다 — Unity <c>PlayerPrefs</c> 는 키를 열거하지 못한다. 대신 세션을 실제로 저장했다가
    /// wipe 뒤에 다시 읽는 <c>TheSessionIsStillLoadableAfterAWipe</c> 가 그 자리를 대신한다.
    /// </remarks>
    public sealed class OwnedPlayerPrefsTests
    {
        private const string GameProgressKey = "game.progress";
        private const string GameVolumeKey = "game.volume";

        private readonly Dictionary<string, string> savedStrings = new Dictionary<string, string>();
        private readonly Dictionary<string, int> savedInts = new Dictionary<string, int>();

        [SetUp]
        public void SetUp()
        {
            // 토큰 두 개는 보안 저장소를 거친다. 갈아끼우지 않으면 macOS 는 Keychain,
            // Windows 는 DPAPI 로 가고, PlayerPrefs 에는 애초에 아무것도 쓰이지 않아
            // 이 테스트가 그 두 플랫폼에서 아무것도 검사하지 않는 껍데기가 된다 —
            // 정확히 버그가 새어 나갈 수 있는 플랫폼 비대칭이다.
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();

            savedStrings.Clear();
            savedInts.Clear();

            foreach (var key in ArtelOwnedPlayerPrefs.StringKeys)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    savedStrings[key] = PlayerPrefs.GetString(key);
                }

                PlayerPrefs.DeleteKey(key);
            }

            foreach (var key in ArtelOwnedPlayerPrefs.IntKeys)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    savedInts[key] = PlayerPrefs.GetInt(key);
                }

                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.DeleteKey(GameProgressKey);
            PlayerPrefs.DeleteKey(GameVolumeKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var key in ArtelOwnedPlayerPrefs.StringKeys)
            {
                if (savedStrings.TryGetValue(key, out var value))
                {
                    PlayerPrefs.SetString(key, value);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }

            foreach (var key in ArtelOwnedPlayerPrefs.IntKeys)
            {
                if (savedInts.TryGetValue(key, out var value))
                {
                    PlayerPrefs.SetInt(key, value);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }

            PlayerPrefs.DeleteKey(GameProgressKey);
            PlayerPrefs.DeleteKey(GameVolumeKey);
            PlayerPrefs.Save();

            ArtelSecretStore.Current = null;
        }

        /// <summary>
        /// SDK 의 키는 값까지 그대로 살아남고, 게임의 키는 사라진다.
        /// </summary>
        /// <remarks>
        /// <c>Artel.DarkTheme</c> 을 0 으로 두는 것이 이 테스트에서 가장 많은 일을 한다.
        /// 0 은 <c>GetInt</c> 의 기본값(1)과 다르므로 "기본값으로 되쓰지 않는다"를 잡고,
        /// int 로 저장된 값이 int 로 돌아오는지도 함께 잡는다 — 문자열 목록에 섞였다면
        /// <c>GetString</c> 이 왕복하지 못해 여기서 깨진다.
        /// </remarks>
        [Test]
        public void TheSdksOwnKeysSurviveAWipe()
        {
            var expected = new Dictionary<string, string>();
            foreach (var key in ArtelOwnedPlayerPrefs.StringKeys)
            {
                expected[key] = "value-for-" + key;
                PlayerPrefs.SetString(key, expected[key]);
            }

            PlayerPrefs.SetInt(ArtelOwnedPlayerPrefs.DarkTheme, 0);
            PlayerPrefs.SetString(GameProgressKey, "level-7");
            PlayerPrefs.SetFloat(GameVolumeKey, 0.5f);
            PlayerPrefs.Save();

            ArtelOwnedPlayerPrefs.DeleteAllExceptOwn();

            foreach (var key in ArtelOwnedPlayerPrefs.StringKeys)
            {
                Assert.That(PlayerPrefs.HasKey(key), Is.True, key + " was wiped");
                Assert.That(PlayerPrefs.GetString(key), Is.EqualTo(expected[key]), key);
            }

            Assert.That(PlayerPrefs.HasKey(ArtelOwnedPlayerPrefs.DarkTheme), Is.True);
            Assert.That(PlayerPrefs.GetInt(ArtelOwnedPlayerPrefs.DarkTheme), Is.EqualTo(0));

            Assert.That(PlayerPrefs.HasKey(GameProgressKey), Is.False);
            Assert.That(PlayerPrefs.HasKey(GameVolumeKey), Is.False);
        }

        /// <summary>
        /// 없던 키를 만들어 내지 않는다.
        /// </summary>
        /// <remarks>
        /// 기본값으로 읽고 되쓰면 사용자가 한 번도 고른 적 없는 테마가 생기고, 라이트 테마를
        /// 쓰던 사람이 그 순간부터 다크에 고정된다. <c>Artel.ProjectId</c> 도 마찬가지로
        /// 빈 문자열이 생기면 "고른 적 없음"과 "빈 값을 골랐음"이 구분되지 않는다.
        /// </remarks>
        [Test]
        public void AKeyTheSdkNeverWroteIsNotCreated()
        {
            Assert.That(PlayerPrefs.HasKey(ArtelOwnedPlayerPrefs.DarkTheme), Is.False);
            Assert.That(PlayerPrefs.HasKey(ArtelOwnedPlayerPrefs.ProjectId), Is.False);

            ArtelOwnedPlayerPrefs.DeleteAllExceptOwn();

            Assert.That(PlayerPrefs.HasKey(ArtelOwnedPlayerPrefs.DarkTheme), Is.False);
            Assert.That(PlayerPrefs.HasKey(ArtelOwnedPlayerPrefs.ProjectId), Is.False);
        }

        /// <summary>
        /// wipe 를 지나고도 세션이 그대로 열린다.
        /// </summary>
        /// <remarks>
        /// 레지스트리를 production 코드에 못 박는 것이 이 테스트다. 누군가
        /// <c>ArtelSdkSession</c> 에 키를 하나 더하고 레지스트리에 적는 것을 잊으면, 상수
        /// 목록을 세는 테스트는 여전히 통과하지만 이것은 깨진다 — 저장한 값을 다시 읽지
        /// 못하게 되기 때문이다.
        /// </remarks>
        [Test]
        public void TheSessionIsStillLoadableAfterAWipe()
        {
            var sdkId = ArtelSdkIdentity.LoadOrCreate();
            ArtelSdkSession.SaveToken("token-abc", "2999-01-01T00:00:00Z", "Someone");
            ArtelSdkSession.SaveRefreshToken("refresh-abc", "2999-01-01T00:00:00Z");
            ArtelSdkSession.SaveProjectId("project-1");
            ArtelSdkSession.SaveInstanceId("instance-1");
            ArtelSdkSession.SaveGameBuildId("build-1");
            PlayerPrefs.SetString(GameProgressKey, "level-7");
            PlayerPrefs.Save();

            ArtelOwnedPlayerPrefs.DeleteAllExceptOwn();

            Assert.That(ArtelSdkSession.TryLoadToken(out var token), Is.True);
            Assert.That(token, Is.EqualTo("token-abc"));
            Assert.That(ArtelSdkSession.TryLoadRefreshToken(out var refreshToken), Is.True);
            Assert.That(refreshToken, Is.EqualTo("refresh-abc"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out var projectId), Is.True);
            Assert.That(projectId, Is.EqualTo("project-1"));
            Assert.That(ArtelSdkSession.TryLoadInstanceId(out var instanceId), Is.True);
            Assert.That(instanceId, Is.EqualTo("instance-1"));
            Assert.That(ArtelSdkSession.TryLoadGameBuildId(out var gameBuildId), Is.True);
            Assert.That(gameBuildId, Is.EqualTo("build-1"));
            Assert.That(ArtelSdkSession.DisplayName, Is.EqualTo("Someone"));

            // 같은 설치본으로 남아야 한다. 새 guid 가 나오면 서버 쪽에서는 다른 SDK 다.
            Assert.That(ArtelSdkIdentity.LoadOrCreate(), Is.EqualTo(sdkId));

            Assert.That(PlayerPrefs.HasKey(GameProgressKey), Is.False);
        }
    }
}
