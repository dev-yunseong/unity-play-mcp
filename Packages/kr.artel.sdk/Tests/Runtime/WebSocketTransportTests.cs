using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Artel.Domain;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

            // A button in a pinned scene is dead, so its onClick wiring is what explains it.
            Assert.That(ArtelTestPage.Html, Does.Contain("for (const handler of component.onClick || [])"));
        }

        [Test]
        public void OverlayViewModel_StartsInNeedsKeyWhenNoKeyStored()
        {
            var viewModel = CreateViewModel();

            viewModel.Initialize();

            Assert.That(viewModel.State, Is.EqualTo(ArtelConnectionState.NeedsKey));
            Assert.That(viewModel.HasStoredKey, Is.False);
            Assert.That(viewModel.ShowPanel, Is.True);
            Assert.That(viewModel.KeyInput, Is.Empty);
            Assert.That(viewModel.CanRegister, Is.False);
            Assert.That(viewModel.CanConnect, Is.False);
        }

        [Test]
        public void OverlayViewModel_KeepsPanelCollapsedWhenKeyStored()
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
        public void OverlayViewModel_DoesNotPersistKeyWhenRegistrationFails()
        {
            var viewModel = CreateViewModel();
            viewModel.Initialize();

            // An unconfigured Server throws while the request is built, before anything is sent.
            var registration = viewModel.Register(new Server(), "H4KQ2-8VTRM-9XZ0C-N5JWE", "sdk-uuid", "1.2.3", () => { });

            Assert.That(registration.MoveNext(), Is.False);
            Assert.That(viewModel.State, Is.EqualTo(ArtelConnectionState.NeedsKey));
            Assert.That(viewModel.ShowPanel, Is.True);
            Assert.That(viewModel.Status, Does.StartWith("설정 오류: "));
            Assert.That(viewModel.HasStoredKey, Is.False);
            Assert.That(PlayerPrefs.HasKey(InstanceKeyPlayerPrefsKey), Is.False);
        }

        [Test]
        public void ArtelManager_CreatesOverlayGuiAutomatically()
        {
            var host = new GameObject("Artel overlay test");
            var manager = host.AddComponent<ArtelManager>();

            try
            {
                InvokeLifecycle(manager, "Awake");
                var controller = host.GetComponent<ArtelOverlayController>();
                Assert.That(controller, Is.Not.Null);
                Assert.That(host.GetComponent<KeyboardStatusController>(), Is.Not.Null);
                InvokeLifecycle(controller, "Awake");
                InvokeLifecycle(controller, "Start");
                var canvas = GameObject.Find("Artel Overlay Canvas");
                Assert.That(canvas, Is.Not.Null);
                var buttons = canvas.GetComponentsInChildren<Button>(true);
                var smoothCursorToggle = canvas.GetComponentInChildren<Toggle>(true);
                var instanceKeyField = canvas.GetComponentInChildren<InputField>(true);
                var registerButton = Array.Find(buttons, button => button.name == "등록 Button");
                var connectButton = Array.Find(buttons, button => button.name == "연결 Button");

                Assert.That(manager.SdkId, Is.Not.Empty);
                // Artel 토글, 고급, 등록, 나중에, 연결, 키 지우기.
                Assert.That(buttons, Has.Length.EqualTo(6));
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
                Assert.That(canvas.GetComponentsInChildren<ArtelLogoGraphic>(true), Has.Length.EqualTo(2));
            }
            finally
            {
                var canvas = GameObject.Find("Artel Overlay Canvas");
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

        [Test]
        public void OverlayGui_CoverGeometryAndOpacity()
        {
            var host = new GameObject("Artel cover geometry test");

            // 매니저는 RequireComponent를 채우려고만 붙인다. 매니저의 Awake는
            // DontDestroyOnLoad를 부르는데 그건 플레이 모드 전용이라 여기서 돌릴 수 없다.
            host.AddComponent<ArtelManager>();
            var controller = host.AddComponent<ArtelOverlayController>();

            try
            {
                InvokeLifecycle(controller, "Awake");
                InvokeLifecycle(controller, "Start");

                var canvas = GameObject.Find("Artel Overlay Canvas");
                var cover = canvas.transform.Find("Artel Overlay Cover");
                Assert.That(cover, Is.Not.Null);

                // SetUp이 키를 지우므로 Start 직후가 곧 첫 실행이고, 게이트가 이 덮개로
                // 뜬다. 켜져 있는 것이 맞다.
                Assert.That(cover.gameObject.activeSelf, Is.True);

                // 캔버스의 마지막 자식이어야 같은 캔버스의 패널 위에 그려진다.
                Assert.That(cover.GetSiblingIndex(), Is.EqualTo(canvas.transform.childCount - 1));

                var coverRect = cover.GetComponent<RectTransform>();
                Assert.That(coverRect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(coverRect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(coverRect.offsetMin, Is.EqualTo(Vector2.zero));
                Assert.That(coverRect.offsetMax, Is.EqualTo(Vector2.zero));

                // 반투명하면 가리려던 씬 전환이 그대로 비치고, raycastTarget이 꺼지면
                // 덮인 게임 UI로 클릭이 샌다.
                var coverImage = cover.GetComponent<Image>();
                Assert.That(coverImage.color.a, Is.EqualTo(1f));
                Assert.That(coverImage.raycastTarget, Is.True);
            }
            finally
            {
                var canvas = GameObject.Find("Artel Overlay Canvas");
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

        [Test]
        public void OverlayViewModel_ShowsGateOnlyWhenNoKeyStored()
        {
            var withoutKey = CreateViewModel();
            withoutKey.Initialize();
            Assert.That(withoutKey.ShowGate, Is.True);

            ArtelInstanceKey.Save("H4KQ2-8VTRM-9XZ0C-N5JWE");
            var withKey = CreateViewModel();
            withKey.Initialize();

            // 저장 키가 있으면 Start가 곧바로 등록에 들어가므로 게이트를 건너뛴다. State만
            // 보면 그 한 프레임에 게이트가 번쩍인다 — ARTEL-152가 고친 깜박임.
            Assert.That(withKey.ShowGate, Is.False);
        }

        [Test]
        public void OverlayViewModel_ShowsGateAgainAfterFailureWithStoredKey()
        {
            ArtelInstanceKey.Save("H4KQ2-8VTRM-9XZ0C-N5JWE");
            var viewModel = CreateViewModel();
            viewModel.Initialize();

            // 설정되지 않은 Server는 요청을 만드는 중에 던진다. 404가 아니므로 저장 키가
            // 그대로 남는 실패 경로다.
            var registration = viewModel.Register(new Server(), "H4KQ2-8VTRM-9XZ0C-N5JWE", "sdk-uuid", "1.2.3", () => { });
            Assert.That(registration.MoveNext(), Is.False);

            Assert.That(viewModel.HasStoredKey, Is.True);
            Assert.That(viewModel.HasError, Is.True);

            // 이것이 없으면 사용자는 우상단 작은 패널을 스스로 발견해야 한다.
            Assert.That(viewModel.ShowGate, Is.True);
            Assert.That(viewModel.KeyInput, Is.EqualTo("H4KQ2-8VTRM-9XZ0C-N5JWE"));
        }

        [Test]
        public void OverlayGui_ShowsGateOnFirstLaunch()
        {
            WithOverlay((controller, canvas) =>
            {
                var cover = canvas.transform.Find("Artel Overlay Cover");
                Assert.That(cover.gameObject.activeSelf, Is.True);
                Assert.That(cover.Find("Gate Content").gameObject.activeSelf, Is.True);
                Assert.That(cover.Find("Progress Content").gameObject.activeSelf, Is.False);

                var field = canvas.GetComponentInChildren<InputField>(true);
                Assert.That(field.gameObject.activeInHierarchy, Is.True);
                Assert.That(field.interactable, Is.True);
            });
        }

        [Test]
        public void OverlayGui_HidesCoverWhenConnected()
        {
            WithOverlay((controller, canvas) =>
            {
                SetViewModelState(controller, ArtelConnectionState.Connected);

                // 이 행이 깨지면 게임 화면이 통째로 검게 남는다.
                Assert.That(canvas.transform.Find("Artel Overlay Cover").gameObject.activeSelf, Is.False);
            });
        }

        [Test]
        public void OverlayGui_HidesCoverWhenGateDismissed()
        {
            WithOverlay((controller, canvas) =>
            {
                var cover = canvas.transform.Find("Artel Overlay Cover");
                var buttons = canvas.GetComponentsInChildren<Button>(true);

                // 나중에가 없으면 등록이 계속 실패할 때 게임으로 돌아갈 길이 없다.
                Array.Find(buttons, button => button.name == "나중에 Button").onClick.Invoke();
                Assert.That(cover.gameObject.activeSelf, Is.False);

                // 반대 방향도 막혀 있다. 게이트가 내려가면 등록 버튼과 입력 필드도 함께
                // 비활성되므로, 이 버튼이 gateDismissed를 지우지 않으면 그 세션에서 SDK를
                // 다시 등록할 수 없다.
                Array.Find(buttons, button => button.name == "키 지우기 Button").onClick.Invoke();
                Assert.That(cover.gameObject.activeSelf, Is.True);
                Assert.That(cover.Find("Gate Content").gameObject.activeSelf, Is.True);
            });
        }

        [Test]
        public void RegisterButton_LabelMeetsContrastRatio()
        {
            WithOverlay((controller, canvas) =>
            {
                var registerButton = Array.Find(
                    canvas.GetComponentsInChildren<Button>(true),
                    button => button.name == "등록 Button");
                var label = registerButton.GetComponentInChildren<Text>(true);

                // 액센트가 밝아서 흰 라벨은 대비 기준을 넘지 못한다. 색값을 색값과 비교하는
                // 동어반복 대신 실제 불변식을 지킨다. 재색상해도 살아남는다.
                Assert.That(
                    ContrastRatio(registerButton.image.color, label.color),
                    Is.GreaterThanOrEqualTo(4.5f));
            });
        }

        [Test]
        public void OverlayGui_UsesBrandCoralOnlyForActionAccent()
        {
            WithOverlay((controller, canvas) =>
            {
                var logos = canvas.GetComponentsInChildren<ArtelLogoGraphic>(true);
                var registerButton = Array.Find(
                    canvas.GetComponentsInChildren<Button>(true),
                    button => button.name == "등록 Button");
                var checkmark = canvas.transform.Find("Artel Panel/Advanced Section/부드러운 커서 Toggle/Background/Checkmark")
                    .GetComponent<Image>();

                Assert.That(logos, Has.Length.EqualTo(2));
                Assert.That(registerButton.image.color, Is.EqualTo((Color)ArtelLogoGraphic.Coral));
                Assert.That(checkmark.color, Is.EqualTo((Color)new Color32(0x24, 0xC7, 0xE8, 0xFF)));
                Assert.That(ArtelLogoGraphic.Charcoal, Is.EqualTo(new Color32(0x20, 0x23, 0x2B, 0xFF)));
                Assert.That(ArtelLogoGraphic.Coral, Is.EqualTo(new Color32(0xF0, 0x4B, 0x3A, 0xFF)));
            });
        }

        [Test]
        public void ArtelLogoGraphic_DrawsFiveCharcoalSegmentsAndOneCoralSegment()
        {
            var logoObject = new GameObject("Artel logo mesh test", typeof(RectTransform), typeof(ArtelLogoGraphic));
            var vertexHelper = new VertexHelper();

            try
            {
                logoObject.GetComponent<RectTransform>().sizeDelta = new Vector2(64f, 64f);
                typeof(ArtelLogoGraphic)
                    .GetMethod(
                        "OnPopulateMesh",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(VertexHelper) },
                        null)
                    .Invoke(logoObject.GetComponent<ArtelLogoGraphic>(), new object[] { vertexHelper });

                // 본체는 선분별 quad가 아니라 공유 miter join을 가진 하나의 strip이다.
                Assert.That(vertexHelper.currentVertCount, Is.EqualTo(16));
                Assert.That(vertexHelper.currentIndexCount, Is.EqualTo(36));

                var mesh = new Mesh();
                vertexHelper.FillMesh(mesh);
                Assert.That(Array.FindAll(mesh.colors32, color => color.Equals(ArtelLogoGraphic.Charcoal)), Has.Length.EqualTo(12));
                Assert.That(Array.FindAll(mesh.colors32, color => color.Equals(ArtelLogoGraphic.Coral)), Has.Length.EqualTo(4));

                var expectedControlPoints = new[]
                {
                    new Vector2(20f, -8f),
                    new Vector2(20f, 14f),
                    new Vector2(0f, 26f),
                    new Vector2(-20f, 14f),
                    new Vector2(-20f, -14f),
                    new Vector2(-2f, -24f),
                    new Vector2(4f, -24f),
                    new Vector2(20f, -14f)
                };
                var vertices = mesh.vertices;
                for (var index = 0; index < expectedControlPoints.Length; index++)
                {
                    var midpoint = ((Vector2)vertices[index * 2] + (Vector2)vertices[(index * 2) + 1]) * 0.5f;
                    Assert.That(midpoint.x, Is.EqualTo(expectedControlPoints[index].x).Within(0.001f));
                    Assert.That(midpoint.y, Is.EqualTo(expectedControlPoints[index].y).Within(0.001f));
                }

                UnityEngine.Object.DestroyImmediate(mesh);
            }
            finally
            {
                vertexHelper.Dispose();
                UnityEngine.Object.DestroyImmediate(logoObject);
            }
        }

        [UnityTest]
        public IEnumerator OverlayGui_ScansScenesOnceAndReusesTheReport()
        {
            var host = new GameObject("Artel scan cache test");
            host.AddComponent<ArtelManager>();
            var controller = host.AddComponent<ArtelOverlayController>();

            try
            {
                InvokeLifecycle(controller, "Awake");
                InvokeLifecycle(controller, "Start");

                yield return RunRegistration(controller);
                var firstReport = CachedSceneScan(controller);
                Assert.That(firstReport, Is.Not.Null);

                // 등록이 실패해 다시 시도하는 경로. 캐시가 없으면 여기서 전체 씬을 다시
                // 걷는다 — 씬 수만큼 몇 초씩.
                yield return RunRegistration(controller);
                Assert.That(CachedSceneScan(controller), Is.SameAs(firstReport));
            }
            finally
            {
                var canvas = GameObject.Find("Artel Overlay Canvas");
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

        // StartCoroutine은 EditMode에서 돌지 않으므로 코루틴을 직접 꺼내 펌프한다.
        private static IEnumerator RunRegistration(ArtelOverlayController controller)
        {
            var coroutine = (IEnumerator)controller.GetType()
                .GetMethod("ScanScenesThenRegister", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            while (coroutine.MoveNext())
            {
                yield return coroutine.Current;
            }
        }

        private static object CachedSceneScan(ArtelOverlayController controller)
        {
            return controller.GetType()
                .GetField("cachedSceneScan", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
        }

        private static float ContrastRatio(Color a, Color b)
        {
            var lighter = Mathf.Max(RelativeLuminance(a), RelativeLuminance(b));
            var darker = Mathf.Min(RelativeLuminance(a), RelativeLuminance(b));
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        private static float RelativeLuminance(Color color)
        {
            return (0.2126f * Linearize(color.r)) +
                   (0.7152f * Linearize(color.g)) +
                   (0.0722f * Linearize(color.b));
        }

        private static float Linearize(float channel)
        {
            return channel <= 0.03928f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }

        // State는 private 세터이고 Connected로 가는 유일한 길이 실제 HTTP 등록 성공이므로,
        // 컨트롤러의 viewModel을 꺼내 직접 넣는다. InvokeLifecycle과 같은 리플렉션 시임이다.
        private static void SetViewModelState(object controller, ArtelConnectionState state)
        {
            var viewModel = controller.GetType()
                .GetField("viewModel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
            viewModel.GetType()
                .GetProperty("State", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(viewModel, state);
            InvokeLifecycle(controller, "RefreshView");
        }

        private static void WithOverlay(Action<ArtelOverlayController, GameObject> assertions)
        {
            var host = new GameObject("Artel overlay gate test");

            // 매니저는 RequireComponent를 채우려고만 붙인다. 매니저의 Awake는
            // DontDestroyOnLoad를 부르는데 그건 플레이 모드 전용이라 여기서 돌릴 수 없다.
            host.AddComponent<ArtelManager>();
            var controller = host.AddComponent<ArtelOverlayController>();

            try
            {
                InvokeLifecycle(controller, "Awake");
                InvokeLifecycle(controller, "Start");
                assertions(controller, GameObject.Find("Artel Overlay Canvas"));
            }
            finally
            {
                var canvas = GameObject.Find("Artel Overlay Canvas");
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

        private static ArtelOverlayViewModel CreateViewModel()
        {
            return new ArtelOverlayViewModel(
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
