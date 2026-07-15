using System;
using System.Reflection;
using Artel.Domain;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

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
        public void Server_BuildsSdkRegistrationAndSecureWebSocketEndpoints()
        {
            var server = new Server("https://test.artel.example/", "wss://socket.artel.example/");

            Assert.That(server.SdkRegistrationUri.AbsoluteUri, Is.EqualTo("https://test.artel.example/api/sdkId"));
            Assert.That(
                server.GetSdkWebSocketUri("sdk id").AbsoluteUri,
                Is.EqualTo("wss://socket.artel.example/ws/sdk?sdkId=sdk%20id"));
        }

        [Test]
        public void SdkRegistrationRequest_SerializesExpectedContract()
        {
            var json = JsonConvert.SerializeObject(new SdkRegistrationRequestDto { SdkId = "sdk-id" });

            Assert.That(json, Is.EqualTo("{\"sdkId\":\"sdk-id\"}"));
        }

        [Test]
        public void OnboardingController_CreatesToggleAndRegistrationPanel()
        {
            var host = new GameObject("Artel onboarding test");
            var manager = host.AddComponent<ArtelManager>();
            var controller = host.AddComponent<ArtelOnboardingController>();

            try
            {
                InvokeLifecycle(manager, "Awake");
                InvokeLifecycle(controller, "Awake");
                InvokeLifecycle(controller, "Start");
                var canvas = GameObject.Find("Artel Onboarding Canvas");
                Assert.That(canvas, Is.Not.Null);
                var buttons = canvas.GetComponentsInChildren<Button>(true);
                var connectButton = Array.Find(buttons, button => button.name == "실시간 연결 Button");

                Assert.That(manager.SdkId, Is.Not.Empty);
                Assert.That(buttons, Has.Length.EqualTo(3));
                Assert.That(connectButton, Is.Not.Null);
                Assert.That(connectButton.interactable, Is.False);
            }
            finally
            {
                var canvas = GameObject.Find("Artel Onboarding Canvas");
                var eventSystem = GameObject.Find("Artel EventSystem");
                if (canvas != null)
                {
                    UnityEngine.Object.DestroyImmediate(canvas);
                }

                if (eventSystem != null)
                {
                    UnityEngine.Object.DestroyImmediate(eventSystem);
                }

                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void InvokeLifecycle(object target, string methodName)
        {
            target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }
    }
}
