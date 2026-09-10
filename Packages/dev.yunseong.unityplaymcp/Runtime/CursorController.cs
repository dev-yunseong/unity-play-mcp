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

        /// <summary>이 controller 가 만든 overlay canvas. reload 를 건너 살아남는 유일한 손잡이다.</summary>
        /// <remarks>
        /// play 중 assembly reload 는 GameObject 를 하나도 파괴하지 않으므로 canvas 는 그대로 남고, 그것을
        /// 가리키던 field 만 사라진다. 그 상태에서 확인 없이 다시 만들면 cursor 가 두 벌이 된다 (issue #65).
        /// 그래서 이 참조 하나만 serialize 한다.
        ///
        /// 이름으로 <c>transform.Find</c> 하는 길을 버린 이유는 이름이 바뀌면 Find 가 조용히 실패해 바로 그
        /// 두 벌을 만들기 때문이다. 참조는 그 자체로 정확하다.
        ///
        /// inspector 에 내놓을 값은 아니다. 사람이 고르는 설정이 아니라 controller 가 제가 만든 것을 적어
        /// 두는 자리다.
        /// </remarks>
        [SerializeField, HideInInspector] private GameObject overlayCanvas;

        private RectTransform cursorTransform;
        private Texture2D cursorTexture;
        private Sprite cursorSprite;
        private bool darkTheme;

        /// <summary>이 domain 에서 cursor 를 만들었는지.</summary>
        /// <remarks>
        /// serialize 하지 않는다. reload 를 건너면 false 로 돌아오고, 여기서는 그것이 원하는 바다: 같은
        /// reload 에 함께 사라진 <see cref="cursorTransform"/> 이하를 다시 만들라고 <see cref="OnEnable"/>
        /// 에게 말하는 것이 이 false 다. <c>UnityPlayMcpHost.ownsRuntime</c> 이 같은 자리에서 같은 일을 한다.
        /// </remarks>
        private bool builtOverlay;

        public bool SmoothMovement
        {
            get { return smoothMovement; }
            set { smoothMovement = value; }
        }

        /// <summary>
        /// 첫 활성화와 assembly reload 가 함께 지나는 자리. 두 번 불러도 cursor 는 한 벌이다.
        /// </summary>
        /// <remarks>
        /// Unity 는 play 중 assembly reload 에서 <c>OnDisable</c> → serialize → domain 교체 → deserialize
        /// → <c>OnEnable</c> 순으로 가고 <c>Awake</c> 는 다시 부르지 않는다. 만드는 일이 <c>Awake</c> 에만
        /// 있던 동안 reload 를 건넌 controller 는 <see cref="cursorTexture"/> 가 null 인 채로
        /// <see cref="Update"/> 만 돌아 theme 이 바뀌는 프레임에 던졌고, <see cref="MoveTo(Vector2, Action{Vector2}, bool)"/>
        /// 는 null 확인에 걸려 조용히 빠져나가 cursor 를 그리지 않았다 (issue #65).
        ///
        /// 그냥 껐다 켜는 길로도 여기 온다. 그때는 <see cref="builtOverlay"/> 가 true 라 아무것도 하지
        /// 않는다 — 다시 만들면 cursor 가 깜빡이고 있던 자리를 잃는다.
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

        /// <summary>reload 를 건너 살아남은 overlay 를 걷어낸다.</summary>
        private void DiscardOverlay()
        {
            if (overlayCanvas == null)
            {
                return;
            }

            // texture 와 sprite 는 canvas 의 자식이 아니라 asset 이라, GameObject 를 지워도 함께 사라지지
            // 않는다. 이 둘이 reload 를 살아남았다면 여기 말고 놓아줄 자리가 없어, reload 한 번마다 36×48
            // texture 하나와 sprite 하나가 play 세션 끝까지 떠 있게 된다. 이미 사라졌으면 아래 확인에 걸려
            // 그냥 지나간다.
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

            // Destroy 는 프레임 끝에야 처리된다. 먼저 꺼 두지 않으면 새로 만든 cursor 와 살아남은 cursor 가
            // 그 한 프레임 동안 함께 그려진다.
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
