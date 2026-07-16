using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    public sealed class CursorController : MonoBehaviour
    {
        private const int CursorWidth = 18;
        private const int CursorHeight = 24;
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

            var targetCanvas = target.GetComponentInParent<Canvas>();
            var targetCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? targetCanvas.worldCamera
                : null;
            var targetCenter = target.TransformPoint(target.rect.center);
            var screenPosition = RectTransformUtility.WorldToScreenPoint(targetCamera, targetCenter);

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

            for (var y = 0; y < CursorHeight; y++)
            {
                var distanceFromTop = CursorHeight - y - 1;
                var width = distanceFromTop < 15 ? (distanceFromTop / 2) + 1 : 0;
                for (var x = 0; x < width; x++)
                {
                    var isBorder = x == 0 || x == width - 1 || distanceFromTop == 0 || distanceFromTop == 14;
                    pixels[(y * CursorWidth) + x] = isBorder
                        ? new Color32(24, 24, 24, 255)
                        : new Color32(255, 255, 255, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
