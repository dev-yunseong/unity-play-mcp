using System.Collections;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Artel
{
    [RequireComponent(typeof(ArtelManager))]
    public sealed class ArtelOverlayController : MonoBehaviour
    {
        private const int InstanceKeyCharacterLimit = 24;

        // artel-home의 src/styles/tokens.css에서 가져온 값. CSS를 C#으로 자동 동기화할
        // 수단이 없으므로, 16진 리터럴로 적어 두는 것이 원본과 대조하는 유일한 방법이다.
        // Color32를 쓰는 이유는 컴파일 타임에 걸리기 때문이다. ColorUtility로 파싱하면
        // 오타가 런타임에 조용히 잘못된 색이 된다.
        private static readonly Color BgSurface = new Color32(0x10, 0x15, 0x1B, 0xFF);
        private static readonly Color BgRaised = new Color32(0x17, 0x1D, 0x25, 0xFF);
        private static readonly Color BorderStrong = new Color32(0x3B, 0x48, 0x57, 0xFF);
        private static readonly Color TextPrimary = new Color32(0xF4, 0xF7, 0xFA, 0xFF);
        private static readonly Color TextSecondary = new Color32(0xA7, 0xB0, 0xBC, 0xFF);
        private static readonly Color TextMuted = new Color32(0x70, 0x7B, 0x88, 0xFF);
        private static readonly Color ActionPrimary = new Color32(0x24, 0xC7, 0xE8, 0xFF);
        private static readonly Color StatusCritical = new Color32(0xFF, 0x63, 0x4F, 0xFF);
        private static readonly Color StatusSuccess = new Color32(0x48, 0xC7, 0x8E, 0xFF);

        // --color-bg-canvas. primary 버튼의 글자색이기도 하다. #24C7E8이 밝아서 흰 글자는
        // 대비 기준을 넘지 못한다. artel-home의 .button--primary도 같은 이유로
        // color: var(--color-bg-canvas)를 쓴다.
        private static readonly Color BgCanvas = new Color32(0x09, 0x0C, 0x10, 0xFF);

        // 덮개는 뒤를 비추면 안 된다. 알파가 1보다 작으면 가리려던 씬 전환이 그대로 비친다.
        // 그래서 --color-overlay-scrim(72%)이나 -stream(88%)을 쓸 수 없고 BgCanvas를
        // 알파 1로 쓴다.
        private static readonly Color CoverColor = BgCanvas;

        [SerializeField] private ArtelManager artelManager;

        private GameObject canvasObject;
        private GameObject createdEventSystem;
        private GameObject panelObject;
        private GameObject advancedObject;
        private GameObject coverObject;
        private GameObject gateContent;
        private GameObject progressContent;
        private InputField instanceKeyField;
        private Button registerButton;
        private Button connectButton;
        private Text statusText;
        private Text gateErrorText;
        private Text coverStatusText;
        private Text coverProgressText;
        private bool appliedShowPanel;
        private bool appliedShowGate;
        private bool registrationRunning;

        // 게이트를 이 세션에서 내려둘지. 덮개가 우상단 패널과 고급 섹션을 전부 덮으므로,
        // 등록이 계속 실패할 때 이것이 게임으로 돌아가는 유일한 길이다. 되돌아오는 길은
        // 고급 섹션의 키 지우기·연결이며, 둘 다 이 값을 지운다.
        private bool gateDismissed;
        private ArtelOverlayViewModel viewModel;

        private void Awake()
        {
            if (artelManager == null)
            {
                artelManager = GetComponent<ArtelManager>();
            }

            viewModel = new ArtelOverlayViewModel(
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
            gateDismissed = false;

            // 씬 워크가 올린 씬은 실제로 그려진다. 등록이 끝날 때까지 화면을 덮어 두는 것이
            // 그 깜박임을 사람이 보지 않게 하는 유일한 방법이다. 오버레이 캔버스로 그리는
            // 다른 씬의 UI까지 가려야 하므로 카메라를 꺼서는 부족하다.
            //
            // registrationRunning은 RefreshView가 읽는다. 뒤집은 직후 직접 불러야 하는데,
            // 이 플래그는 컨트롤러 로컬이라 viewModel.Changed가 뜨지 않는다.
            coverProgressText.text = string.Empty;
            RefreshView();
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
                RefreshView();
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

        // 나중에로 게이트를 내리면 등록 버튼과 입력 필드도 함께 비활성된다. 그래서 게이트로
        // 되돌아오는 길은 이 두 버튼뿐이고, 둘 다 gateDismissed를 지워야 한다. 연결이 있는
        // 이유는 키를 버리지 않고 재시도할 길을 남기는 것이다 — 키 지우기만 남기면 24자를
        // 다시 쳐야 한다.
        private void ConnectWebSocket()
        {
            gateDismissed = false;
            viewModel.Connect(artelManager.StartTransport);
            RefreshView();
        }

        private void ClearStoredKey()
        {
            gateDismissed = false;
            viewModel.ClearStoredKey();
            RefreshView();
        }

        private void DismissGate()
        {
            gateDismissed = true;
            RefreshView();
        }

        private void CreateGui()
        {
            canvasObject = new GameObject("Artel Overlay Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

            panelObject = new GameObject("Artel Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            panelObject.GetComponent<Image>().color = BgSurface;
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-24f, -84f);
            panelRect.sizeDelta = new Vector2(440f, 300f);

            // 키 입력은 게이트가 소유한다. 같은 KeyInput을 두 위젯이 물면 동기화 문제가
            // 생기고, 무엇보다 입력 지점이 둘이면 처음 쓰는 사람이 어느 쪽을 봐야 할지
            // 모른다. 패널에는 상태 문구와 고급 섹션만 남는다.
            var title = CreateText(panelObject.transform, "Artel SDK", 24, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(20f, -16f), new Vector2(400f, 36f));

            statusText = CreateText(panelObject.transform, string.Empty, 15, TextAnchor.UpperLeft, TextSecondary);
            SetRect(statusText.rectTransform, new Vector2(20f, -60f), new Vector2(400f, 66f));

            var advancedButton = CreateButton(panelObject.transform, "고급", new Vector2(400f, 34f));
            SetRect(advancedButton.GetComponent<RectTransform>(), new Vector2(20f, -132f), new Vector2(400f, 34f));
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
            coverObject = new GameObject("Artel Overlay Cover", typeof(RectTransform), typeof(Image));
            coverObject.transform.SetParent(canvasObject.transform, false);

            // raycastTarget이 켜진 채라 덮인 게임 UI로 클릭이 새지 않는다.
            coverObject.GetComponent<Image>().color = CoverColor;
            var coverRect = coverObject.GetComponent<RectTransform>();
            coverRect.anchorMin = Vector2.zero;
            coverRect.anchorMax = Vector2.one;
            coverRect.offsetMin = Vector2.zero;
            coverRect.offsetMax = Vector2.zero;

            CreateProgressContent();
            CreateGateContent();

            coverObject.SetActive(false);
        }

        private void CreateProgressContent()
        {
            progressContent = new GameObject("Progress Content", typeof(RectTransform));
            progressContent.transform.SetParent(coverObject.transform, false);
            Inset(progressContent.GetComponent<RectTransform>(), 0f);

            var title = CreateText(progressContent.transform, "Artel SDK", 30, TextAnchor.MiddleCenter);
            CenterRect(title.rectTransform, new Vector2(0f, 52f), new Vector2(900f, 44f));

            var message = CreateText(
                progressContent.transform,
                "게임 화면을 분석하는 중입니다. 잠시만 기다려 주세요.",
                20,
                TextAnchor.MiddleCenter,
                TextSecondary);
            CenterRect(message.rectTransform, new Vector2(0f, 4f), new Vector2(900f, 32f));

            coverProgressText = CreateText(
                progressContent.transform, string.Empty, 18, TextAnchor.MiddleCenter, TextMuted);
            CenterRect(coverProgressText.rectTransform, new Vector2(0f, -34f), new Vector2(900f, 28f));

            coverStatusText = CreateText(
                progressContent.transform, string.Empty, 16, TextAnchor.MiddleCenter, TextMuted);
            CenterRect(coverStatusText.rectTransform, new Vector2(0f, -70f), new Vector2(900f, 28f));
        }

        // 게임 화면 거리에서 읽히도록 artel-home의 타이포보다 한 단계 크게 잡는다.
        //
        // 배치는 VerticalLayoutGroup이 아니라 CenterRect 고정 좌표다. 레이아웃 그룹은
        // childControlHeight/childForceExpandHeight가 기본 true인데 InputField와 스프라이트
        // 없는 Image/Button은 ILayoutElement를 구현하지 않아(Text만 한다) ContentSizeFitter
        // 아래에서 높이가 0으로 접힌다. 요소마다 LayoutElement를 붙이는 쪽이 더 길다.
        private void CreateGateContent()
        {
            gateContent = new GameObject("Gate Content", typeof(RectTransform));
            gateContent.transform.SetParent(coverObject.transform, false);
            Inset(gateContent.GetComponent<RectTransform>(), 0f);

            var title = CreateText(gateContent.transform, "Artel SDK", 32, TextAnchor.MiddleCenter);
            CenterRect(title.rectTransform, new Vector2(0f, 148f), new Vector2(900f, 48f));

            var message = CreateText(
                gateContent.transform,
                "대시보드에서 발급받은 인스턴스 키를 입력하면 연결됩니다.",
                18,
                TextAnchor.MiddleCenter,
                TextSecondary);
            CenterRect(message.rectTransform, new Vector2(0f, 96f), new Vector2(900f, 28f));

            instanceKeyField = CreateInputField(
                gateContent.transform,
                "대시보드에서 발급받은 키를 입력하세요",
                InstanceKeyCharacterLimit,
                fontSize: 20);
            CenterRect(instanceKeyField.GetComponent<RectTransform>(), new Vector2(0f, 32f), new Vector2(640f, 64f));
            instanceKeyField.onValueChanged.AddListener(value => viewModel.KeyInput = value);
            instanceKeyField.onEndEdit.AddListener(SubmitOnEnter);

            // 오류 줄은 빈 문자열로도 자리를 차지한다. 나타날 때 아래 버튼이 밀리면 누르려던
            // 위치가 어긋난다.
            gateErrorText = CreateText(
                gateContent.transform, string.Empty, 16, TextAnchor.MiddleCenter, StatusCritical);
            CenterRect(gateErrorText.rectTransform, new Vector2(0f, -18f), new Vector2(640f, 24f));

            registerButton = CreateButton(gateContent.transform, "등록", new Vector2(640f, 56f), primary: true);
            CenterRect(registerButton.GetComponent<RectTransform>(), new Vector2(0f, -64f), new Vector2(640f, 56f));
            registerButton.onClick.AddListener(RegisterInstanceKey);

            var dismissButton = CreateButton(gateContent.transform, "나중에", new Vector2(640f, 44f));
            CenterRect(dismissButton.GetComponent<RectTransform>(), new Vector2(0f, -124f), new Vector2(640f, 44f));
            dismissButton.onClick.AddListener(DismissGate);
        }

        // 덮개는 화면을 다 채운 raycastTarget이라 배경을 클릭해도 포커스가 풀리고 onEndEdit이
        // 뜬다. 실제 Enter인지 확인하지 않으면 몇 글자 치고 배경을 누른 사람이 요청하지도
        // 않은 등록 실패를 본다. 이 어셈블리는 IL 위버가 건너뛰므로(ArtelILPostProcessor)
        // 여기의 Input은 실제 UnityEngine.Input이고 원격 에이전트의 가상 키보드로는 눌리지
        // 않는다. 셋업 화면이므로 그게 맞다.
        private void SubmitOnEnter(string value)
        {
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                return;
            }

            if (viewModel.CanRegister)
            {
                RegisterInstanceKey();
            }
        }

        private void CreateAdvancedSection()
        {
            advancedObject = new GameObject("Advanced Section", typeof(RectTransform));
            advancedObject.transform.SetParent(panelObject.transform, false);
            SetRect(advancedObject.GetComponent<RectTransform>(), new Vector2(0f, -170f), new Vector2(440f, 128f));

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
            clearKeyButton.onClick.AddListener(ClearStoredKey);

            advancedObject.SetActive(false);
        }

        private void RefreshView()
        {
            // 게이트 콘텐츠가 GUI에서 마지막에 만들어지므로, 이것이 있으면 나머지도 다 있다.
            if (gateErrorText == null)
            {
                return;
            }

            if (instanceKeyField.text != viewModel.KeyInput)
            {
                instanceKeyField.text = viewModel.KeyInput;
            }

            statusText.text = viewModel.Status;
            // 실패를 문장으로만 알리면 눈에 걸리지 않는다.
            statusText.color = StatusColor();
            coverStatusText.text = viewModel.Status;
            gateErrorText.text = viewModel.HasError ? viewModel.Status : string.Empty;
            registerButton.interactable = viewModel.CanRegister;
            connectButton.interactable = viewModel.CanConnect;

            // 덮개와 두 콘텐츠 그룹의 쓰기 주체는 여기 하나다. 코루틴이 따로 켜고 끄면
            // 어느 한쪽 경로에서 덮개가 켜진 채 남아 게임 화면을 통째로 가린다.
            //
            // registrationRunning이 콘텐츠 선택에 들어가는 이유: 스캔은 State가 아직
            // NeedsKey인 채로 몇 초 돈다. ShowGate만 보면 그 동안 게이트가 등록 버튼을 켠 채
            // 얼어 있고 진행 숫자는 꺼진 그룹에 써진다.
            var showGate = viewModel.ShowGate && !registrationRunning && !gateDismissed;
            coverObject.SetActive(showGate || registrationRunning);
            gateContent.SetActive(showGate);
            progressContent.SetActive(registrationRunning);

            // 패널을 매 Changed마다 덮어쓰면 Artel 토글이 무력화된다 — 직접 열어둔 패널이
            // 다음 상태 변화에 닫혀버린다. 그래서 전이에서만 쓴다.
            if (appliedShowPanel != viewModel.ShowPanel)
            {
                appliedShowPanel = viewModel.ShowPanel;
                panelObject.SetActive(appliedShowPanel);
            }

            // ActivateInputField는 멱등하지 않다. RefreshView는 키 입력마다 도므로
            // (KeyInput 세터가 Changed를 쏜다) 매번 부르면 캐럿과 선택이 초기화되어
            // 타이핑이 깨진다.
            if (appliedShowGate != showGate)
            {
                appliedShowGate = showGate;
                if (showGate)
                {
                    instanceKeyField.ActivateInputField();
                }
            }
        }

        private Color StatusColor()
        {
            if (viewModel.State == ArtelConnectionState.Connected)
            {
                return StatusSuccess;
            }

            return viewModel.HasError ? StatusCritical : TextSecondary;
        }

        private static InputField CreateInputField(
            Transform parent, string placeholderLabel, int characterLimit, int fontSize = 18)
        {
            var fieldObject = new GameObject(
                "인스턴스 키 InputField",
                typeof(RectTransform),
                typeof(Image),
                typeof(InputField));
            fieldObject.transform.SetParent(parent, false);

            // 테두리는 겉 Image, 배경은 1유닛 들여 깐 자식 Image. 텍스트 자식들을 이 뒤에
            // 만들어야 배경 위에 그려진다.
            var background = fieldObject.GetComponent<Image>();
            background.color = BorderStrong;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fieldObject.transform, false);
            fill.GetComponent<Image>().color = BgRaised;
            Inset(fill.GetComponent<RectTransform>(), 1f);

            var text = CreateText(fieldObject.transform, string.Empty, fontSize, TextAnchor.MiddleLeft);
            text.name = "Text";
            text.supportRichText = false;
            StretchInside(text.rectTransform);

            var placeholder = CreateText(
                fieldObject.transform, placeholderLabel, fontSize - 2, TextAnchor.MiddleLeft);
            placeholder.name = "Placeholder";
            placeholder.color = TextMuted;
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

        // primary는 화면에서 지금 눌러야 하는 버튼 하나에만 쓴다. 나머지는 secondary로
        // 물러나 있어야 그 하나가 눈에 띈다. artel-home의 .button--primary /
        // .button--secondary와 같은 구분이다.
        private static Button CreateButton(Transform parent, string label, Vector2 size, bool primary = false)
        {
            var buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;

            if (primary)
            {
                buttonObject.GetComponent<Image>().color = ActionPrimary;
            }
            else
            {
                // 테두리는 겉 Image를 테두리색으로 두고 안쪽에 배경색 Image를 1유닛 들여
                // 깔아 낸다. uGUI Image에는 테두리 속성이 없다.
                buttonObject.GetComponent<Image>().color = BorderStrong;
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(buttonObject.transform, false);
                fill.GetComponent<Image>().color = BgRaised;
                Inset(fill.GetComponent<RectTransform>(), 1f);
            }

            var text = CreateText(
                buttonObject.transform,
                label,
                17,
                TextAnchor.MiddleCenter,
                primary ? BgCanvas : TextPrimary);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return buttonObject.GetComponent<Button>();
        }

        private static void Inset(RectTransform rectTransform, float amount)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(amount, amount);
            rectTransform.offsetMax = new Vector2(-amount, -amount);
        }

        private static Text CreateText(
            Transform parent, string value, int fontSize, TextAnchor alignment, Color? color = null)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color ?? TextPrimary;
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
            background.color = BgRaised;

            var checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkObject.transform.SetParent(backgroundObject.transform, false);
            var checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkmarkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;
            var checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.color = ActionPrimary;

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
