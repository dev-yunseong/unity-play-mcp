using System.Collections;
using System.Collections.Generic;
using Artel.Auth;
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
        private const string DarkThemePlayerPrefsKey = ArtelOwnedPlayerPrefs.DarkTheme;

        // 게이트가 세로로 그릴 수 있는 프로젝트 수. 고정 좌표 배치라 이보다 많으면 화면을
        // 벗어난다.
        // ponytail: 프로젝트가 이보다 많은 사람이 나오면 그때 스크롤을 붙인다.
        private const int MaxListedProjects = 4;
        private const float ProjectRowHeight = 60f;

        private const string LoginMessage = "Artel 계정으로 로그인하면 연결됩니다.";
        private const string ChooseProjectMessage = "이 게임을 연결할 프로젝트를 선택해 주세요.";

        // artel-home의 src/styles/tokens.css(Blueprint Paper)에서 가져온 값. CSS를 C#으로 자동 동기화할
        // 수단이 없으므로, 16진 리터럴로 적어 두는 것이 원본과 대조하는 유일한 방법이다.
        // Color32를 쓰는 이유는 컴파일 타임에 걸리기 때문이다. ColorUtility로 파싱하면
        // 오타가 런타임에 조용히 잘못된 색이 된다.
        private Color bgSurface;
        private Color bgRaised;
        private Color borderStrong;
        private Color textPrimary;
        private Color textSecondary;
        private Color textMuted;
        private Color bgCanvas;
        private Color coverColor;
        // 브랜드·action 색은 테마에 따라 달라지므로 static일 수 없다. 실패·성공은
        // 의미 색이라 두 테마에서 같은 값을 유지한다.
        private Color brandAccent;
        private Color actionPrimary;
        private Color textOnAccent;
        private static readonly Color StatusCritical = new Color32(0xFF, 0x63, 0x4F, 0xFF);
        private static readonly Color StatusSuccess = new Color32(0x48, 0xC7, 0x8E, 0xFF);

        [SerializeField] private ArtelManager artelManager;

        private GameObject canvasObject;
        private GameObject createdEventSystem;
        private GameObject panelObject;
        private GameObject advancedObject;
        private GameObject coverObject;
        private GameObject gateContent;
        private GameObject progressContent;
        private GameObject projectListObject;
        private Button loginButton;
        private Button reloadProjectsButton;
        private Button gateLogOutButton;
        private Button connectButton;
        private Text statusText;
        private Text accountText;
        private Text gateMessageText;
        private Text gateErrorText;
        private Text coverMessageText;
        private Text coverStatusText;
        private Text coverProgressText;
        private bool appliedShowPanel;
        private bool registrationRunning;
        private bool loginRunning;
        private bool darkTheme;

        // 그려 둔 프로젝트 버튼의 id 목록. RefreshView는 상태가 바뀔 때마다 도므로, 목록이
        // 실제로 달라졌을 때만 다시 만들어야 누르려던 버튼이 손 밑에서 사라지지 않는다.
        private readonly List<string> listedProjectIds = new List<string>();

        // 게이트를 이 세션에서 내려둘지. 덮개가 우상단 패널과 고급 섹션을 전부 덮으므로,
        // 등록이 계속 실패할 때 이것이 게임으로 돌아가는 유일한 길이다. 되돌아오는 길은
        // 고급 섹션의 키 지우기·연결이며, 둘 다 이 값을 지운다.
        private bool gateDismissed;

        // 프로세스가 사는 동안 한 번만 걷는다. ScanScenesThenRegister 참조.
        private SceneScanReportDto cachedSceneScan;
        private ArtelOverlayViewModel viewModel;

        private void Awake()
        {
            if (artelManager == null)
            {
                artelManager = GetComponent<ArtelManager>();
            }

            var jsonCodec = new NewtonsoftJsonCodec();
            viewModel = new ArtelOverlayViewModel(
                new ArtelSdkRegistrationClient(jsonCodec),
                new ArtelSdkAuthClient(jsonCodec),
                jsonCodec);
            viewModel.Changed += RefreshView;
            darkTheme = PlayerPrefs.GetInt(DarkThemePlayerPrefsKey, 1) != 0;
            ApplyPalette();
        }

        private void Start()
        {
            viewModel.Initialize();
            CreateGui();
            RefreshView();

            if (viewModel.HasStoredSession)
            {
                RegisterInstance();
                return;
            }

            // 토큰만 남고 프로젝트가 없는 경우. 목록을 읽어야 고를 수 있고, 하나뿐이면
            // 고르는 화면 없이 그대로 등록으로 이어진다.
            if (viewModel.HasToken)
            {
                StartCoroutine(LoadProjectsThenRegister());
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

        private void RegisterInstance()
        {
            // viewModel은 스캔이 끝나고 Register에 들어가야 Registering이 된다. 그때까지
            // 프로젝트 버튼이 살아 있으므로, 이 가드가 없으면 연타한 만큼 씬 워크가 겹쳐 돈다.
            if (registrationRunning)
            {
                return;
            }

            StartCoroutine(ScanScenesThenRegister());
        }

        private void BeginLogin()
        {
            if (loginRunning || registrationRunning)
            {
                return;
            }

            StartCoroutine(LogInThenRegister());
        }

        private IEnumerator LogInThenRegister()
        {
            loginRunning = true;
            gateDismissed = false;
            ShowCoverMessage("브라우저에서 로그인을 완료해 주세요.");
            try
            {
                yield return viewModel.LogIn(artelManager.Server);
            }
            finally
            {
                loginRunning = false;
                RefreshView();
            }

            // 프로젝트가 하나뿐이면 LogIn이 그 자리에서 골라 둔다. 그럴 때만 이어서 등록한다.
            if (viewModel.HasStoredSession)
            {
                RegisterInstance();
            }
        }

        private IEnumerator LoadProjectsThenRegister()
        {
            loginRunning = true;
            ShowCoverMessage("프로젝트 목록을 불러오는 중입니다.");
            try
            {
                yield return viewModel.LoadProjects(artelManager.Server);
            }
            finally
            {
                loginRunning = false;
                RefreshView();
            }

            if (viewModel.HasStoredSession)
            {
                RegisterInstance();
            }
        }

        private void ReloadProjects()
        {
            if (loginRunning || registrationRunning)
            {
                return;
            }

            gateDismissed = false;
            StartCoroutine(LoadProjectsThenRegister());
        }

        private void ChooseProject(string projectId)
        {
            gateDismissed = false;
            viewModel.SelectProject(projectId);
            RegisterInstance();
        }

        // 첫 등록은 씬 워크가 끝날 때까지 늦게 시작한다. 두 번째부터는 캐시를 쓴다.
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
            ShowCoverMessage(cachedSceneScan == null
                ? "게임 화면을 분석하는 중입니다. 잠시만 기다려 주세요."
                : "인스턴스를 등록하는 중입니다. 잠시만 기다려 주세요.");
            RefreshView();
            try
            {
                // 스캔은 씬을 하나씩 로드했다 내리므로 씬 수만큼 몇 초씩 걸린다. 빌드에 담긴
                // 씬은 프로세스가 사는 동안 바뀌지 않으니 한 번만 걷고 재사용한다. 등록이
                // 실패해 다시 시도할 때 이 캐시가 없으면 매번 전체 씬을 다시 걷는다.
                //
                // ponytail: 에디터에서 플레이 중에 씬을 편집하면 캐시가 낡는다. 플레이를
                // 다시 시작하면 지워지므로 그대로 둔다. 런타임 무효화가 필요해지면
                // AllSceneScanner 쪽에 변경 신호를 만들어야 한다.
                if (cachedSceneScan == null)
                {
                    yield return SceneScanReporter.CreateReport(
                        report => cachedSceneScan = report,
                        ShowScanProgress);

                    ShowScanProgress(0, 0);
                }

                yield return viewModel.Register(
                    artelManager.Server,
                    artelManager.SdkId,
                    artelManager.InstanceName,
                    artelManager.GameVersion,
                    artelManager.StartTransport,
                    cachedSceneScan);
            }
            finally
            {
                registrationRunning = false;
                RefreshView();
            }
        }

        // 덮개는 로그인·목록 조회·씬 스캔·등록을 모두 덮는다. 어느 단계에서 기다리는지
        // 말해 주지 않으면 넷 다 똑같이 멈춘 화면으로 보인다.
        private void ShowCoverMessage(string message)
        {
            if (coverMessageText == null)
            {
                return;
            }

            coverProgressText.text = string.Empty;
            coverMessageText.text = message;
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

        // 나중에로 게이트를 내리면 게이트의 버튼도 함께 비활성된다. 그래서 게이트로
        // 되돌아오는 길은 고급 섹션의 이 두 버튼뿐이고, 둘 다 gateDismissed를 지워야 한다.
        // 연결이 있는 이유는 로그인을 버리지 않고 재시도할 길을 남기는 것이다.
        private void ConnectWebSocket()
        {
            gateDismissed = false;
            viewModel.Connect(artelManager.StartTransport);
            RefreshView();
        }

        private void LogOut()
        {
            gateDismissed = false;
            viewModel.LogOut();
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

            if (createdEventSystem == null)
            {
                createdEventSystem = EnsureEventSystem(transform);
            }

            var toggleButton = CreateButton(canvasObject.transform, "Artel", new Vector2(56f, 48f));
            toggleButton.GetComponentInChildren<Text>().text = string.Empty;
            CreateLogo(toggleButton.transform, Vector2.zero, 36f);
            AnchorTopRight(toggleButton.GetComponent<RectTransform>(), new Vector2(-24f, -24f));
            toggleButton.onClick.AddListener(() => panelObject.SetActive(!panelObject.activeSelf));

            panelObject = new GameObject("Artel Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            panelObject.GetComponent<Image>().color = bgSurface;
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-24f, -84f);
            panelRect.sizeDelta = new Vector2(440f, 300f);

            // 로그인과 프로젝트 선택은 게이트가 소유한다. 진입 지점이 둘이면 처음 쓰는
            // 사람이 어느 쪽을 봐야 할지 모른다. 패널에는 상태 문구와 고급 섹션만 남는다.
            var title = CreateText(panelObject.transform, "Artel SDK", 24, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(20f, -16f), new Vector2(400f, 36f));

            statusText = CreateText(panelObject.transform, string.Empty, 15, TextAnchor.UpperLeft, textSecondary);
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
            coverObject.GetComponent<Image>().color = coverColor;
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

            CreateLogo(progressContent.transform, new Vector2(0f, 104f), 72f);

            var title = CreateText(progressContent.transform, "Artel SDK", 30, TextAnchor.MiddleCenter);
            CenterRect(title.rectTransform, new Vector2(0f, 38f), new Vector2(900f, 44f));

            coverMessageText = CreateText(
                progressContent.transform,
                "게임 화면을 분석하는 중입니다. 잠시만 기다려 주세요.",
                20,
                TextAnchor.MiddleCenter,
                textSecondary);
            CenterRect(coverMessageText.rectTransform, new Vector2(0f, -10f), new Vector2(900f, 32f));

            coverProgressText = CreateText(
                progressContent.transform, string.Empty, 18, TextAnchor.MiddleCenter, textMuted);
            CenterRect(coverProgressText.rectTransform, new Vector2(0f, -48f), new Vector2(900f, 28f));

            coverStatusText = CreateText(
                progressContent.transform, string.Empty, 16, TextAnchor.MiddleCenter, textMuted);
            CenterRect(coverStatusText.rectTransform, new Vector2(0f, -84f), new Vector2(900f, 28f));
        }

        // 게임 화면 거리에서 읽히도록 artel-home의 타이포보다 한 단계 크게 잡는다.
        //
        // 배치는 VerticalLayoutGroup이 아니라 CenterRect 고정 좌표다. 레이아웃 그룹은
        // childControlHeight/childForceExpandHeight가 기본 true인데 스프라이트 없는
        // Image/Button은 ILayoutElement를 구현하지 않아(Text만 한다) ContentSizeFitter
        // 아래에서 높이가 0으로 접힌다. 요소마다 LayoutElement를 붙이는 쪽이 더 길다.
        private void CreateGateContent()
        {
            gateContent = new GameObject("Gate Content", typeof(RectTransform));
            gateContent.transform.SetParent(coverObject.transform, false);
            Inset(gateContent.GetComponent<RectTransform>(), 0f);

            CreateLogo(gateContent.transform, new Vector2(0f, 220f), 72f);

            var title = CreateText(gateContent.transform, "Artel SDK", 32, TextAnchor.MiddleCenter);
            CenterRect(title.rectTransform, new Vector2(0f, 148f), new Vector2(900f, 48f));

            gateMessageText = CreateText(
                gateContent.transform,
                LoginMessage,
                18,
                TextAnchor.MiddleCenter,
                textSecondary);
            CenterRect(gateMessageText.rectTransform, new Vector2(0f, 96f), new Vector2(900f, 28f));

            // 오류 줄은 빈 문자열로도 자리를 차지한다. 나타날 때 아래 버튼이 밀리면 누르려던
            // 위치가 어긋난다.
            gateErrorText = CreateText(
                gateContent.transform, string.Empty, 16, TextAnchor.MiddleCenter, StatusCritical);
            CenterRect(gateErrorText.rectTransform, new Vector2(0f, 54f), new Vector2(900f, 24f));

            loginButton = CreateButton(gateContent.transform, "로그인", new Vector2(640f, 56f), primary: true);
            CenterRect(loginButton.GetComponent<RectTransform>(), new Vector2(0f, 4f), new Vector2(640f, 56f));
            loginButton.onClick.AddListener(BeginLogin);

            // 로그인은 됐는데 목록을 못 읽었거나 등록이 실패한 자리. 이것이 없으면 게이트에
            // 남는 길이 로그아웃뿐이라, 잠깐 끊긴 서버 때문에 다시 로그인해야 한다.
            reloadProjectsButton = CreateButton(gateContent.transform, "다시 시도", new Vector2(640f, 56f), primary: true);
            CenterRect(reloadProjectsButton.GetComponent<RectTransform>(), new Vector2(0f, 4f), new Vector2(640f, 56f));
            reloadProjectsButton.onClick.AddListener(ReloadProjects);

            projectListObject = new GameObject("Project List", typeof(RectTransform));
            projectListObject.transform.SetParent(gateContent.transform, false);
            CenterRect(
                projectListObject.GetComponent<RectTransform>(),
                new Vector2(0f, 4f),
                new Vector2(640f, MaxListedProjects * ProjectRowHeight));

            gateLogOutButton = CreateButton(gateContent.transform, "로그아웃", new Vector2(640f, 44f));
            // 고급 섹션에도 같은 라벨의 버튼이 있다. GameObject 이름이 겹치면 어느 쪽을
            // 집었는지 알 수 없어 계층에서도 테스트에서도 헷갈린다.
            gateLogOutButton.gameObject.name = "게이트 로그아웃 Button";
            CenterRect(gateLogOutButton.GetComponent<RectTransform>(), new Vector2(0f, -232f), new Vector2(640f, 44f));
            gateLogOutButton.onClick.AddListener(LogOut);

            var dismissButton = CreateButton(gateContent.transform, "나중에", new Vector2(640f, 44f));
            CenterRect(dismissButton.GetComponent<RectTransform>(), new Vector2(0f, -284f), new Vector2(640f, 44f));
            dismissButton.onClick.AddListener(DismissGate);
        }

        // 프로젝트 버튼은 목록을 받은 뒤에야 몇 개인지 안다. 목록이 실제로 달라졌을 때만
        // 다시 만든다 — 매번 지웠다 만들면 누르는 순간 버튼이 사라진다.
        private void RebuildProjectList()
        {
            var projects = viewModel.Projects;
            if (MatchesListedProjects(projects))
            {
                return;
            }

            listedProjectIds.Clear();
            for (var index = projectListObject.transform.childCount - 1; index >= 0; index--)
            {
                var child = projectListObject.transform.GetChild(index).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }

            var rowCount = Mathf.Min(projects.Count, MaxListedProjects);
            for (var index = 0; index < rowCount; index++)
            {
                var project = projects[index];
                var button = CreateButton(
                    projectListObject.transform, project.Name ?? project.Id, new Vector2(640f, 52f));
                CenterRect(
                    button.GetComponent<RectTransform>(),
                    new Vector2(0f, ((rowCount - 1) * ProjectRowHeight * 0.5f) - (index * ProjectRowHeight)),
                    new Vector2(640f, 52f));
                var projectId = project.Id;
                button.onClick.AddListener(() => ChooseProject(projectId));
                listedProjectIds.Add(projectId);
            }
        }

        private bool MatchesListedProjects(IReadOnlyList<SdkProjectDto> projects)
        {
            var rowCount = Mathf.Min(projects.Count, MaxListedProjects);
            if (listedProjectIds.Count != rowCount)
            {
                return false;
            }

            for (var index = 0; index < rowCount; index++)
            {
                if (!string.Equals(listedProjectIds[index], projects[index].Id, System.StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
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
            accountText = details;

            var smoothCursorToggle = CreateToggle(advancedObject.transform, "부드러운 커서");
            SetRect(smoothCursorToggle.GetComponent<RectTransform>(), new Vector2(20f, -58f), new Vector2(200f, 32f));
            smoothCursorToggle.isOn = artelManager.SmoothCursorMovement;
            smoothCursorToggle.onValueChanged.AddListener(value => artelManager.SmoothCursorMovement = value);

            var themeToggle = CreateToggle(advancedObject.transform, "다크 모드");
            SetRect(themeToggle.GetComponent<RectTransform>(), new Vector2(20f, -92f), new Vector2(200f, 32f));
            themeToggle.isOn = darkTheme;
            themeToggle.onValueChanged.AddListener(SetDarkTheme);

            connectButton = CreateButton(advancedObject.transform, "연결", new Vector2(180f, 36f));
            SetRect(connectButton.GetComponent<RectTransform>(), new Vector2(240f, -56f), new Vector2(180f, 36f));
            connectButton.onClick.AddListener(ConnectWebSocket);

            var logOutButton = CreateButton(advancedObject.transform, "로그아웃", new Vector2(180f, 32f));
            SetRect(logOutButton.GetComponent<RectTransform>(), new Vector2(240f, -96f), new Vector2(180f, 32f));
            logOutButton.onClick.AddListener(LogOut);

            advancedObject.SetActive(false);
        }

        private void RefreshView()
        {
            // 게이트 콘텐츠가 GUI에서 마지막에 만들어지고, 이 버튼은 그중에서도 마지막에
            // 잡히는 참조다. 이것이 있으면 아래에서 만지는 나머지도 다 있다.
            if (gateLogOutButton == null)
            {
                return;
            }

            statusText.text = viewModel.Status;
            // 실패를 문장으로만 알리면 눈에 걸리지 않는다.
            statusText.color = StatusColor();
            accountText.text =
                (viewModel.HasToken ? "로그인 " + AccountLabel() : "로그인하지 않음") +
                "\nSDK UUID " + artelManager.SdkId +
                "\n게임 버전 " + artelManager.GameVersion;
            coverStatusText.text = viewModel.Status;
            gateErrorText.text = viewModel.HasError ? viewModel.Status : string.Empty;
            connectButton.interactable = viewModel.CanConnect;

            // 게이트는 한 번에 하나만 묻는다. 토큰이 없으면 로그인, 목록이 있으면 프로젝트,
            // 토큰만 있고 목록이 비었으면 다시 시도. 셋을 동시에 띄우면 무엇이 다음 단계인지
            // 알 수 없다.
            var choosingProject = viewModel.HasToken && viewModel.Projects.Count > 0;
            loginButton.gameObject.SetActive(!viewModel.HasToken);
            loginButton.interactable = viewModel.CanLogIn && !loginRunning;
            reloadProjectsButton.gameObject.SetActive(viewModel.HasToken && !choosingProject);
            reloadProjectsButton.interactable = !loginRunning;
            projectListObject.SetActive(choosingProject);
            gateLogOutButton.gameObject.SetActive(viewModel.HasToken);
            gateMessageText.text = viewModel.HasToken ? ChooseProjectMessage : LoginMessage;
            RebuildProjectList();

            // 덮개와 두 콘텐츠 그룹의 쓰기 주체는 여기 하나다. 코루틴이 따로 켜고 끄면
            // 어느 한쪽 경로에서 덮개가 켜진 채 남아 게임 화면을 통째로 가린다.
            //
            // registrationRunning이 콘텐츠 선택에 들어가는 이유: 스캔은 State가 아직
            // NeedsLogin인 채로 몇 초 돈다. ShowGate만 보면 그 동안 게이트가 버튼을 켠 채
            // 얼어 있고 진행 숫자는 꺼진 그룹에 써진다. 브라우저를 기다리는 동안도 같다.
            var busy = registrationRunning || loginRunning;
            var showGate = viewModel.ShowGate && !busy && !gateDismissed;
            coverObject.SetActive(showGate || busy);
            gateContent.SetActive(showGate);
            progressContent.SetActive(busy);

            // 패널을 매 Changed마다 덮어쓰면 Artel 토글이 무력화된다 — 직접 열어둔 패널이
            // 다음 상태 변화에 닫혀버린다. 그래서 전이에서만 쓴다.
            if (appliedShowPanel != viewModel.ShowPanel)
            {
                appliedShowPanel = viewModel.ShowPanel;
                panelObject.SetActive(appliedShowPanel);
            }
        }

        private string AccountLabel()
        {
            var displayName = viewModel.DisplayName;
            return string.IsNullOrWhiteSpace(displayName) ? "완료" : displayName;
        }

        private Color StatusColor()
        {
            if (viewModel.State == ArtelConnectionState.Connected)
            {
                return StatusSuccess;
            }

            return viewModel.HasError ? StatusCritical : textSecondary;
        }

        // primary는 화면에서 지금 눌러야 하는 버튼 하나에만 쓴다. 나머지는 secondary로
        // 물러나 있어야 그 하나가 눈에 띈다. artel-home의 .button--primary /
        // .button--secondary와 같은 구분이다.
        private Button CreateButton(Transform parent, string label, Vector2 size, bool primary = false)
        {
            var buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;

            if (primary)
            {
                buttonObject.GetComponent<Image>().color = brandAccent;
            }
            else
            {
                // 테두리는 겉 Image를 테두리색으로 두고 안쪽에 배경색 Image를 1유닛 들여
                // 깔아 낸다. uGUI Image에는 테두리 속성이 없다.
                buttonObject.GetComponent<Image>().color = borderStrong;
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(buttonObject.transform, false);
                fill.GetComponent<Image>().color = bgRaised;
                Inset(fill.GetComponent<RectTransform>(), 1f);
            }

            var text = CreateText(
                buttonObject.transform,
                label,
                17,
                TextAnchor.MiddleCenter,
                primary ? textOnAccent : textPrimary);
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

        private Text CreateText(
            Transform parent, string value, int fontSize, TextAnchor alignment, Color? color = null)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color ?? textPrimary;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private void CreateLogo(Transform parent, Vector2 position, float size)
        {
            var mark = new GameObject("Artel Logo", typeof(RectTransform), typeof(ArtelLogoGraphic));
            mark.transform.SetParent(parent, false);
            var logo = mark.GetComponent<ArtelLogoGraphic>();
            logo.BodyColor = ArtelLogoGraphic.Body(darkTheme);
            logo.AccentColor = ArtelLogoGraphic.Accent(darkTheme);
            logo.raycastTarget = false;
            CenterRect(mark.GetComponent<RectTransform>(), position, new Vector2(size, size));
        }

        private Toggle CreateToggle(Transform parent, string label)
        {
            var toggleObject = new GameObject(label + " Toggle", typeof(RectTransform), typeof(Toggle));
            toggleObject.transform.SetParent(parent, false);

            var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(toggleObject.transform, false);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            SetRect(backgroundRect, Vector2.zero, new Vector2(28f, 28f));
            var background = backgroundObject.GetComponent<Image>();
            background.color = bgRaised;

            var checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkObject.transform.SetParent(backgroundObject.transform, false);
            var checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkmarkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;
            var checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.color = actionPrimary;

            var text = CreateText(toggleObject.transform, label, 16, TextAnchor.MiddleLeft);
            SetRect(text.rectTransform, new Vector2(40f, 0f), new Vector2(180f, 28f));

            var toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            return toggle;
        }

        private void SetDarkTheme(bool enabled)
        {
            if (darkTheme == enabled)
            {
                return;
            }

            darkTheme = enabled;
            PlayerPrefs.SetInt(DarkThemePlayerPrefsKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyPalette();

            var previousCanvas = canvasObject;
            CreateGui();
            RefreshView();
            if (Application.isPlaying)
            {
                Destroy(previousCanvas);
            }
            else
            {
                DestroyImmediate(previousCanvas);
            }
        }

        private void ApplyPalette()
        {
            if (darkTheme)
            {
                bgCanvas = new Color32(0x14, 0x16, 0x1C, 0xFF);
                bgSurface = new Color32(0x1A, 0x1D, 0x24, 0xFF);
                bgRaised = new Color32(0x22, 0x26, 0x2F, 0xFF);
                borderStrong = new Color32(0x61, 0x6B, 0x7A, 0xFF);
                textPrimary = ArtelLogoGraphic.Ink;
                textSecondary = new Color32(0x9A, 0xA1, 0xAD, 0xFF);
                textMuted = new Color32(0x83, 0x8C, 0x9A, 0xFF);
            }
            else
            {
                bgCanvas = new Color32(0xF7, 0xF4, 0xEE, 0xFF);
                bgSurface = new Color32(0xFD, 0xFB, 0xF7, 0xFF);
                bgRaised = new Color32(0xF1, 0xED, 0xE5, 0xFF);
                borderStrong = new Color32(0x92, 0x8C, 0x7D, 0xFF);
                textPrimary = ArtelLogoGraphic.Charcoal;
                textSecondary = new Color32(0x5A, 0x5F, 0x6B, 0xFF);
                textMuted = new Color32(0x6F, 0x6C, 0x62, 0xFF);
            }

            brandAccent = ArtelLogoGraphic.Accent(darkTheme);
            actionPrimary = brandAccent;

            // 채운 accent 위에는 두 테마 모두 잉크를 얹는다. 흰 글자는 #F04B3A 위에서
            // 3.64:1이고 잉크는 4.97:1이다. 버튼 라벨에는 후자만 통과한다.
            textOnAccent = new Color32(0x14, 0x16, 0x1C, 0xFF);

            // 덮개는 씬 전환을 비추지 않도록 항상 불투명하다.
            coverColor = bgCanvas;
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
