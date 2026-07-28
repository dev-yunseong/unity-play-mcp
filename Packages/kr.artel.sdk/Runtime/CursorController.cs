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

        [SerializeField] private bool smoothMovement;
        [SerializeField] private float movementDurationSeconds = 0.35f;

        private RectTransform cursorTransform;
        private Texture2D cursorTexture;
        private Sprite cursorSprite;

        public bool SmoothMovement
        {
            get { return smoothMovement; }
            set { smoothMovement = value; }
        }

        private void Awake()
        {
            CreateCursor();
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

        public IEnumerator MoveTo(RectTransform target)
        {
            if (target == null || cursorTransform == null)
            {
                yield break;
            }

            var targetCenter = target.TransformPoint(target.rect.center);
            var screenPosition = RectTransformUtility.WorldToScreenPoint(CanvasCamera.For(target), targetCenter);

            cursorTransform.gameObject.SetActive(true);
            cursorTransform.SetAsLastSibling();

            if (!smoothMovement || movementDurationSeconds <= 0f)
            {
                cursorTransform.position = screenPosition;
                yield break;
            }

            var startPosition = cursorTransform.position;
            var elapsed = 0f;
            while (elapsed < movementDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / movementDurationSeconds);
                cursorTransform.position = Vector2.Lerp(startPosition, screenPosition, SmoothStep(progress));
                yield return null;
            }

            cursorTransform.position = screenPosition;
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

            cursorTexture = CreateCursorTexture();
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

        private static Texture2D CreateCursorTexture()
        {
            var texture = new Texture2D(CursorWidth, CursorHeight, TextureFormat.RGBA32, false)
            {
                name = "Artel Virtual Cursor Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[CursorWidth * CursorHeight];

            for (var distanceFromTop = 0; distanceFromTop < CursorHeight; distanceFromTop++)
            {
                var y = CursorHeight - distanceFromTop - 1;
                for (var x = 0; x < CursorWidth; x++)
                {
                    if (IsCursorShape(x, distanceFromTop))
                    {
                        pixels[(y * CursorWidth) + x] = IsCursorBorder(x, distanceFromTop)
                            ? new Color32(12, 22, 38, 255)
                            : new Color32(72, 200, 255, 255);
                        continue;
                    }

                    if (IsCursorShape(x - 3, distanceFromTop - 3))
                    {
                        pixels[(y * CursorWidth) + x] = new Color32(0, 0, 0, 90);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
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
