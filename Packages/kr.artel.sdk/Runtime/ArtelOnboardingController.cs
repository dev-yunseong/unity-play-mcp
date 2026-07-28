using System.Collections;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Artel
{
    [RequireComponent(typeof(ArtelManager))]
    public sealed class ArtelOnboardingController : MonoBehaviour
    {
        private const int InstanceKeyCharacterLimit = 24;

        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.94f);

        // 덮개는 뒤를 비추면 안 된다. 알파가 1보다 작으면 가리려던 씬 전환이 그대로 비친다.
        private static readonly Color CoverColor = new Color(0.05f, 0.06f, 0.08f, 1f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.45f, 0.85f, 1f);
        private static readonly Color FieldColor = new Color(0.16f, 0.18f, 0.24f, 1f);
        private static readonly Color PlaceholderColor = new Color(0.62f, 0.65f, 0.72f, 1f);

        [SerializeField] private ArtelManager artelManager;

        private GameObject canvasObject;
        private GameObject createdEventSystem;
        private GameObject panelObject;
        private GameObject advancedObject;
        private GameObject coverObject;
        private InputField instanceKeyField;
        private Button registerButton;
        private Button connectButton;
        private Text statusText;
        private Text coverStatusText;
        private Text coverProgressText;
        private bool appliedShowPanel;
        private bool registrationRunning;
        private ArtelOnboardingViewModel viewModel;

        private void Awake()
        {
            if (artelManager == null)
            {
                artelManager = GetComponent<ArtelManager>();
            }

            viewModel = new ArtelOnboardingViewModel(
                new ArtelSdkRegistrationClient(new NewtonsoftJsonCodec()));
            viewModel.Changed += RefreshView;
        }

        private void Start()
        {
            viewModel.Initialize();
            CreateGui();
            RefreshView();

            if (viewModel.HasStoredKey)
            {
                RegisterInstanceKey();
            }
        }

        private void OnDestroy()
        {
            if (canvasObject != null)
            {
                Destroy(canvasObject);
            }

            if (createdEventSystem != null)
            {
                Destroy(createdEventSystem);
            }

            if (viewModel != null)
            {
                viewModel.Changed -= RefreshView;
            }
        }

        private void RegisterInstanceKey()
        {
            // viewModel은 스캔이 끝나고 Register에 들어가야 Registering이 된다. 그때까지
            // CanRegister가 살아 있으므로, 이 가드가 없으면 등록을 연타한 만큼 씬 워크가
            // 겹쳐 돈다.
            if (registrationRunning)
            {
                return;
            }

            StartCoroutine(ScanScenesThenRegister());
        }

        // 스캔이 빌드 내 씬을 하나씩 로드했다 내리므로 등록은 그만큼 늦게 시작한다.
        private IEnumerator ScanScenesThenRegister()
        {
            registrationRunning = true;

            // 씬 워크가 올린 씬은 실제로 그려진다. 등록이 끝날 때까지 화면을 덮어 두는 것이
            // 그 깜박임을 사람이 보지 않게 하는 유일한 방법이다. 오버레이 캔버스로 그리는
            // 다른 씬의 UI까지 가려야 하므로 카메라를 꺼서는 부족하다.
            ShowCover();
            try
            {
                SceneScanReportDto sceneScan = null;
                yield return SceneScanReporter.CreateReport(
                    report => sceneScan = report,
                    ShowScanProgress);

                ShowScanProgress(0, 0);
                yield return viewModel.Register(
                    artelManager.Server,
                    viewModel.KeyInput,
                    artelManager.SdkId,
                    artelManager.GameVersion,
                    artelManager.StartTransport,
                    sceneScan);
            }
            finally
            {
                registrationRunning = false;
                HideCover();
            }
        }

        private void ShowCover()
        {
            if (coverObject == null)
            {
                return;
            }

            coverProgressText.text = string.Empty;
            coverStatusText.text = viewModel.Status;
            coverObject.SetActive(true);
        }

        private void HideCover()
        {
            if (coverObject != null)
            {
                coverObject.SetActive(false);
            }
        }

        // 씬 수만큼 로드와 언로드가 쌓여 몇 초씩 걸린다. 진행 숫자가 없으면 덮개가 멈춘
        // 화면과 구분되지 않는다. sceneCount가 0이면 씬 워크가 끝났다는 뜻이다.
        private void ShowScanProgress(int sceneNumber, int sceneCount)
        {
            if (coverProgressText == null)
            {
                return;
            }

            coverProgressText.text = sceneCount <= 0
                ? string.Empty
                : "씬 " + sceneNumber + " / " + sceneCount;
        }

        private void ConnectWebSocket()
        {
            viewModel.Connect(artelManager.StartTransport);
        }

        private void CreateGui()
        {
            canvasObject = new GameObject("Artel Onboarding Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            // Parented to the manager so it rides along across scene loads. Left at
            // the scene root it is destroyed with that scene, and this controller —
            // which does survive — would be left holding a destroyed canvas and
            // never rebuild it. CursorController and KeyboardStatusController
            // already attach theirs the same way.
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            createdEventSystem = EnsureEventSystem(transform);

            var toggleButton = CreateButton(canvasObject.transform, "Artel", new Vector2(140f, 48f));
            AnchorTopRight(toggleButton.GetComponent<RectTransform>(), new Vector2(-24f, -24f));
            toggleButton.onClick.AddListener(() => panelObject.SetActive(!panelObject.activeSelf));

            panelObject = new GameObject("Onboarding Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            panelObject.GetComponent<Image>().color = PanelColor;
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-24f, -84f);
            panelRect.sizeDelta = new Vector2(440f, 400f);

            var title = CreateText(panelObject.transform, "Artel SDK", 24, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(20f, -16f), new Vector2(400f, 36f));

            instanceKeyField = CreateInputField(
                panelObject.transform,
                "대시보드에서 발급받은 키를 입력하세요",
                InstanceKeyCharacterLimit);
            SetRect(instanceKeyField.GetComponent<RectTransform>(), new Vector2(20f, -58f), new Vector2(400f, 44f));
            instanceKeyField.onValueChanged.AddListener(value => viewModel.KeyInput = value);

            registerButton = CreateButton(panelObject.transform, "등록", new Vector2(400f, 44f));
            SetRect(registerButton.GetComponent<RectTransform>(), new Vector2(20f, -110f), new Vector2(400f, 44f));
            registerButton.onClick.AddListener(RegisterInstanceKey);

            statusText = CreateText(panelObject.transform, string.Empty, 15, TextAnchor.UpperLeft);
            SetRect(statusText.rectTransform, new Vector2(20f, -162f), new Vector2(400f, 66f));

            var advancedButton = CreateButton(panelObject.transform, "고급", new Vector2(400f, 34f));
            SetRect(advancedButton.GetComponent<RectTransform>(), new Vector2(20f, -234f), new Vector2(400f, 34f));
            advancedButton.onClick.AddListener(() => advancedObject.SetActive(!advancedObject.activeSelf));

            CreateAdvancedSection();
            CreateCover();

            appliedShowPanel = viewModel.ShowPanel;
            panelObject.SetActive(appliedShowPanel);
        }

        private void CreateCover()
        {
            // 캔버스의 마지막 자식이므로 같은 캔버스의 패널 위에 그려지고, 이 캔버스의
            // sortingOrder가 short.MaxValue - 1이라 게임 쪽 캔버스보다도 위다. 정렬 순서
            // 상수를 건드리지 않고 화면을 덮기 위해 여기에 붙인다. 위에 남는 것은 가상
            // 커서 캔버스(short.MaxValue)뿐인데, 커서는 보이는 편이 맞다.
            coverObject = new GameObject("Scan Cover", typeof(RectTransform), typeof(Image));
            coverObject.transform.SetParent(canvasObject.transform, false);

            // raycastTarget이 켜진 채라 덮인 게임 UI로 클릭이 새지 않는다.
            coverObject.GetComponent<Image>().color = CoverColor;
            var coverRect = coverObject.GetComponent<RectTransform>();
            coverRect.anchorMin = Vector2.zero;
            coverRect.anchorMax = Vector2.one;
            coverRect.offsetMin = Vector2.zero;
            coverRect.offsetMax = Vector2.zero;

            var title = CreateText(coverObject.transform, "Artel SDK", 30, TextAnchor.MiddleCenter);
            CenterRect(title.rectTransform, new Vector2(0f, 52f), new Vector2(900f, 44f));

            var message = CreateText(
                coverObject.transform,
                "게임 화면을 분석하는 중입니다. 잠시만 기다려 주세요.",
                20,
                TextAnchor.MiddleCenter);
            CenterRect(message.rectTransform, new Vector2(0f, 4f), new Vector2(900f, 32f));

            coverProgressText = CreateText(coverObject.transform, string.Empty, 18, TextAnchor.MiddleCenter);
            CenterRect(coverProgressText.rectTransform, new Vector2(0f, -34f), new Vector2(900f, 28f));

            coverStatusText = CreateText(coverObject.transform, string.Empty, 16, TextAnchor.MiddleCenter);
            coverStatusText.color = PlaceholderColor;
            CenterRect(coverStatusText.rectTransform, new Vector2(0f, -70f), new Vector2(900f, 28f));

            coverObject.SetActive(false);
        }

        private void CreateAdvancedSection()
        {
            advancedObject = new GameObject("Advanced Section", typeof(RectTransform));
            advancedObject.transform.SetParent(panelObject.transform, false);
            SetRect(advancedObject.GetComponent<RectTransform>(), new Vector2(0f, -272f), new Vector2(440f, 128f));

            var details = CreateText(
                advancedObject.transform,
                "SDK UUID " + artelManager.SdkId + "\n게임 버전 " + artelManager.GameVersion,
                14,
                TextAnchor.UpperLeft);
            SetRect(details.rectTransform, new Vector2(20f, -8f), new Vector2(400f, 44f));

            var smoothCursorToggle = CreateToggle(advancedObject.transform, "부드러운 커서");
            SetRect(smoothCursorToggle.GetComponent<RectTransform>(), new Vector2(20f, -58f), new Vector2(200f, 32f));
            smoothCursorToggle.isOn = artelManager.SmoothCursorMovement;
            smoothCursorToggle.onValueChanged.AddListener(value => artelManager.SmoothCursorMovement = value);

            connectButton = CreateButton(advancedObject.transform, "연결", new Vector2(180f, 36f));
            SetRect(connectButton.GetComponent<RectTransform>(), new Vector2(240f, -56f), new Vector2(180f, 36f));
            connectButton.onClick.AddListener(ConnectWebSocket);

            var clearKeyButton = CreateButton(advancedObject.transform, "키 지우기", new Vector2(180f, 32f));
            SetRect(clearKeyButton.GetComponent<RectTransform>(), new Vector2(20f, -96f), new Vector2(180f, 32f));
            clearKeyButton.onClick.AddListener(viewModel.ClearStoredKey);

            advancedObject.SetActive(false);
        }

        private void RefreshView()
        {
            // 덮개가 GUI에서 마지막에 만들어지므로, 이것이 있으면 나머지도 다 있다.
            if (coverStatusText == null)
            {
                return;
            }

            if (instanceKeyField.text != viewModel.KeyInput)
            {
                instanceKeyField.text = viewModel.KeyInput;
            }

            statusText.text = viewModel.Status;
            coverStatusText.text = viewModel.Status;
            registerButton.interactable = viewModel.CanRegister;
            connectButton.interactable = viewModel.CanConnect;

            if (appliedShowPanel != viewModel.ShowPanel)
            {
                appliedShowPanel = viewModel.ShowPanel;
                panelObject.SetActive(appliedShowPanel);
            }
        }

        private static InputField CreateInputField(Transform parent, string placeholderLabel, int characterLimit)
        {
            var fieldObject = new GameObject(
                "인스턴스 키 InputField",
                typeof(RectTransform),
                typeof(Image),
                typeof(InputField));
            fieldObject.transform.SetParent(parent, false);
            var background = fieldObject.GetComponent<Image>();
            background.color = FieldColor;

            var text = CreateText(fieldObject.transform, string.Empty, 18, TextAnchor.MiddleLeft);
            text.name = "Text";
            text.supportRichText = false;
            StretchInside(text.rectTransform);

            var placeholder = CreateText(fieldObject.transform, placeholderLabel, 16, TextAnchor.MiddleLeft);
            placeholder.name = "Placeholder";
            placeholder.color = PlaceholderColor;
            placeholder.fontStyle = FontStyle.Italic;
            StretchInside(placeholder.rectTransform);

            var inputField = fieldObject.GetComponent<InputField>();
            inputField.targetGraphic = background;
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.characterLimit = characterLimit;
            inputField.text = string.Empty;
            return inputField;
        }

        private static Button CreateButton(Transform parent, string label, Vector2 size)
        {
            var buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;
            buttonObject.GetComponent<Image>().color = ButtonColor;

            var text = CreateText(buttonObject.transform, label, 17, TextAnchor.MiddleCenter);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return buttonObject.GetComponent<Button>();
        }

        private static Text CreateText(Transform parent, string value, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Toggle CreateToggle(Transform parent, string label)
        {
            var toggleObject = new GameObject(label + " Toggle", typeof(RectTransform), typeof(Toggle));
            toggleObject.transform.SetParent(parent, false);

            var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(toggleObject.transform, false);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            SetRect(backgroundRect, Vector2.zero, new Vector2(28f, 28f));
            var background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.22f, 0.24f, 0.3f, 1f);

            var checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkObject.transform.SetParent(backgroundObject.transform, false);
            var checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkmarkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;
            var checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.color = ButtonColor;

            var text = CreateText(toggleObject.transform, label, 16, TextAnchor.MiddleLeft);
            SetRect(text.rectTransform, new Vector2(40f, 0f), new Vector2(180f, 28f));

            var toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            return toggle;
        }

        private static void AnchorTopRight(RectTransform rectTransform, Vector2 position)
        {
            rectTransform.anchorMin = new Vector2(1f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(1f, 1f);
            rectTransform.anchoredPosition = position;
        }

        private static void SetRect(RectTransform rectTransform, Vector2 position, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;
        }

        private static void CenterRect(RectTransform rectTransform, Vector2 position, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;
        }

        private static void StretchInside(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(12f, 6f);
            rectTransform.offsetMax = new Vector2(-12f, -6f);
        }

        private static GameObject EnsureEventSystem(Transform owner)
        {
            if (EventSystem.current != null)
            {
                return null;
            }

            var eventSystem = new GameObject(
                "Artel EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            // Also parented to the manager: a scene that arrives without an
            // EventSystem leaves the UI unclickable, and the one we made for that
            // case must not disappear with the scene we made it in.
            eventSystem.transform.SetParent(owner, false);
            return eventSystem;
        }
    }
}
