using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp
{
    public sealed class KeyboardStatusController : MonoBehaviour
    {
        private const int OverlaySortingOrder = short.MaxValue - 2;
        private const string DarkThemePlayerPrefsKey = OwnedPlayerPrefs.DarkTheme;
        // 게임 화면 위에 뜨므로 알파를 남겨 두되
        // 글자 대비를 지킬 만큼은 불투명해야 한다.
        internal static readonly Color32 DarkPanelColor = new Color32(0x1A, 0x1D, 0x24, 0xF5);
        internal static readonly Color32 LightPanelColor = new Color32(0xFD, 0xFB, 0xF7, 0xF5);
        internal static readonly Color32 DarkForegroundColor = new Color32(0xF2, 0xEF, 0xE9, 0xFF);
        internal static readonly Color32 LightForegroundColor = new Color32(0x20, 0x23, 0x2B, 0xFF);
        internal static readonly Color32 DarkAccentColor = new Color32(0xFF, 0x5C, 0x48, 0xFF);
        internal static readonly Color32 LightAccentColor = new Color32(0xF0, 0x4B, 0x3A, 0xFF);

        private static readonly string[] MouseButtonNames = { "LEFT", "RIGHT", "MIDDLE" };

        private readonly List<KeyCode> keyboardKeys = new List<KeyCode>();
        private readonly List<KeyCode> pressedKeys = new List<KeyCode>();
        private readonly List<int> heldMouseButtons = new List<int>();

        /// <summary>이 controller 가 만든 overlay canvas. reload 를 건너 살아남는 유일한 손잡이다.</summary>
        /// <remarks>
        /// play 중 assembly reload 는 GameObject 를 하나도 파괴하지 않으므로 canvas 는 그대로 남고, 그것을
        /// 가리키던 field 만 사라진다. 그 상태에서 확인 없이 다시 만들면 status panel 이 두 벌이 된다
        /// (issue #65). 그래서 이 참조 하나만 serialize 한다. <see cref="OnDestroy"/> 가 쓰는 대상은
        /// 그대로다.
        ///
        /// inspector 에 내놓을 값은 아니다. 사람이 고르는 설정이 아니라 controller 가 제가 만든 것을 적어
        /// 두는 자리다.
        /// </remarks>
        [SerializeField, HideInInspector] private GameObject canvasObject;

        private Text keyStatusText;
        private Text pointerStatusText;
        private Image panelImage;
        private Image accentImage;
        private Text keyTitleText;
        private Text pointerTitleText;
        private bool darkTheme;
        private string displayedKeys;
        private string displayedPointer;

        /// <summary>이 domain 에서 GUI 를 만들었는지.</summary>
        /// <remarks>
        /// serialize 하지 않는다. reload 를 건너면 false 로 돌아오고, 여기서는 그것이 원하는 바다: 같은
        /// reload 에 함께 사라진 <see cref="keyStatusText"/> 이하와 비워진 <see cref="keyboardKeys"/> 를
        /// 다시 채우라고 <see cref="OnEnable"/> 에게 말하는 것이 이 false 다.
        /// <c>UnityPlayMcpHost.ownsRuntime</c> 이 같은 자리에서 같은 일을 한다.
        /// </remarks>
        private bool builtOverlay;

        /// <summary>
        /// 첫 활성화와 assembly reload 가 함께 지나는 자리. 두 번 불러도 status panel 은 한 벌이다.
        /// </summary>
        /// <remarks>
        /// Unity 는 play 중 assembly reload 에서 <c>OnDisable</c> → serialize → domain 교체 → deserialize
        /// → <c>OnEnable</c> 순으로 가고 <c>Awake</c> 는 다시 부르지 않는다. 만드는 일이 <c>Awake</c> 에만
        /// 있던 동안 reload 를 건넌 controller 는 <see cref="keyStatusText"/> 가 null 인 채로
        /// <see cref="Update"/> 만 돌았고, 표시 문자열이 바뀔 때마다 — 즉 agent 가 무엇을 누를 때마다 —
        /// <see cref="RefreshText"/> 에서 NullReferenceException 을 냈다 (issue #65).
        ///
        /// 그냥 껐다 켜는 길로도 여기 온다. 그때는 <see cref="builtOverlay"/> 가 true 라 아무것도 하지
        /// 않는다 — 다시 만들면 panel 이 깜빡인다.
        /// </remarks>
        private void OnEnable()
        {
            if (builtOverlay)
            {
                return;
            }

            DiscardOverlay();
            CacheKeyboardKeys();
            darkTheme = PlayerPrefs.GetInt(DarkThemePlayerPrefsKey, 1) != 0;
            CreateGui();
            ApplyTheme();

            // 갓 만든 Text 는 빈 문자열이다. 지난 domain 이 남긴 표시 문자열을 그대로 들고 있으면
            // RefreshText 가 "같다" 고 보고 panel 을 빈 채로 둔다.
            displayedKeys = null;
            displayedPointer = null;
            RefreshText();

            builtOverlay = true;
        }

        /// <summary>reload 를 건너 살아남은 overlay 를 걷어낸다.</summary>
        private void DiscardOverlay()
        {
            if (canvasObject == null)
            {
                return;
            }

            // Destroy 는 프레임 끝에야 처리된다. 먼저 꺼 두지 않으면 새로 만든 panel 과 살아남은 panel 이
            // 그 한 프레임 동안 겹쳐 그려진다.
            canvasObject.SetActive(false);
            Destroy(canvasObject);
            canvasObject = null;
        }

        private void Update()
        {
            var currentDarkTheme = PlayerPrefs.GetInt(DarkThemePlayerPrefsKey, 1) != 0;
            if (darkTheme != currentDarkTheme)
            {
                darkTheme = currentDarkTheme;
                ApplyTheme();
            }

            pressedKeys.Clear();
            foreach (var key in keyboardKeys)
            {
                if (VirtualInput.GetKey(key))
                {
                    pressedKeys.Add(key);
                }
            }

            heldMouseButtons.Clear();
            for (var button = 0; button < VirtualMouseState.ButtonCount; button++)
            {
                if (VirtualInput.IsMouseButtonHeld(button))
                {
                    heldMouseButtons.Add(button);
                }
            }

            RefreshText();
        }

        private void OnDestroy()
        {
            if (canvasObject != null)
            {
                Destroy(canvasObject);
            }
        }

        /// <summary>
        /// The agent's pointer, or a dash while it has never been moved. A held button with no
        /// visible drag is the failure this line exists to make obvious.
        /// </summary>
        internal static string FormatPointer(
            bool hasPosition, Vector2 position, IReadOnlyList<int> heldButtons)
        {
            if (!hasPosition)
            {
                return "—";
            }

            var result = new StringBuilder();
            result.Append('(');
            result.Append(Mathf.RoundToInt(position.x));
            result.Append(", ");
            result.Append(Mathf.RoundToInt(position.y));
            result.Append(')');

            if (heldButtons == null || heldButtons.Count == 0)
            {
                return result.ToString();
            }

            for (var index = 0; index < heldButtons.Count; index++)
            {
                result.Append(index == 0 ? "   HOLD  " : "  +  ");
                result.Append(MouseButtonNames[heldButtons[index]]);
            }

            return result.ToString();
        }

        internal static string FormatPressedKeys(IReadOnlyList<KeyCode> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return "—";
            }

            var result = new StringBuilder();
            for (var index = 0; index < keys.Count; index++)
            {
                if (index > 0)
                {
                    result.Append("  +  ");
                }

                result.Append(FormatKeyName(keys[index]));
            }

            return result.ToString();
        }

        private void CacheKeyboardKeys()
        {
            // 두 번 불려도 같은 목록이어야 한다. reload 는 이 list 를 비운 채 돌려주지만, 그냥 껐다 켜는
            // 길이나 앞으로 늘어날 다른 길에서 채워진 채로 여기 들어오면 KeyCode 가 두 벌씩 쌓인다.
            keyboardKeys.Clear();

            var seenValues = new HashSet<int>();
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                var name = key.ToString();
                if (key == KeyCode.None ||
                    name.StartsWith("Mouse", StringComparison.Ordinal) ||
                    name.StartsWith("Joystick", StringComparison.Ordinal) ||
                    !seenValues.Add((int)key))
                {
                    continue;
                }

                keyboardKeys.Add(key);
            }
        }

        private void CreateGui()
        {
            canvasObject = new GameObject(
                "Unity Play MCP Keyboard Status Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var panelObject = new GameObject(
                "Keyboard Status Panel",
                typeof(RectTransform),
                typeof(Image),
                typeof(Shadow));
            panelObject.transform.SetParent(canvasObject.transform, false);
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 28f);
            panelRect.sizeDelta = new Vector2(720f, 96f);
            panelImage = panelObject.GetComponent<Image>();
            panelImage.raycastTarget = false;
            var shadow = panelObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.28f);
            shadow.effectDistance = new Vector2(0f, -4f);

            var accent = new GameObject("Brand Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(panelObject.transform, false);
            accentImage = accent.GetComponent<Image>();
            accentImage.raycastTarget = false;
            SetStretchRect(accent.GetComponent<RectTransform>(), Vector2.zero, new Vector2(-714f, 0f));

            keyTitleText = CreateText(panelObject.transform, "PRESSED KEYS", 13, Accent(darkTheme));
            SetStretchRect(keyTitleText.rectTransform, new Vector2(24f, 55f), new Vector2(-304f, -10f));

            keyStatusText = CreateText(panelObject.transform, string.Empty, 23, DarkForegroundColor);
            keyStatusText.fontStyle = FontStyle.Bold;
            SetStretchRect(keyStatusText.rectTransform, new Vector2(24f, 10f), new Vector2(-304f, -40f));

            var separator = new GameObject("Separator", typeof(RectTransform), typeof(Image));
            separator.transform.SetParent(panelObject.transform, false);
            separator.GetComponent<Image>().raycastTarget = false;
            var separatorRect = separator.GetComponent<RectTransform>();
            separatorRect.anchorMin = new Vector2(0f, 0.5f);
            separatorRect.anchorMax = new Vector2(0f, 0.5f);
            separatorRect.pivot = new Vector2(0.5f, 0.5f);
            separatorRect.anchoredPosition = new Vector2(432f, 0f);
            separatorRect.sizeDelta = new Vector2(1f, 64f);

            pointerTitleText = CreateText(panelObject.transform, "POINTER", 13, Accent(darkTheme));
            SetStretchRect(pointerTitleText.rectTransform, new Vector2(456f, 55f), new Vector2(-24f, -10f));

            pointerStatusText = CreateText(panelObject.transform, string.Empty, 19, DarkForegroundColor);
            pointerStatusText.fontStyle = FontStyle.Bold;
            SetStretchRect(pointerStatusText.rectTransform, new Vector2(456f, 10f), new Vector2(-24f, -40f));
        }

        private void ApplyTheme()
        {
            var foreground = (Color)Foreground(darkTheme);
            var accent = (Color)Accent(darkTheme);
            panelImage.color = darkTheme ? DarkPanelColor : LightPanelColor;
            keyStatusText.color = foreground;
            pointerStatusText.color = foreground;
            accentImage.color = accent;
            keyTitleText.color = accent;
            pointerTitleText.color = accent;

            var separator = panelImage.transform.Find("Separator").GetComponent<Image>();
            separator.color = darkTheme
                ? new Color32(0x61, 0x6B, 0x7A, 0xFF)
                : new Color32(0x92, 0x8C, 0x7D, 0xFF);
        }

        internal static Color32 Foreground(bool darkTheme) =>
            darkTheme ? DarkForegroundColor : LightForegroundColor;

        internal static Color32 Accent(bool darkTheme) =>
            darkTheme ? DarkAccentColor : LightAccentColor;

        private void RefreshText()
        {
            var formattedKeys = FormatPressedKeys(pressedKeys);
            if (displayedKeys != formattedKeys)
            {
                displayedKeys = formattedKeys;
                keyStatusText.text = formattedKeys;
            }

            var formattedPointer = FormatPointer(
                VirtualInput.HasVirtualMousePosition, VirtualInput.mousePosition, heldMouseButtons);
            if (displayedPointer != formattedPointer)
            {
                displayedPointer = formattedPointer;
                pointerStatusText.text = formattedPointer;
            }
        }

        private static Text CreateText(Transform parent, string value, int fontSize, Color color)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static void SetStretchRect(RectTransform rectTransform, Vector2 offsetMin, Vector2 offsetMax)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }

        private static string FormatKeyName(KeyCode key)
        {
            var name = key.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal))
            {
                return name.Substring("Alpha".Length);
            }

            if (name.StartsWith("Keypad", StringComparison.Ordinal))
            {
                return "NUM " + name.Substring("Keypad".Length).ToUpperInvariant();
            }

            if (key == KeyCode.Return)
            {
                return "ENTER";
            }

            if (key == KeyCode.Escape)
            {
                return "ESC";
            }

            var result = new StringBuilder();
            for (var index = 0; index < name.Length; index++)
            {
                var character = name[index];
                if (index > 0 && char.IsUpper(character) && char.IsLower(name[index - 1]))
                {
                    result.Append(' ');
                }

                result.Append(char.ToUpperInvariant(character));
            }

            return result.ToString();
        }
    }
}
