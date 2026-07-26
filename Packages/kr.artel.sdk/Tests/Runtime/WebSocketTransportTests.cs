using System;
using System.Reflection;
using System.Text;
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
        private const string InstanceKeyPlayerPrefsKey = "Artel.InstanceKey";
        private string originalSdkId;
        private bool hadOriginalSdkId;
        private string originalInstanceKey;
        private bool hadOriginalInstanceKey;

        [SetUp]
        public void SetUp()
        {
            hadOriginalSdkId = PlayerPrefs.HasKey(PlayerPrefsKey);
            originalSdkId = PlayerPrefs.GetString(PlayerPrefsKey);
            hadOriginalInstanceKey = PlayerPrefs.HasKey(InstanceKeyPlayerPrefsKey);
            originalInstanceKey = PlayerPrefs.GetString(InstanceKeyPlayerPrefsKey);
            PlayerPrefs.DeleteKey(InstanceKeyPlayerPrefsKey);
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

            if (hadOriginalInstanceKey)
            {
                PlayerPrefs.SetString(InstanceKeyPlayerPrefsKey, originalInstanceKey);
            }
            else
            {
                PlayerPrefs.DeleteKey(InstanceKeyPlayerPrefsKey);
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
        public void InstanceKey_IsAbsentBeforeFirstSave()
        {
            var loaded = ArtelInstanceKey.TryLoad(out var instanceKey);

            Assert.That(loaded, Is.False);
            Assert.That(instanceKey, Is.Empty);
        }

        [Test]
        public void InstanceKey_RoundTripsThroughPlayerPrefs()
        {
            ArtelInstanceKey.Save("  H4KQ2-8VTRM-9XZ0C-N5JWE  ");

            var loaded = ArtelInstanceKey.TryLoad(out var instanceKey);

            Assert.That(loaded, Is.True);
            Assert.That(instanceKey, Is.EqualTo("H4KQ2-8VTRM-9XZ0C-N5JWE"));

            ArtelInstanceKey.Clear();

            Assert.That(ArtelInstanceKey.TryLoad(out _), Is.False);
        }

        [Test]
        public void Server_BuildsSecureProtocolBaseUris()
        {
            var server = new Server(true, "test.artel.example", 8443);

            Assert.That(server.HttpBaseUri.AbsoluteUri, Is.EqualTo("https://test.artel.example:8443/"));
            Assert.That(
                server.WebSocketBaseUri.AbsoluteUri,
                Is.EqualTo("wss://test.artel.example:8443/"));
        }

        [Test]
        public void RegistrationClient_OwnsSdkRegistrationPathAndBody()
        {
            var server = new Server(false, "127.0.0.1", 8080);
            var client = new ArtelSdkRegistrationClient(new Artel.Serialization.NewtonsoftJsonCodec());
            var request = client.CreateRequest(server, "H4KQ2-8VTRM-9XZ0C-N5JWE", "sdk-uuid", "1.2.3");

            try
            {
                Assert.That(request.url, Is.EqualTo("http://127.0.0.1:8080/api/sdk/registrations"));
                Assert.That(
                    Encoding.UTF8.GetString(request.uploadHandler.data),
                    Is.EqualTo(
                        "{\"instanceKey\":\"H4KQ2-8VTRM-9XZ0C-N5JWE\"," +
                        "\"sdkUuid\":\"sdk-uuid\",\"gameVersion\":\"1.2.3\"}"));
            }
            finally
            {
                request.Dispose();
            }
        }

        [Test]
        public void RegistrationClient_IncludesSceneScanWhenProvided()
        {
            var server = new Server(false, "127.0.0.1", 8080);
            var client = new ArtelSdkRegistrationClient(new Artel.Serialization.NewtonsoftJsonCodec());
            var sceneScan = new Artel.Protocol.Dto.SceneScanReportDto();
            sceneScan.ScenesInBuild.Add("Assets/Scenes/Main.unity");

            var request = client.CreateRequest(server, "H4KQ2-8VTRM-9XZ0C-N5JWE", "sdk-uuid", "1.2.3", sceneScan);

            try
            {
                Assert.That(
                    Encoding.UTF8.GetString(request.uploadHandler.data),
                    Does.Contain("\"sceneScan\":{\"scenesInBuild\":[\"Assets/Scenes/Main.unity\"]"));
            }
            finally
            {
                request.Dispose();
            }
        }

        [Test]
        public void WebSocketClient_OwnsSdkWebSocketPathAndQuery()
        {
            var server = new Server(true, "socket.artel.example", 443);

            var endpoint = ArtelWebSocketClient.BuildEndpoint(server, "instance key");

            Assert.That(
                endpoint.AbsoluteUri,
                Is.EqualTo("wss://socket.artel.example/ws/sdk?instanceKey=instance%20key"));
        }

        [Test]
        public void SdkRegistrationRequest_SerializesExpectedContract()
        {
            var json = JsonConvert.SerializeObject(new SdkRegistrationRequestDto
            {
                InstanceKey = "H4KQ2-8VTRM-9XZ0C-N5JWE",
                SdkUuid = "sdk-uuid",
                GameVersion = "1.2.3"
            });

            Assert.That(
                json,
                Is.EqualTo(
                    "{\"instanceKey\":\"H4KQ2-8VTRM-9XZ0C-N5JWE\"," +
                    "\"sdkUuid\":\"sdk-uuid\",\"gameVersion\":\"1.2.3\"}"));
        }

        [Test]
        public void SdkRegistrationRequest_KeepsNullGameVersion()
        {
            var json = JsonConvert.SerializeObject(new SdkRegistrationRequestDto
            {
                InstanceKey = "H4KQ2-8VTRM-9XZ0C-N5JWE",
                SdkUuid = "sdk-uuid",
                GameVersion = null
            });

            Assert.That(
                json,
                Is.EqualTo(
                    "{\"instanceKey\":\"H4KQ2-8VTRM-9XZ0C-N5JWE\"," +
                    "\"sdkUuid\":\"sdk-uuid\",\"gameVersion\":null}"));
        }

        [Test]
        public void SdkRegistrationResponse_DeserializesServerContract()
        {
            var response = JsonConvert.DeserializeObject<SdkRegistrationResponseDto>(
                "{\"instanceId\":\"12\",\"projectId\":\"3\",\"instanceName\":\"메인 빌드\"," +
                "\"gameBuildId\":\"5\",\"gameVersion\":\"1.2.3\"}");

            Assert.That(response.InstanceId, Is.EqualTo("12"));
            Assert.That(response.ProjectId, Is.EqualTo("3"));
            Assert.That(response.InstanceName, Is.EqualTo("메인 빌드"));
            Assert.That(response.GameBuildId, Is.EqualTo("5"));
            Assert.That(response.GameVersion, Is.EqualTo("1.2.3"));
        }

        [Test]
        public void TestPage_ProvidesKeyboardActionControls()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"key-code\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"key-duration\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"key-click\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("sendAction('key_click', [key, duration])"));
        }

        [Test]
        public void TestPage_ScansEverySceneAndRendersTheResultDead()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"scan-all\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("sendAction('scan_all_scenes', mode ? [mode] : [])"));
            Assert.That(ArtelTestPage.Html, Does.Contain("if (message.type === 'ALL_SCENES') renderAllScenes(message.scenes, message)"));

            // Blocks from a scene the walk unloaded are gone by the time the page draws
            // them, so only the scene that was already open stays clickable.
            Assert.That(ArtelTestPage.Html, Does.Contain("renderNode(entry.scene, entry.scene.id === liveSceneId)"));
            Assert.That(ArtelTestPage.Html, Does.Contain("button.disabled = !interactive"));
            Assert.That(ArtelTestPage.Html, Does.Contain("input.disabled = !interactive"));
        }

        [Test]
        public void TestPage_PinsTheFullScanResultOutsideTheLiveScene()
        {
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"scan-all-full\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("scanAllScenes('full')"));

            // The poller pushes a GAME_STATE within a second of any change. A scan that
            // took the whole walk to produce has to survive that, so it is drawn into its
            // own section and stays until Clear.
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"snapshot\""));
            Assert.That(ArtelTestPage.Html, Does.Contain("snapshotScene.appendChild(renderNode(entry.scene"));
            Assert.That(ArtelTestPage.Html, Does.Contain("snapshotJson.textContent = JSON.stringify(message, null, 2)"));
            Assert.That(ArtelTestPage.Html, Does.Contain("id=\"snapshot-clear\""));

            // Only a full scan reports inactive objects, and they have to read as inactive.
            Assert.That(ArtelTestPage.Html, Does.Contain("const inactive = node.active === false"));
        }

        [Test]
        public void OnboardingViewModel_StartsInNeedsKeyWhenNoKeyStored()
        {
            var viewModel = CreateViewModel();

            viewModel.Initialize();

            Assert.That(viewModel.State, Is.EqualTo(ArtelOnboardingState.NeedsKey));
            Assert.That(viewModel.HasStoredKey, Is.False);
            Assert.That(viewModel.ShowPanel, Is.True);
            Assert.That(viewModel.KeyInput, Is.Empty);
            Assert.That(viewModel.CanRegister, Is.False);
            Assert.That(viewModel.CanConnect, Is.False);
        }

        [Test]
        public void OnboardingViewModel_KeepsPanelCollapsedWhenKeyStored()
        {
            ArtelInstanceKey.Save("H4KQ2-8VTRM-9XZ0C-N5JWE");
            var viewModel = CreateViewModel();

            viewModel.Initialize();

            Assert.That(viewModel.HasStoredKey, Is.True);
            Assert.That(viewModel.ShowPanel, Is.False);
            Assert.That(viewModel.KeyInput, Is.EqualTo("H4KQ2-8VTRM-9XZ0C-N5JWE"));
            Assert.That(viewModel.CanRegister, Is.True);
        }

        [Test]
        public void OnboardingViewModel_DoesNotPersistKeyWhenRegistrationFails()
        {
            var viewModel = CreateViewModel();
            viewModel.Initialize();

            // An unconfigured Server throws while the request is built, before anything is sent.
            var registration = viewModel.Register(new Server(), "H4KQ2-8VTRM-9XZ0C-N5JWE", "sdk-uuid", "1.2.3", () => { });

            Assert.That(registration.MoveNext(), Is.False);
            Assert.That(viewModel.State, Is.EqualTo(ArtelOnboardingState.NeedsKey));
            Assert.That(viewModel.ShowPanel, Is.True);
            Assert.That(viewModel.Status, Does.StartWith("설정 오류: "));
            Assert.That(viewModel.HasStoredKey, Is.False);
            Assert.That(PlayerPrefs.HasKey(InstanceKeyPlayerPrefsKey), Is.False);
        }

        [Test]
        public void ArtelManager_CreatesOnboardingGuiAutomatically()
        {
            var host = new GameObject("Artel onboarding test");
            var manager = host.AddComponent<ArtelManager>();

            try
            {
                InvokeLifecycle(manager, "Awake");
                var controller = host.GetComponent<ArtelOnboardingController>();
                Assert.That(controller, Is.Not.Null);
                Assert.That(host.GetComponent<KeyboardStatusController>(), Is.Not.Null);
                InvokeLifecycle(controller, "Awake");
                InvokeLifecycle(controller, "Start");
                var canvas = GameObject.Find("Artel Onboarding Canvas");
                Assert.That(canvas, Is.Not.Null);
                var buttons = canvas.GetComponentsInChildren<Button>(true);
                var smoothCursorToggle = canvas.GetComponentInChildren<Toggle>(true);
                var instanceKeyField = canvas.GetComponentInChildren<InputField>(true);
                var registerButton = Array.Find(buttons, button => button.name == "등록 Button");
                var connectButton = Array.Find(buttons, button => button.name == "연결 Button");

                Assert.That(manager.SdkId, Is.Not.Empty);
                Assert.That(buttons, Has.Length.EqualTo(5));
                Assert.That(instanceKeyField, Is.Not.Null);
                Assert.That(instanceKeyField.textComponent, Is.Not.Null);
                Assert.That(instanceKeyField.placeholder, Is.Not.Null);
                Assert.That(instanceKeyField.characterLimit, Is.EqualTo(24));
                Assert.That(registerButton, Is.Not.Null);
                Assert.That(registerButton.interactable, Is.False);
                Assert.That(connectButton, Is.Not.Null);
                Assert.That(connectButton.interactable, Is.False);
                Assert.That(smoothCursorToggle, Is.Not.Null);
                Assert.That(smoothCursorToggle.isOn, Is.False);
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

        private static ArtelOnboardingViewModel CreateViewModel()
        {
            return new ArtelOnboardingViewModel(
                new ArtelSdkRegistrationClient(new Artel.Serialization.NewtonsoftJsonCodec()));
        }

        private static void InvokeLifecycle(object target, string methodName)
        {
            target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }
    }
}
