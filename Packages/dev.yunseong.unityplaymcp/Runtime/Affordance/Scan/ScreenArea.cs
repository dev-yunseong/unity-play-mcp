using UnityEngine;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 객체가 화면에서 차지하는 면적. 좌상단 기준 픽셀이다.
    /// </summary>
    /// <remarks>
    /// Unity 는 좌하단 기준이고 액션 프로토콜은 좌상단 기준이므로 여기서 뒤집어, 호출자가 보고된 값을 그대로 액션에 쓸 수 있게 한다.
    /// SDK 런타임에 같은 규칙의 코드가 있지만, SDK 런타임이 이 어셈블리를 참조하므로 공유하지 못하고 복사해 둔다.
    /// </remarks>
    internal static class ScreenArea
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        private static Camera _camera;

        /// <summary>스캔마다 카메라를 한 번 찾는다.</summary>
        /// <remarks>
        /// <c>Camera.main</c> 은 태그로 씬 전체를 조회하므로 객체마다 부르면 느리다.
        /// </remarks>
        internal static void Begin()
        {
            _camera = Camera.main;
        }

        internal static void Forget()
        {
            _camera = null;
        }

        /// <summary>화면상의 면적. 대상이 없으면 크기 0 이다.</summary>
        internal static Rect Of(Transform subject)
        {
            if (subject == null)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            if (subject is RectTransform rect)
            {
                return FromCorners(rect);
            }

            // 월드 객체의 transform 은 넓이가 없는 점이므로 renderer 의 bounds 로 면적을 구한다.
            var renderer = subject.GetComponent<Renderer>();

            return renderer == null ? AtPoint(subject.position) : FromBounds(renderer.bounds);
        }

        private static Rect FromCorners(RectTransform subject)
        {
            subject.GetWorldCorners(Corners);

            var canvas = subject.GetComponentInParent<Canvas>();

            // ScreenSpaceOverlay 캔버스의 코너는 이미 화면 좌표이므로 카메라로 투영하지 않는다.
            var through = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _camera;

            var first = Project(Corners[0], through);
            var min = first;
            var max = first;

            for (var index = 1; index < 4; index++)
            {
                var point = Project(Corners[index], through);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Between(min, max);
        }

        private static Rect FromBounds(Bounds bounds)
        {
            var min = Vector2.zero;
            var max = Vector2.zero;

            for (var index = 0; index < 8; index++)
            {
                var corner = new Vector3(
                    (index & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (index & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (index & 4) == 0 ? bounds.min.z : bounds.max.z);

                var point = Project(corner, _camera);

                if (index == 0)
                {
                    min = point;
                    max = point;
                    continue;
                }

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Between(min, max);
        }

        private static Rect AtPoint(Vector3 world)
        {
            var point = Project(world, _camera);

            return new Rect(point.x, point.y, 0f, 0f);
        }

        /// <summary>월드 좌표를 좌상단 기준 화면 좌표로 바꾼다.</summary>
        private static Vector2 Project(Vector3 world, Camera through)
        {
            var point = through == null
                ? new Vector3(world.x, world.y, 0f)
                : through.WorldToScreenPoint(world);

            return new Vector2(point.x, Screen.height - point.y);
        }

        private static Rect Between(Vector2 min, Vector2 max)
        {
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }
    }
}
