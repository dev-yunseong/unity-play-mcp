using System.Collections.Generic;
using UnityPlayMcp.Affordances.Scan;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 보이는 요소가 지금 눈에 닿는가: 화면 안인지, 그리고 뒤에 그려진 것에 가려졌는지.
    /// </summary>
    /// <remarks>
    /// 요소가 어디 있는지는 <c>rect</c> 가 이미 말한다. 그것으로 답할 수 없는 것이 둘이다 — 그 사각형이 화면 밖이라 아무도 못
    /// 보는 것인지, 그리고 그 위에 다른 것이 칠해져 있는지. 화면 밖인지는 <c>Screen</c> 의 크기를 아는 이쪽만 답할 수 있다.
    ///
    /// <b>가려짐은 추측이다.</b> 한 canvas 안에서 uGUI 가 그리는 순서는 계층의 depth-first pre-order 이고, 그것이 걷는 순서와
    /// 같다 — 뒤에 오는 것이 위에 그려진다. 그래서 뒤에 온 요소가 앞의 요소의 한가운데를 품으면 앞의 것을 가려진 것으로 본다.
    /// 이 규칙이 모르는 것: <c>Canvas.sortingOrder</c>, <c>overrideSorting</c>, 투명한 그림, <c>RectMask2D</c> 로 잘린 것.
    /// 확실한 답이 필요한 쪽에는 화면 캡처가 있고, tool 설명이 그렇게 말한다.
    ///
    /// canvas 가 다른 두 요소는 아예 비교하지 않는다. HUD 위에 모달을 얹는 게임 — UI 가 겹치는 바로 그 게임 — 에서 root
    /// 두 개의 계층 순서는 그리는 순서와 아무 관계가 없다. 이렇게 하면 <b>놓치는 쪽으로</b> 틀린다: 모달이 HUD 를 덮어도 안
    /// 덮었다고 한다. 덮었다고 잘못 말하는 것보다 낫다 — 앞의 실수는 요소가 하나 더 나오는 것이고, 뒤의 실수는 화면에 있는
    /// 요소가 목록에서 사라지는 것이다.
    ///
    /// 꺼져 있는 요소는 아예 보지 않는다 — 덮개로도, 답을 내는 대상으로도. 자세한 이유는 <see cref="Survey"/> 안에 있다.
    ///
    /// 요소 수에 상한을 두지 않는다. 상한 위에서 <c>covered</c> 가 조용히 거짓이 되는데, 모르는 것을 아니오로 보고하는 것은 이
    /// 패키지가 다른 모든 자리에서 거절하는 모양이다. 첫 덮개에서 멈추고, 계층을 거슬러 오르는 검사는 canvas 와 사각형 검사를
    /// 통과한 뒤에만 돈다.
    /// </remarks>
    internal static class Sight
    {
        /// <summary>이 사각형이 화면과 겹치는가.</summary>
        /// <remarks>
        /// 넓이가 0 인 것은 화면 밖으로 친다. <see cref="ScreenArea.Of"/> 는 아무 데도 아닌 것에 크기 0 인 면적을 주고,
        /// 그것은 아무도 볼 수 없는 것이다.
        /// </remarks>
        internal static bool OnScreen(Rect area, int width, int height)
        {
            if (area.width <= 0f || area.height <= 0f)
            {
                return false;
            }

            return area.xMax > 0f && area.yMax > 0f && area.x < width && area.y < height;
        }

        /// <summary><paramref name="later"/> 가 <paramref name="area"/> 의 한가운데를 품는가.</summary>
        /// <remarks>
        /// 한가운데 하나만 본다. 겹친 넓이를 재면 반쯤 가려진 것에 대해 임의의 경계를 골라야 하고, 그 경계는 어느 게임에도
        /// 맞지 않는 숫자가 된다.
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
        /// 걸어온 것들 중 보이는 요소마다, pulse 에 그대로 붙일 조각을 그 instance id 에 걸어 돌려준다.
        /// </summary>
        /// <remarks>
        /// 문자열로 돌려주는 것은 offer 가 이미 쓰는 모양이다. 부르는 쪽은 그것을 장부에 대고 붙이기만 하면 되고, 값이 그대로면
        /// 장부가 알아서 막는다.
        ///
        /// 보이는 요소가 아닌 것은 map 에 아예 안 들어간다. 없음은 "안 가려졌다" 가 아니라 "보이는 요소가 아니다" 다.
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

                // 꺼져 있는 것은 덮개로도 세지 않고 답도 내지 않는다. 걷기는 꺼진 객체까지 걷고 GetWorldCorners 는 켜짐을
                // 보지 않으므로, 닫힌 모달 — 같은 canvas 아래, HUD 뒤에 선언된 전체 화면 Image — 이 그러지 않으면 HUD 를
                // 통째로 가려진 것으로 만든다. 그러면 화면에 실제로 보이는 것이 목록에서 사라진다.
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

        /// <summary>이것보다 뒤에 그려지는 것 중 이것의 한가운데를 덮는 것이 있는가.</summary>
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

                // 버튼 위의 캡션이 그 버튼을 가렸다고 말하면 안 된다. 자식은 그것 위에 그려지지만 그것의 일부다.
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
