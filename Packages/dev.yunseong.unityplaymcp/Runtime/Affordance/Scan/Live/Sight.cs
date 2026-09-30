using System.Collections.Generic;
using UnityPlayMcp.Affordances.Scan;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 보이는 요소가 화면 안에 있는지, 뒤에 그려진 요소에 가려졌는지 판정한다.
    /// </summary>
    /// <remarks>
    /// 가려짐은 추정이다. 한 canvas 안에서 uGUI 는 계층의 depth-first pre-order 로 그리므로 뒤의 요소가 앞 요소의 중심을
    /// 품으면 가려진 것으로 본다. <c>Canvas.sortingOrder</c>, <c>overrideSorting</c>, 투명한 그림, <c>RectMask2D</c> 는
    /// 반영하지 않는다. 확실한 답은 화면 캡처로 확인한다.
    ///
    /// canvas 가 다른 요소끼리는 root 계층 순서가 그리는 순서와 무관하므로 비교하지 않는다. 그래서 가려짐을 놓칠 수는 있어도,
    /// 보이는 요소를 가려졌다고 잘못 빼지는 않는다.
    ///
    /// 요소 수에 상한을 두지 않는다. 상한을 넘으면 <c>covered</c> 가 모르는 것을 false 로 보고하게 된다.
    /// </remarks>
    internal static class Sight
    {
        /// <summary>이 사각형이 화면과 겹치는가.</summary>
        /// <remarks>
        /// 넓이가 0 이면 화면 밖으로 본다. <see cref="ScreenArea.Of"/> 는 위치를 구할 수 없을 때 크기 0 을 돌려준다.
        /// </remarks>
        internal static bool OnScreen(Rect area, int width, int height)
        {
            if (area.width <= 0f || area.height <= 0f)
            {
                return false;
            }

            return area.xMax > 0f && area.yMax > 0f && area.x < width && area.y < height;
        }

        /// <summary><paramref name="later"/> 가 <paramref name="area"/> 의 중심을 품는가.</summary>
        /// <remarks>
        /// 겹친 넓이로 재면 임의의 비율 경계가 필요해 중심 한 점만 본다.
        /// </remarks>
        internal static bool Covers(Rect later, Rect area)
        {
            if (later.width <= 0f || later.height <= 0f)
            {
                return false;
            }

            return later.Contains(area.center);
        }

        /// <summary>
        /// 보이는 요소마다 pulse 에 붙일 JSON 조각을 instance id 로 돌려준다.
        /// </summary>
        /// <remarks>
        /// offer 와 같은 문자열 형태라 부르는 쪽은 <c>ledger</c> 에 그대로 붙인다.
        ///
        /// 보이는 요소가 아니면 map 에 없다. 없다는 것은 "안 가려졌다" 가 아니다.
        /// </remarks>
        internal static Dictionary<int, string> Survey(
            List<Transform> walked, int width, int height)
        {
            var said = new Dictionary<int, string>();
            var drawn = new List<Transform>();
            var areas = new List<Rect>();
            var canvases = new List<int>();

            foreach (var transform in walked)
            {
                if (transform == null || !Drawn.Any(transform.gameObject))
                {
                    continue;
                }

                // 꺼진 객체는 판정에서 뺀다. walk 는 꺼진 객체도 포함하고 GetWorldCorners 는 활성 여부를 보지 않아,
                // 닫힌 전체 화면 모달이 HUD 를 가린 것으로 판정된다.
                if (!transform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                drawn.Add(transform);
                areas.Add(ScreenArea.Of(transform));

                var canvas = transform.GetComponentInParent<Canvas>();
                canvases.Add(canvas == null ? 0 : canvas.GetInstanceID());
            }

            for (var at = 0; at < drawn.Count; at++)
            {
                said[drawn[at].gameObject.GetInstanceID()] =
                    ",\"onScreen\":" + (OnScreen(areas[at], width, height) ? "true" : "false") +
                    ",\"covered\":" + (Behind(drawn, areas, canvases, at) ? "true" : "false");
            }

            return said;
        }

        /// <summary>뒤에 그려지는 요소 중 이 요소의 중심을 덮는 것이 있는가.</summary>
        private static bool Behind(
            List<Transform> drawn, List<Rect> areas, List<int> canvases, int at)
        {
            for (var later = at + 1; later < drawn.Count; later++)
            {
                // canvas 가 다르면 그리는 순서를 계층에서 읽을 수 없다.
                if (canvases[later] != canvases[at] || canvases[at] == 0)
                {
                    continue;
                }

                if (!Covers(areas[later], areas[at]))
                {
                    continue;
                }

                // 자식은 위에 그려져도 부모의 일부라 가린 것으로 보지 않는다(버튼 위 캡션).
                if (drawn[later].IsChildOf(drawn[at]))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
