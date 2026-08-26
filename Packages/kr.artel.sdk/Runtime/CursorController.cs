using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    public sealed class CursorController : MonoBehaviour
    {
        private const int CursorWidth = 36;
        private const int CursorHeight = 48;
        private const int OverlaySortingOrder = short.MaxValue;
        private const string DarkThemePlayerPrefsKey = ArtelOwnedPlayerPrefs.DarkTheme;

        [SerializeField] private bool smoothMovement;
        [SerializeField] private float movementDurationSeconds = 0.35f;

        private RectTransform cursorTransform;
        private Texture2D cursorTexture;
        private Sprite cursorSprite;
        private bool darkTheme;

        public bool SmoothMovement
        {
            get { return smoothMovement; }
            set { smoothMovement = value; }
        }

        private void Awake()
        {
            darkTheme = PlayerPrefs.GetInt(DarkThemePlayerPrefsKey, 1) != 0;
            CreateCursor();
        }

        private void Update()
        {
            var currentDarkTheme = PlayerPrefs.GetInt(DarkThemePlayerPrefsKey, 1) != 0;
            if (darkTheme == currentDarkTheme)
            {
                return;
            }

            darkTheme = currentDarkTheme;
            PaintCursorTexture(cursorTexture, darkTheme);
        }

        private void OnDestroy()
        {
            if (cursorTexture != null)
            {
                Destroy(cursorTexture);
            }

            if (cursorSprite != null)
            {
                Destroy(cursorSprite);
            }
        }

        public IEnumerator MoveTo(RectTransform target, Action<Vector2> moved)
        {
            if (target == null)
            {
                yield break;
            }

            var targetCenter = target.TransformPoint(target.rect.center);
            yield return MoveTo(
                RectTransformUtility.WorldToScreenPoint(CanvasCamera.For(target), targetCenter),
                moved);
        }

        /// <summary>
        /// Reports every intermediate position, not just the destination. A drag is made of the
        /// positions along the way — a handler that only ever saw the endpoint would be watching
        /// something teleport.
        /// </summary>
        /// <param name="glide">
        /// Forces the travel even when smooth movement is off. A pointer move is the one case where
        /// the path is the point: a held button turns it into a drag, and a jump from start to end
        /// gives the game a single drag event to work out what happened from.
        /// </param>
        public IEnumerator MoveTo(Vector2 screenPosition, Action<Vector2> moved, bool glide = false)
        {
            if (cursorTransform == null)
            {
                yield break;
            }

            cursorTransform.gameObject.SetActive(true);
            cursorTransform.SetAsLastSibling();

            if ((!smoothMovement && !glide) || movementDurationSeconds <= 0f)
            {
                PlaceCursor(screenPosition, moved);
                yield break;
            }

            var startPosition = (Vector2)cursorTransform.position;
            var elapsed = 0f;
            while (elapsed < movementDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / movementDurationSeconds);
                PlaceCursor(Vector2.Lerp(startPosition, screenPosition, SmoothStep(progress)), moved);
                yield return null;
            }

            PlaceCursor(screenPosition, moved);
        }

        private void PlaceCursor(Vector2 screenPosition, Action<Vector2> moved)
        {
            cursorTransform.position = screenPosition;
            if (moved != null)
            {
                moved(screenPosition);
            }
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - (2f * value));
        }

        private void CreateCursor()
        {
            var canvasObject = new GameObject("Artel Virtual Cursor Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;

            var cursorObject = new GameObject("Artel Virtual Cursor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cursorObject.transform.SetParent(canvasObject.transform, false);

            cursorTransform = cursorObject.GetComponent<RectTransform>();
            cursorTransform.anchorMin = Vector2.zero;
            cursorTransform.anchorMax = Vector2.zero;
            cursorTransform.pivot = new Vector2(0f, 1f);
            cursorTransform.sizeDelta = new Vector2(CursorWidth, CursorHeight);

            cursorTexture = CreateCursorTexture(darkTheme);
            cursorSprite = Sprite.Create(
                cursorTexture,
                new Rect(0f, 0f, CursorWidth, CursorHeight),
                new Vector2(0f, 1f),
                100f);
            var image = cursorObject.GetComponent<Image>();
            image.sprite = cursorSprite;
            image.raycastTarget = false;

            cursorObject.SetActive(false);
        }

        private static Texture2D CreateCursorTexture(bool darkTheme)
        {
            var texture = new Texture2D(CursorWidth, CursorHeight, TextureFormat.RGBA32, false)
            {
                name = "Artel Virtual Cursor Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            PaintCursorTexture(texture, darkTheme);
            return texture;
        }

        private static void PaintCursorTexture(Texture2D texture, bool darkTheme)
        {
            var pixels = new Color32[CursorWidth * CursorHeight];
            var borderColor = ArtelLogoGraphic.Body(darkTheme);
            var fillColor = ArtelLogoGraphic.Accent(darkTheme);

            for (var distanceFromTop = 0; distanceFromTop < CursorHeight; distanceFromTop++)
            {
                var y = CursorHeight - distanceFromTop - 1;
                for (var x = 0; x < CursorWidth; x++)
                {
                    if (IsCursorShape(x, distanceFromTop))
                    {
                        pixels[(y * CursorWidth) + x] = IsCursorBorder(x, distanceFromTop)
                            ? borderColor
                            : fillColor;
                        continue;
                    }

                    if (IsCursorShape(x - 3, distanceFromTop - 3))
                    {
                        pixels[(y * CursorWidth) + x] = new Color32(0, 0, 0, 90);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        private static bool IsCursorShape(int x, int distanceFromTop)
        {
            if (x < 0 || distanceFromTop < 0)
            {
                return false;
            }

            var arrowHead = distanceFromTop <= 29 && x <= Mathf.FloorToInt(distanceFromTop * 0.64f);
            var stemStart = 9 + Mathf.Max(0, distanceFromTop - 20) / 3;
            var arrowStem = distanceFromTop >= 20
                && distanceFromTop <= 43
                && x >= stemStart
                && x <= stemStart + 8;
            return arrowHead || arrowStem;
        }

        private static bool IsCursorBorder(int x, int distanceFromTop)
        {
            const int borderThickness = 2;
            for (var yOffset = -borderThickness; yOffset <= borderThickness; yOffset++)
            {
                for (var xOffset = -borderThickness; xOffset <= borderThickness; xOffset++)
                {
                    if (!IsCursorShape(x + xOffset, distanceFromTop + yOffset))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
