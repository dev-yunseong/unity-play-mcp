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
        private static readonly Color ButtonColor = new Color(0.18f, 0.45f, 0.85f, 1f);
        private static readonly Color FieldColor = new Color(0.16f, 0.18f, 0.24f, 1f);
        private static readonly Color PlaceholderColor = new Color(0.62f, 0.65f, 0.72f, 1f);

        [SerializeField] private ArtelManager artelManager;

        private GameObject canvasObject;
        private GameObject createdEventSystem;
        private GameObject panelObject;
        private GameObject advancedObject;
        private InputField instanceKeyField;
        private Button registerButton;
        private Button connectButton;
        private Text statusText;
        private bool appliedShowPanel;
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
            StartCoroutine(viewModel.Register(
                artelManager.Server,
                viewModel.KeyInput,
                artelManager.SdkId,
                artelManager.GameVersion,
                artelManager.StartTransport));
        }

        private void ConnectWebSocket()
        {
            viewModel.Connect(artelManager.StartTransport);
        }

        private void CreateGui()
        {
            canvasObject = new GameObject("Artel Onboarding Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            createdEventSystem = EnsureEventSystem();

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

            appliedShowPanel = viewModel.ShowPanel;
            panelObject.SetActive(appliedShowPanel);
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
            if (statusText == null)
            {
                return;
            }

            if (instanceKeyField.text != viewModel.KeyInput)
            {
                instanceKeyField.text = viewModel.KeyInput;
            }

            statusText.text = viewModel.Status;
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

        private static void StretchInside(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(12f, 6f);
            rectTransform.offsetMax = new Vector2(-12f, -6f);
        }

        private static GameObject EnsureEventSystem()
        {
            if (EventSystem.current == null)
            {
                return new GameObject("Artel EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            return null;
        }
    }
}
