using System.Collections;
using System.Text;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Artel
{
    [RequireComponent(typeof(ArtelManager))]
    public sealed class ArtelOnboardingController : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.45f, 0.85f, 1f);

        [SerializeField] private ArtelManager artelManager;

        private readonly IJsonCodec jsonCodec = new NewtonsoftJsonCodec();
        private GameObject canvasObject;
        private GameObject createdEventSystem;
        private GameObject panelObject;
        private Button registerButton;
        private Button connectButton;
        private Text statusText;
        private bool registered;
        private bool requestInProgress;

        private void Awake()
        {
            if (artelManager == null)
            {
                artelManager = GetComponent<ArtelManager>();
            }
        }

        private void Start()
        {
            CreateGui();
            SetStatus("SDK ID를 서버에 등록해 주세요.");
            RefreshButtons();
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
        }

        private void RegisterSdkId()
        {
            if (!requestInProgress)
            {
                StartCoroutine(RegisterSdkIdRequest());
            }
        }

        private IEnumerator RegisterSdkIdRequest()
        {
            requestInProgress = true;
            registered = false;
            SetStatus("SDK ID 등록 중...");
            RefreshButtons();

            UnityWebRequest request;
            try
            {
                var body = jsonCodec.Serialize(new SdkRegistrationRequestDto { SdkId = artelManager.SdkId });
                request = new UnityWebRequest(
                    artelManager.Server.SdkRegistrationUri.AbsoluteUri,
                    UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                request.SetRequestHeader("Content-Type", "application/json");
            }
            catch (System.Exception exception)
            {
                requestInProgress = false;
                SetStatus("설정 오류: " + exception.Message);
                RefreshButtons();
                yield break;
            }

            using (request)
            {
                yield return request.SendWebRequest();

                registered = request.result == UnityWebRequest.Result.Success;
                var response = string.IsNullOrWhiteSpace(request.downloadHandler.text)
                    ? "HTTP " + request.responseCode
                    : request.downloadHandler.text;
                SetStatus((registered ? "등록 성공: " : "등록 실패: ") + response);
            }

            requestInProgress = false;
            RefreshButtons();
        }

        private void ConnectWebSocket()
        {
            if (!registered || requestInProgress)
            {
                return;
            }

            try
            {
                artelManager.StartTransport();
                SetStatus("실시간 서버 연결을 시작했습니다.");
                connectButton.interactable = false;
            }
            catch (System.Exception exception)
            {
                SetStatus("연결 실패: " + exception.Message);
            }
        }

        private void CreateGui()
        {
            canvasObject = new GameObject("Artel Onboarding Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

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
            panelRect.sizeDelta = new Vector2(440f, 270f);

            var title = CreateText(panelObject.transform, "Artel SDK Onboarding", 24, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(20f, -18f), new Vector2(400f, 40f));

            var sdkIdText = CreateText(panelObject.transform, "SDK ID\n" + artelManager.SdkId, 16, TextAnchor.UpperLeft);
            SetRect(sdkIdText.rectTransform, new Vector2(20f, -66f), new Vector2(400f, 50f));

            registerButton = CreateButton(panelObject.transform, "SDK ID 등록", new Vector2(190f, 44f));
            SetRect(registerButton.GetComponent<RectTransform>(), new Vector2(20f, -126f), new Vector2(190f, 44f));
            registerButton.onClick.AddListener(RegisterSdkId);

            connectButton = CreateButton(panelObject.transform, "실시간 연결", new Vector2(190f, 44f));
            SetRect(connectButton.GetComponent<RectTransform>(), new Vector2(230f, -126f), new Vector2(190f, 44f));
            connectButton.onClick.AddListener(ConnectWebSocket);

            statusText = CreateText(panelObject.transform, string.Empty, 15, TextAnchor.UpperLeft);
            SetRect(statusText.rectTransform, new Vector2(20f, -182f), new Vector2(400f, 70f));
        }

        private void RefreshButtons()
        {
            registerButton.interactable = !requestInProgress;
            connectButton.interactable = registered && !requestInProgress;
        }

        private void SetStatus(string status)
        {
            statusText.text = status;
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
