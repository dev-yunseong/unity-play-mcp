using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    public sealed class KeyboardStatusController : MonoBehaviour
    {
        private const int OverlaySortingOrder = short.MaxValue - 2;
        private const string DarkThemePlayerPrefsKey = ArtelOwnedPlayerPrefs.DarkTheme;
        // artel-home tokens.css의 bg.surface. 게임 화면 위에 뜨므로 알파를 남겨 두되
        // 글자 대비를 지킬 만큼은 불투명해야 한다.
        internal static readonly Color32 DarkPanelColor = new Color32(0x1A, 0x1D, 0x24, 0xF5);
        internal static readonly Color32 LightPanelColor = new Color32(0xFD, 0xFB, 0xF7, 0xF5);

        private static readonly string[] MouseButtonNames = { "LEFT", "RIGHT", "MIDDLE" };

        private readonly List<KeyCode> keyboardKeys = new List<KeyCode>();
        private readonly List<KeyCode> pressedKeys = new List<KeyCode>();
        private readonly List<int> heldMouseButtons = new List<int>();
        private GameObject canvasObject;
        private Text keyStatusText;
        private Text pointerStatusText;
        private Image panelImage;
        private Image accentImage;
        private Text keyTitleText;
        private Text pointerTitleText;
        private bool darkTheme;
        private string displayedKeys;
        private string displayedPointer;

        private void Awake()
        {
            CacheKeyboardKeys();
            darkTheme = PlayerPrefs.GetInt(DarkThemePlayerPrefsKey, 1) != 0;
            CreateGui();
            ApplyTheme();
            RefreshText();
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
                if (ArtelInput.GetKey(key))
                {
                    pressedKeys.Add(key);
                }
            }

            heldMouseButtons.Clear();
            for (var button = 0; button < VirtualMouseState.ButtonCount; button++)
            {
                if (ArtelInput.IsMouseButtonHeld(button))
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
                "Artel Keyboard Status Canvas",
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

            keyTitleText = CreateText(panelObject.transform, "PRESSED KEYS", 13, ArtelLogoGraphic.Accent(darkTheme));
            SetStretchRect(keyTitleText.rectTransform, new Vector2(24f, 55f), new Vector2(-304f, -10f));

            keyStatusText = CreateText(panelObject.transform, string.Empty, 23, ArtelLogoGraphic.Ink);
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

            pointerTitleText = CreateText(panelObject.transform, "POINTER", 13, ArtelLogoGraphic.Accent(darkTheme));
            SetStretchRect(pointerTitleText.rectTransform, new Vector2(456f, 55f), new Vector2(-24f, -10f));

            pointerStatusText = CreateText(panelObject.transform, string.Empty, 19, ArtelLogoGraphic.Ink);
            pointerStatusText.fontStyle = FontStyle.Bold;
            SetStretchRect(pointerStatusText.rectTransform, new Vector2(456f, 10f), new Vector2(-24f, -40f));
        }

        private void ApplyTheme()
        {
            var foreground = (Color)ArtelLogoGraphic.Body(darkTheme);
            var accent = (Color)ArtelLogoGraphic.Accent(darkTheme);
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

        private void RefreshText()
        {
            var formattedKeys = FormatPressedKeys(pressedKeys);
            if (displayedKeys != formattedKeys)
            {
                displayedKeys = formattedKeys;
                keyStatusText.text = formattedKeys;
            }

            var formattedPointer = FormatPointer(
                ArtelInput.HasVirtualMousePosition, ArtelInput.mousePosition, heldMouseButtons);
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
