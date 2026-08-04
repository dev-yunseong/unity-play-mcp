using System;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Auth
{
    public sealed class SdkLoginTests
    {
        private static readonly string[] SessionPlayerPrefsKeys =
        {
            "Artel.SdkToken",
            "Artel.SdkTokenExpiresAt",
            "Artel.SdkRefreshToken",
            "Artel.SdkRefreshTokenExpiresAt",
            "Artel.SdkDisplayName",
            "Artel.ProjectId",
            "Artel.InstanceId"
        };

        [SetUp]
        public void SetUp()
        {
            // 세션 테스트는 실제 키체인이나 사용자 프로필을 건드리지 않는다. 실제 플랫폼
            // 구현은 PlatformSecretStore_RoundTripsAndDeletes 하나에서만 돈다.
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ClearSession();
        }

        [TearDown]
        public void TearDown()
        {
            ClearSession();
            ArtelSecretStore.Current = null;
        }

        [Test]
        public void Callback_YieldsCodeWhenStateMatches()
        {
            var read = ArtelLoopbackLogin.TryReadCallback(
                "/callback?code=abc123&state=xyz", "xyz", out var code, out var error);

            Assert.That(read, Is.True);
            Assert.That(code, Is.EqualTo("abc123"));
            Assert.That(error, Is.Null);
        }

        [Test]
        public void Callback_UnescapesPercentEncodedCode()
        {
            var read = ArtelLoopbackLogin.TryReadCallback(
                "/callback?state=xyz&code=a%2Bb%2Fc", "xyz", out var code, out _);

            Assert.That(read, Is.True);
            Assert.That(code, Is.EqualTo("a+b/c"));
        }

        [Test]
        public void Callback_RejectsMismatchedState()
        {
            var read = ArtelLoopbackLogin.TryReadCallback(
                "/callback?code=abc123&state=attacker", "xyz", out var code, out var error);

            Assert.That(read, Is.False);
            Assert.That(code, Is.Null);
            Assert.That(error, Does.Contain("state"));
        }

        [Test]
        public void Callback_RejectsMissingState()
        {
            var read = ArtelLoopbackLogin.TryReadCallback(
                "/callback?code=abc123", "xyz", out _, out var error);

            Assert.That(read, Is.False);
            Assert.That(error, Does.Contain("state"));
        }

        [Test]
        public void Callback_RejectsMissingCode()
        {
            var read = ArtelLoopbackLogin.TryReadCallback(
                "/callback?state=xyz", "xyz", out _, out var error);

            Assert.That(read, Is.False);
            Assert.That(error, Does.Contain("code"));
        }

        [Test]
        public void Callback_IgnoresPathsOtherThanCallback()
        {
            // 브라우저는 콜백 직후 /favicon.ico를 물어본다. 이것을 콜백으로 세면 진짜
            // 콜백이 오기 전에 로그인이 실패로 끝난다.
            Assert.That(ArtelLoopbackLogin.IsCallback("/callback?code=a&state=b"), Is.True);
            Assert.That(ArtelLoopbackLogin.IsCallback("/favicon.ico"), Is.False);
            Assert.That(ArtelLoopbackLogin.IsCallback("/"), Is.False);
        }

        [Test]
        public void CodeChallenge_MatchesKnownVector()
        {
            // RFC 7636 Appendix B. 서버가 SHA-256(verifier)를 같은 방식으로 계산하므로,
            // 패딩을 남기거나 +/를 그대로 두면 교환이 전부 400이 된다.
            Assert.That(
                ArtelLoopbackLogin.CreateCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"),
                Is.EqualTo("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
        }

        [Test]
        public void CodeVerifier_UsesUnreservedCharactersWithinLengthBounds()
        {
            var verifier = ArtelLoopbackLogin.CreateCodeVerifier();

            Assert.That(verifier.Length, Is.InRange(43, 128));
            foreach (var character in verifier)
            {
                Assert.That(
                    char.IsLetterOrDigit(character) || character == '-' || character == '.' ||
                    character == '_' || character == '~',
                    Is.True,
                    "code_verifier must stay in the unreserved set: " + verifier);
            }

            Assert.That(ArtelLoopbackLogin.CreateCodeVerifier(), Is.Not.EqualTo(verifier));
        }

        [Test]
        public void LoginUrl_CarriesPortStateAndChallenge()
        {
            var url = ArtelLoopbackLogin.BuildLoginUrl(
                new Uri("http://localhost:5173"), 51234, "state+value", "challenge");

            Assert.That(
                url,
                Is.EqualTo("http://localhost:5173/sdk-login?port=51234&state=state%2Bvalue&challenge=challenge"));
        }

        [Test]
        public void Session_IsAbsentBeforeFirstSave()
        {
            Assert.That(ArtelSdkSession.TryLoadToken(out var token), Is.False);
            Assert.That(token, Is.Empty);
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadInstanceId(out _), Is.False);
        }

        [Test]
        public void Session_RoundTripsThroughPlayerPrefs()
        {
            ArtelSdkSession.SaveToken("  jwt-token  ", "2999-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveProjectId("1");
            ArtelSdkSession.SaveInstanceId("7");

            Assert.That(ArtelSdkSession.TryLoadToken(out var token), Is.True);
            Assert.That(token, Is.EqualTo("jwt-token"));
            Assert.That(ArtelSdkSession.DisplayName, Is.EqualTo("octocat"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out var projectId), Is.True);
            Assert.That(projectId, Is.EqualTo("1"));
            Assert.That(ArtelSdkSession.LoadInstanceId(), Is.EqualTo("7"));

            ArtelSdkSession.Clear();

            Assert.That(ArtelSdkSession.TryLoadToken(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadInstanceId(out _), Is.False);
        }

        [Test]
        public void Session_DropsExpiredToken()
        {
            ArtelSdkSession.SaveToken("jwt-token", "2000-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveProjectId("1");

            Assert.That(ArtelSdkSession.TryLoadToken(out _), Is.False);

            // 만료를 발견한 자리에서 세션 전체를 버려야 다음 실행이 프로젝트만 들고
            // 로그인 없는 상태로 남지 않는다.
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.False);
        }

        [Test]
        public void Session_KeepsTokenWhenExpiryIsUnreadable()
        {
            // 서버가 형식을 바꾸면 401을 한 번 받는 쪽이, 로그인이 통째로 막히는 쪽보다 낫다.
            ArtelSdkSession.SaveToken("jwt-token", "언젠가", "octocat");

            Assert.That(ArtelSdkSession.TryLoadToken(out _), Is.True);
        }

        [Test]
        public void Session_KeepsRefreshTokenWhenAccessTokenExpires()
        {
            // 재발급의 전제. 만료를 발견한 자리에서 세션을 통째로 버리면 다시 받을 수단까지 사라진다.
            ArtelSdkSession.SaveToken("jwt-token", "2000-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveRefreshToken("refresh-token", "2999-01-01T00:00:00Z");
            ArtelSdkSession.SaveProjectId("1");

            Assert.That(ArtelSdkSession.TryLoadToken(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadRefreshToken(out var refreshToken), Is.True);
            Assert.That(refreshToken, Is.EqualTo("refresh-token"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.True);
        }

        [Test]
        public void Session_DropsEverythingWhenRefreshTokenExpiresToo()
        {
            ArtelSdkSession.SaveToken("jwt-token", "2000-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveRefreshToken("refresh-token", "2000-01-01T00:00:00Z");
            ArtelSdkSession.SaveProjectId("1");

            Assert.That(ArtelSdkSession.TryLoadToken(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadRefreshToken(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.False);
        }

        [Test]
        public void Session_ClearRemovesRefreshToken()
        {
            ArtelSdkSession.SaveToken("jwt-token", "2999-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveRefreshToken("refresh-token", "2999-01-01T00:00:00Z");

            ArtelSdkSession.Clear();

            Assert.That(ArtelSdkSession.TryLoadRefreshToken(out _), Is.False);
        }

        [Test]
        public void PlatformSecretStore_RoundTripsAndDeletes()
        {
            // 플랫폼 구현이 실제로 도는지 보는 유일한 자리다. macOS면 키체인에, Windows면
            // 사용자 프로필 아래 DPAPI 파일에 진짜로 쓴다. 세션 키와 겹치지 않는 이름을 써서
            // 이 테스트가 로그인 상태를 지우지 않도록 한다.
            const string probeKey = "Artel.SecretStoreProbe";
            var store = ArtelSecretStore.CreatePlatformStore();

            // 실제 SDK 토큰 길이로 넣는다. 짧은 값은 macOS 키체인의 128바이트 절단을 넘어가지
            // 못해, 저장은 되는데 서버가 401을 주는 상태를 테스트가 통과시켜 버린다.
            var probeValue = "eyJhbGciOiJIUzI1NiJ9." + new string('x', 600) + ".signature";

            try
            {
                store.Save(probeKey, probeValue);

                Assert.That(store.TryLoad(probeKey, out var value), Is.True);
                Assert.That(value, Is.EqualTo(probeValue));
            }
            finally
            {
                store.Delete(probeKey);
            }

            Assert.That(store.TryLoad(probeKey, out _), Is.False);

            // 없는 키를 다시 지우는 것은 성공으로 친다 — 로그아웃이 두 번 눌려도 오류가 아니다.
            Assert.DoesNotThrow(() => store.Delete(probeKey));
        }

        private static void ClearSession()
        {
            foreach (var key in SessionPlayerPrefsKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
        }
    }
}
