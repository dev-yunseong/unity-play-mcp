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
        private static readonly Color PanelColor = new Color(0.04f, 0.07f, 0.12f, 0.92f);
        private static readonly Color AccentColor = new Color(0.28f, 0.78f, 1f, 1f);

        private readonly List<KeyCode> keyboardKeys = new List<KeyCode>();
        private readonly List<KeyCode> pressedKeys = new List<KeyCode>();
        private GameObject canvasObject;
        private Text keyStatusText;
        private string displayedKeys;

        private void Awake()
        {
            CacheKeyboardKeys();
            CreateGui();
            RefreshText();
        }

        private void Update()
        {
            pressedKeys.Clear();
            foreach (var key in keyboardKeys)
            {
                if (ArtelInput.GetKey(key))
                {
                    pressedKeys.Add(key);
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

            var panelObject = new GameObject("Keyboard Status Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 28f);
            panelRect.sizeDelta = new Vector2(560f, 92f);
            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = false;

            var titleObject = CreateText(panelObject.transform, "PRESSED KEYS", 15, AccentColor);
            SetStretchRect(titleObject.rectTransform, new Vector2(22f, 50f), new Vector2(-22f, -12f));

            keyStatusText = CreateText(panelObject.transform, string.Empty, 25, Color.white);
            keyStatusText.fontStyle = FontStyle.Bold;
            SetStretchRect(keyStatusText.rectTransform, new Vector2(22f, 10f), new Vector2(-22f, -42f));
        }

        private void RefreshText()
        {
            var formattedKeys = FormatPressedKeys(pressedKeys);
            if (displayedKeys == formattedKeys)
            {
                return;
            }

            displayedKeys = formattedKeys;
            keyStatusText.text = formattedKeys;
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
            text.alignment = TextAnchor.MiddleCenter;
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
