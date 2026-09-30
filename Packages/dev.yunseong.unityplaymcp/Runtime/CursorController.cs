using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp
{
    public sealed class CursorController : MonoBehaviour
    {
        private const int CursorWidth = 36;
        private const int CursorHeight = 48;
        private const int OverlaySortingOrder = short.MaxValue;
        private const string DarkThemePlayerPrefsKey = OwnedPlayerPrefs.DarkTheme;

        [SerializeField] private bool smoothMovement;
        [SerializeField] private float movementDurationSeconds = 0.35f;

        /// <summary>이 controller 가 만든 overlay canvas 다. assembly reload 뒤에도 남는다.</summary>
        /// <remarks>
        /// play 중 assembly reload 는 canvas 를 남기고 non-serialized field 만 지운다. 이 참조가 없으면
        /// 다시 만들 때 cursor 가 두 개가 된다 (#65). 이름으로 <c>transform.Find</c> 하면 이름이 바뀔 때
        /// 조용히 실패하므로 참조를 serialize 한다.
        /// </remarks>
        [SerializeField, HideInInspector] private GameObject overlayCanvas;

        private RectTransform cursorTransform;
        private Texture2D cursorTexture;
        private Sprite cursorSprite;
        private bool darkTheme;

        /// <summary>이 domain 에서 cursor 를 만들었는지 여부다.</summary>
        /// <remarks>
        /// 일부러 serialize 하지 않는다. reload 뒤 false 가 되어 <see cref="OnEnable"/> 이 함께 사라진
        /// <see cref="cursorTransform"/> 등을 다시 만든다. <c>UnityPlayMcpHost.ownsRuntime</c> 과 같은 방식이다.
        /// </remarks>
        private bool builtOverlay;

        public bool SmoothMovement
        {
            get { return smoothMovement; }
            set { smoothMovement = value; }
        }

        /// <summary>
        /// 첫 활성화와 assembly reload 모두에서 cursor 를 만든다. 여러 번 불러도 cursor 는 하나다.
        /// </summary>
        /// <remarks>
        /// play 중 assembly reload 는 <c>Awake</c> 를 다시 부르지 않고 <c>OnEnable</c> 만 부르므로 여기서 만든다 (#65).
        ///
        /// 단순히 껐다 켤 때는 <see cref="builtOverlay"/> 가 true 라 아무것도 하지 않는다.
        /// 다시 만들면 cursor 위치를 잃는다.
        /// </remarks>
        private void OnEnable()
        {
            if (builtOverlay)
            {
                return;
            }

            DiscardOverlay();
            darkTheme = PlayerPrefs.GetInt(DarkThemePlayerPrefsKey, 1) != 0;
            CreateCursor();
            builtOverlay = true;
        }

        /// <summary>reload 뒤에 남은 overlay 를 제거한다.</summary>
        private void DiscardOverlay()
        {
            if (overlayCanvas == null)
            {
                return;
            }

            // texture 와 sprite 는 GameObject 와 함께 파괴되지 않는 asset 이다. 여기서 해제하지 않으면
            // reload 마다 하나씩 누수된다.
            var image = overlayCanvas.GetComponentInChildren<Image>(true);
            var sprite = image == null ? null : image.sprite;
            if (sprite != null)
            {
                if (sprite.texture != null)
                {
                    Destroy(sprite.texture);
                }

                Destroy(sprite);
            }

            // Destroy 는 프레임 끝에 처리되므로 먼저 꺼서 두 cursor 가 한 프레임 동안 함께 그려지지 않게 한다.
            overlayCanvas.SetActive(false);
            Destroy(overlayCanvas);
            overlayCanvas = null;
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
        /// Reports every intermediate position, not just the destination, so drag handlers see the path.
        /// </summary>
        /// <param name="glide">
        /// Forces the travel even when smooth movement is off, so a held button produces a real drag
        /// instead of a single jump.
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
            overlayCanvas = new GameObject("Unity Play MCP Virtual Cursor Canvas", typeof(RectTransform), typeof(Canvas));
            overlayCanvas.transform.SetParent(transform, false);

            var canvas = overlayCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;

            var cursorObject = new GameObject("Unity Play MCP Virtual Cursor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cursorObject.transform.SetParent(overlayCanvas.transform, false);

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
                name = "Unity Play MCP Virtual Cursor Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            PaintCursorTexture(texture, darkTheme);
            return texture;
        }

        private static void PaintCursorTexture(Texture2D texture, bool darkTheme)
        {
            var pixels = new Color32[CursorWidth * CursorHeight];
            var borderColor = KeyboardStatusController.Foreground(darkTheme);
            var fillColor = KeyboardStatusController.Accent(darkTheme);

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
