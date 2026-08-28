using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 객체가 화면에서 차지하는 면적. 에이전트가 겨누는 숫자로.
    /// </summary>
    /// <remarks>
    /// 좌상단에서 잰 픽셀이고, 그것은 Unity 가 일하는 공간이 아니다. 엔진은 좌하단에서 재고 액션 프로토콜은 위에서 재므로,
    /// 뒤집기는 와이어의 이쪽 아니면 저쪽에서 일어나야 한다. 여기서 일어난다. 그래야 호출자가 스캔이 보고한 숫자를 그대로
    /// 겨눌 곳으로 되보낼 수 있다.
    ///
    /// SDK 에는 같은 규칙의 reader 가 제 것으로 있다. 그것을 공유하려면 이 어셈블리가 SDK 런타임을 참조해야 하는데, SDK
    /// 런타임이 곧 이쪽을 참조하려 한다 — 연결이 열릴 때 스캔이 시작될 수 있어야 하기 때문이다. 두 방향 중 하나는 복사여야
    /// 하고, 그 복사는 SDK 가 전혀 없어도 계속 돌아야 하는 쪽에 있어야 한다.
    /// </remarks>
    internal static class ScreenArea
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        private static Camera _camera;

        /// <summary>객체마다가 아니라 스캔 전체에 대해 카메라를 한 번 푼다.</summary>
        /// <remarks>
        /// <c>Camera.main</c> 은 태그로 찾고, 그것은 씬 전체 조회다. 객체마다 치르면 웬만한 크기의 씬에서 순회를 잡아먹는다.
        /// </remarks>
        internal static void Begin()
        {
            _camera = Camera.main;
        }

        internal static void Forget()
        {
            _camera = null;
        }

        /// <summary>이것이 화면의 어디인지, 또는 아무 데도 아닐 때 크기 0 인 면적.</summary>
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

            // 스프라이트는 RectTransform 이 아니고, 맨 transform 은 넓이가 없는 점이다. renderer 자신의 bounds 를 읽는 것이
            // 그런 것을 겨눌 수 있게 만드는 유일한 방법이다 — 그것이 없으면 모든 월드 객체가 제 한가운데의 너비 0 인 면적을
            // 보고하게 된다.
            var renderer = subject.GetComponent<Renderer>();

            return renderer == null ? AtPoint(subject.position) : FromBounds(renderer.bounds);
        }

        private static Rect FromCorners(RectTransform subject)
        {
            subject.GetWorldCorners(Corners);

            var canvas = subject.GetComponentInParent<Canvas>();

            // 스크린 공간에 그려지는 캔버스는 그것과 화면 사이에 카메라가 없다. 그런 캔버스에 코너를 투영하라고 청하면 플레이어가
            // 결코 보지 못하는 데로 옮겨 놓게 된다.
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

        /// <summary>월드에서 화면으로. 위에서 아래로 재도록 이미 뒤집은 것.</summary>
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
