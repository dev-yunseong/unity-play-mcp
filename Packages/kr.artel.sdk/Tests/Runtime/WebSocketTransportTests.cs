using System;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Transport
{
    public sealed class WebSocketTransportTests
    {
        private const string PlayerPrefsKey = "Artel.SdkId";
        private string originalSdkId;
        private bool hadOriginalSdkId;

        [SetUp]
        public void SetUp()
        {
            hadOriginalSdkId = PlayerPrefs.HasKey(PlayerPrefsKey);
            originalSdkId = PlayerPrefs.GetString(PlayerPrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            if (hadOriginalSdkId)
            {
                PlayerPrefs.SetString(PlayerPrefsKey, originalSdkId);
            }
            else
            {
                PlayerPrefs.DeleteKey(PlayerPrefsKey);
            }

            PlayerPrefs.Save();
        }

        [Test]
        public void LoadOrCreate_ReusesStoredUuid()
        {
            var expectedSdkId = Guid.NewGuid().ToString("D");
            PlayerPrefs.SetString(PlayerPrefsKey, expectedSdkId);

            var sdkId = ArtelSdkIdentity.LoadOrCreate();

            Assert.That(sdkId, Is.EqualTo(expectedSdkId));
        }

        [Test]
        public void LoadOrCreate_ReplacesInvalidStoredValue()
        {
            PlayerPrefs.SetString(PlayerPrefsKey, "invalid");

            var sdkId = ArtelSdkIdentity.LoadOrCreate();

            Assert.That(Guid.TryParse(sdkId, out _), Is.True);
            Assert.That(PlayerPrefs.GetString(PlayerPrefsKey), Is.EqualTo(sdkId));
        }

        [Test]
        public void WithSdkId_AppendsEscapedQueryParameter()
        {
            var url = ArtelWebSocketUrl.WithSdkId("wss://artel.example/ws?version=1", "sdk id");

            Assert.That(url, Is.EqualTo("wss://artel.example/ws?version=1&sdkId=sdk%20id"));
        }
    }
}
