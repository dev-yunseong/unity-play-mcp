using UnityEngine;

namespace UnityPlayMcp.Play
{
    /// <summary>
    /// 엔터티의 화면 위치와 카메라 선택. 좌표는 Unity Screen 픽셀이고 결과는 좌상단 기준이다.
    /// </summary>
    /// <remarks>
    /// 게임 종류를 가정하지 않는다. 바닥 평면이나 고정 해상도를 쓰지 않는다.
    /// </remarks>
    public static class PlayGeometry
    {
        /// <summary>엔터티의 화면 위 사각형(좌상단 기준). 화면에 그릴 수 없으면 false 다.</summary>
        internal static bool TryScreenRect(GameObject entity, out Rect rect)
        {
            rect = default;
            if (entity == null)
            {
                return false;
            }

            var height = Screen.height;
            var rectTransform = entity.GetComponent<RectTransform>();
            if (rectTransform != null && entity.GetComponentInParent<Canvas>() != null)
            {
                var corners = new Vector3[4];
                rectTransform.GetWorldCorners(corners);
                var camera = CanvasCamera.For(rectTransform);
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var corner in corners)
                {
                    var point = (Vector2)RectTransformUtility.WorldToScreenPoint(camera, corner);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                rect = new Rect(min.x, height - max.y, max.x - min.x, max.y - min.y);
                return true;
            }

            Bounds bounds;
            if (!TryWorldBounds(entity, out bounds))
            {
                return false;
            }

            var worldCamera = WorldCamera();
            if (worldCamera == null)
            {
                return false;
            }

            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            var c = bounds.center;
            var e = bounds.extents;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                var screen = worldCamera.WorldToScreenPoint(corner);
                if (screen.z < 0f)
                {
                    // 카메라 뒤에 있는 모서리로는 사각형을 만들 수 없다.
                    return false;
                }

                lo = Vector2.Min(lo, screen);
                hi = Vector2.Max(hi, screen);
            }

            rect = new Rect(lo.x, height - hi.y, hi.x - lo.x, hi.y - lo.y);
            return true;
        }

        /// <summary>엔터티의 화면 위 중심. 화면 밖이면 false 다.</summary>
        public static bool TryScreenCenter(GameObject entity, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            Rect rect;
            if (!TryScreenRect(entity, out rect))
            {
                return false;
            }

            x = rect.center.x;
            y = rect.center.y;
            return x >= 0f && x <= Screen.width && y >= 0f && y <= Screen.height;
        }

        /// <summary>world 공간의 bounds. renderer, 3D collider, 2D collider 순으로 쓴다.</summary>
        internal static bool TryWorldBounds(GameObject entity, out Bounds bounds)
        {
            return TryWorldBounds(entity, out bounds, out _);
        }

        internal static bool TryWorldBounds(GameObject entity, out Bounds bounds, out string source)
        {
            var renderer = entity.GetComponent<Renderer>();
            if (renderer != null)
            {
                bounds = renderer.bounds;
                source = "renderer";
                return true;
            }

            var collider = entity.GetComponent<Collider>();
            if (collider != null)
            {
                bounds = collider.bounds;
                source = "collider3d";
                return true;
            }

            var collider2D = entity.GetComponent<Collider2D>();
            if (collider2D != null)
            {
                bounds = collider2D.bounds;
                source = "collider2d";
                return true;
            }

            bounds = default;
            source = null;
            return false;
        }

        /// <summary>world 공간 질의에 쓸 카메라. main 이 있으면 그것, 없으면 하나뿐인 활성 카메라다.</summary>
        internal static Camera WorldCamera()
        {
            var main = Camera.main;
            if (main != null)
            {
                return main;
            }

            string problem;
            return OnlyCamera(out problem);
        }

        /// <summary>활성 카메라가 정확히 하나면 그것을 돌려준다. 아니면 이유를 <paramref name="problem"/> 에 준다.</summary>
        internal static Camera OnlyCamera(out string problem)
        {
            problem = null;
            Camera found = null;
            var count = 0;
            foreach (var camera in Camera.allCameras)
            {
                if (camera == null || !camera.isActiveAndEnabled)
                {
                    continue;
                }

                found = camera;
                count++;
            }

            if (count == 0)
            {
                problem = "no_camera";
                return null;
            }

            if (count > 1)
            {
                // 고를 근거가 없다. 추측하지 않고 호출한 쪽이 명시하게 한다.
                problem = "ambiguous_camera";
                return null;
            }

            return found;
        }

        /// <summary>UI 요소나 renderer 가 플레이어 화면에 그려지는지 판정한다. 게임 규칙상 가시성은 보증하지 않는다.</summary>
        internal static bool IsRendered(GameObject entity, out string basis)
        {
            if (!entity.activeInHierarchy)
            {
                basis = "inactive";
                return false;
            }

            var graphic = entity.GetComponent<UnityEngine.UI.Graphic>();
            var isUi = entity.GetComponent<RectTransform>() != null && entity.GetComponentInParent<Canvas>() != null;
            if (isUi)
            {
                basis = "ui.screenRect";
                foreach (var group in entity.GetComponentsInParent<CanvasGroup>())
                {
                    if (group.alpha <= 0f)
                    {
                        return false;
                    }

                    if (group.ignoreParentGroups)
                    {
                        break;
                    }
                }

                Rect rect;
                if (!TryScreenRect(entity, out rect))
                {
                    return false;
                }

                var onScreen = rect.xMax > 0f && rect.xMin < Screen.width && rect.yMax > 0f && rect.yMin < Screen.height;
                return onScreen && (graphic == null || graphic.canvasRenderer == null || !graphic.canvasRenderer.cull);
            }

            basis = "renderer.isVisible";
            foreach (var renderer in entity.GetComponentsInChildren<Renderer>())
            {
                if (renderer.enabled && renderer.isVisible)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
