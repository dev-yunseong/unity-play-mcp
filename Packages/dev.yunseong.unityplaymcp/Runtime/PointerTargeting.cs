using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp
{
    /// <summary>
    /// 겨눈 화면 좌표와, 그 좌표에서 실제로 맞은 오브젝트.
    /// </summary>
    /// <remarks>
    /// 좌표는 Unity 의 좌하단 기준이다. 좌상단으로 뒤집는 일은 결과를 보고하는 자리에서만 한다 —
    /// 여기서 뒤집으면 <c>CursorController</c> 로 넘길 때 도로 뒤집어야 한다.
    /// </remarks>
    internal readonly struct PointerAim
    {
        public PointerAim(Vector2 screenPosition, int hitId, string hitName)
        {
            ScreenPosition = screenPosition;
            HitId = hitId;
            HitName = hitName;
        }

        public Vector2 ScreenPosition { get; }

        /// <summary>
        /// 겨눈 좌표에서 raycast 가 답한 그 오브젝트. 대상 자신일 수도, 그 자식일 수도 있다.
        /// </summary>
        /// <remarks>
        /// <c>GameObject</c> 가 아니라 id 와 이름을 베껴 든다. 겨눈 뒤 결과를 보고하기까지 세
        /// 프레임이 지나고, 그 사이에 게임이 대상을 파괴할 수 있다 — 죽는 적을 클릭하고 소모되는
        /// 카드를 드래그하는 것이 바로 이 issue 가 겨냥한 장면이다. 파괴된 <c>GameObject</c> 의
        /// <c>name</c> 을 읽으면 <c>MissingReferenceException</c> 이 나고, 그 예외는 coroutine 을
        /// 뚫고 나가 host 의 action queue 를 <c>processingActions</c> 가 참인 채로 세운다. 그러면
        /// 그 세션의 이후 액션이 전부 조용히 버려진다.
        /// </remarks>
        public int HitId { get; }

        /// <inheritdoc cref="HitId"/>
        public string HitName { get; }
    }

    /// <summary>
    /// ID 로 받은 대상을 포인터가 겨눌 화면 좌표로 바꾼다.
    /// </summary>
    internal static class PointerTargeting
    {
        /// <summary>
        /// 대상 면적 안에서 시험해 볼 좌표들. 면적의 가로세로 비율로 적는다.
        /// </summary>
        /// <remarks>
        /// 가운데 한 점만 보면 안 된다. 가운데가 비어 있는 collider 나, 가운데만 다른 것에 가린
        /// 카드가 실제로 있다 — issue #59 의 Validation Notes 가 적은 "rect 안의 점에서도 실제
        /// collider hit 여부가 달라 실패" 가 그 경우다. 다섯 점에서 멈추는 것은 이것이 한 번의
        /// 액션 안에서 도는 순수 질의이고, 더 촘촘히 훑어도 못 맞히는 모양이라면 좌표를 직접
        /// 겨누는 <c>move_mouse</c> 로 돌아가는 편이 정직하기 때문이다.
        /// </remarks>
        private static readonly Vector2[] Probes =
        {
            new Vector2(0.5f, 0.5f),
            new Vector2(0.25f, 0.25f),
            new Vector2(0.75f, 0.25f),
            new Vector2(0.25f, 0.75f),
            new Vector2(0.75f, 0.75f)
        };

        private static readonly RaycastHit[] SpatialHits = new RaycastHit[8];
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>
        /// 엔진이 그 좌표에서 <c>OnMouse*</c> 를 배달할 오브젝트 하나. 없으면 null.
        /// </summary>
        /// <remarks>
        /// <c>Camera.main</c> 에서 쏜 ray, 2D 와 3D 를 같은 거리로 비교, <c>Camera.eventMask</c>
        /// 로 거른다. 이 규칙이 여기 한 자리에만 있어야 한다 —
        /// <see cref="VirtualMouseMessenger"/> 는 매 프레임 이것으로 대상을 고르고, 겨누기는
        /// 같은 규칙으로 미리 확인한다. 둘이 갈라지면 "확인할 때는 맞았는데 배달은 딴 데로 간"
        /// 클릭이 된다.
        /// <para>
        /// 2D overlap 이 아니라 ray 인 것은, overlap 이 ray 가 놓치는 스프라이트까지 찾더라도
        /// 엔진과 같은 것을 고르는 편이 낫기 때문이다. 엔진이 고르지 못하는 것은 사람도 클릭하지
        /// 못하는 것이고, 그것을 클릭한 에이전트는 돌지 않는 게임을 돈다고 보고한다.
        /// </para>
        /// <para>
        /// 포인터 아래 전부가 아니라 하나만 고른다. 엔진이 하나만 골라 보내므로, 같은 깊이에
        /// 스프라이트가 겹친 게임은 그 모호함을 스스로 푼다. <c>Camera.main</c> 만 본다 — 엔진은
        /// 모든 카메라를 도므로, 두 번째 카메라로 상호작용 오브젝트를 그리는 씬은 덮지 못한다.
        /// </para>
        /// <para>
        /// 버퍼가 static 인 것은 안전하다. 메인 스레드에서만 돌고, 어느 호출자도 이것을
        /// <c>yield</c> 너머로 들고 가지 않는다.
        /// </para>
        /// </remarks>
        public static GameObject ColliderUnder(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            var ray = camera.ScreenPointToRay(screenPosition);
            var flat = Physics2D.GetRayIntersection(ray, camera.farClipPlane, camera.eventMask);

            var hitCount = Physics.RaycastNonAlloc(
                ray, SpatialHits, camera.farClipPlane, camera.eventMask);

            var closest = flat.collider == null ? float.MaxValue : flat.distance;
            var nearest = flat.collider == null ? null : flat.collider.gameObject;
            for (var index = 0; index < hitCount; index++)
            {
                if (SpatialHits[index].distance < closest)
                {
                    closest = SpatialHits[index].distance;
                    nearest = SpatialHits[index].collider.gameObject;
                }
            }

            return nearest;
        }

        /// <summary>
        /// raycast 가 답한 <paramref name="hit"/> 이 <paramref name="subject"/> 를 겨눈 것으로
        /// 쳐도 되는지.
        /// </summary>
        /// <remarks>
        /// 자기 자신과 자손은 두 경로 모두 참이다. 자손인 것은 collider 나 graphic 이 자식에
        /// 앉아 있을 때이고, 그때 엔진도 그 자식을 고른다 — 사람이 같은 자리를 클릭해도 같은
        /// 자식이 받는다.
        /// <para>
        /// 조상은 <paramref name="throughGraphics"/> 일 때만, 그것도 handler 사슬이 같을 때만
        /// 참이다. 두 경로가 배달하는 방식이 다르기 때문이다.
        /// <c>VirtualMouseMessenger.Send</c> 는 맞은 오브젝트 하나에 <c>SendMessage</c> 할 뿐
        /// 위아래로 걷지 않으므로, collider 경로에서 조상이 맞았다는 것은 대상 앞을 더 큰
        /// collider 가 가렸다는 뜻이고 대상의 <c>OnMouseDown</c> 은 영영 오지 않는다. uGUI 는
        /// <c>ExecuteHierarchy</c> 로 위로 걸어 올라가므로 조상이 맞아도 같은 handler 에 닿을 수
        /// 있는데, "닿을 수 있다" 를 믿지 말고 실제로 같은 handler 인지 물어본다 —
        /// <c>raycastTarget</c> 이 켜진 전체 화면 부모 panel 이 모든 자식을 가려 놓고도 전부
        /// 성공으로 보고되는 것이 그러지 않았을 때의 결과다.
        /// </para>
        /// </remarks>
        public static bool Reaches(GameObject subject, GameObject hit, bool throughGraphics)
        {
            if (subject == null || hit == null)
            {
                return false;
            }

            if (hit.transform.IsChildOf(subject.transform))
            {
                return true;
            }

            if (!throughGraphics || !subject.transform.IsChildOf(hit.transform))
            {
                return false;
            }

            return ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) ==
                   ExecuteEvents.GetEventHandler<IPointerClickHandler>(subject);
        }

        /// <summary>
        /// 대상을 겨눌 좌표를 찾는다. 다섯 후보 중 처음으로 대상에 닿는 것을 고른다.
        /// </summary>
        /// <remarks>
        /// hover 를 건드리지 않는 순수 질의다. 고르기 전에 커서를 옮기면 에이전트가 하지도 않은
        /// hover 가 후보점마다 게임으로 나간다.
        /// </remarks>
        public static bool TryAim(
            string method,
            int targetId,
            GameObject target,
            PointerEventDispatcher graphics,
            out PointerAim aim,
            out string error)
        {
            aim = default;

            // uGUI 로 답하는 대상과 collider 로 답하는 대상은 서로 다른 raycast 가 판단한다.
            // 대상이 사는 쪽으로 물어야 실제로 이벤트를 받을 그 경로가 확인되고, 면적을 재는
            // 방법도 같은 답을 따라야 한다 — 둘이 갈리면 Canvas 밖 RectTransform 이 corner 로
            // 재어지고 collider 로 물어져, 월드 단위를 화면 픽셀로 읽은 엉터리 면적을 겨눈다.
            var throughGraphics = AnswersAsGraphic(target);

            if (!TryScreenRect(target, throughGraphics, out var area, out var areaError))
            {
                error = method + ": target " + Describe(target, targetId) + " " + areaError;
                return false;
            }

            // Rect.Overlaps 를 쓰지 않는다. 그것은 너비가 0 인 면적을 겹치지 않는 것으로 보고,
            // 크기 0 인 collider 를 가진 대상이 화면 한가운데 있어도 화면 밖이라고 답한다.
            var onScreen = area.xMax >= 0f && area.xMin <= Screen.width &&
                           area.yMax >= 0f && area.yMin <= Screen.height;
            if (!onScreen)
            {
                error = string.Format(
                    "{0}: target {1} sits outside the {2}x{3} screen, at ({4:0}, {5:0}).",
                    method, Describe(target, targetId), Screen.width, Screen.height,
                    area.center.x, Screen.height - area.center.y);
                return false;
            }

            GameObject firstHit = null;
            var firstHitPoint = Vector2.zero;

            foreach (var probe in Probes)
            {
                var point = new Vector2(
                    Mathf.Lerp(area.xMin, area.xMax, probe.x),
                    Mathf.Lerp(area.yMin, area.yMax, probe.y));

                var hit = throughGraphics ? graphics.GraphicUnder(point) : ColliderUnder(point);
                if (Reaches(target, hit, throughGraphics))
                {
                    aim = new PointerAim(point, hit.GetInstanceID(), hit.name);
                    error = null;
                    return true;
                }

                if (firstHit == null && hit != null)
                {
                    firstHit = hit;
                    firstHitPoint = point;
                }
            }

            error = firstHit == null
                ? string.Format(
                    "{0}: nothing answered the pointer at any of the {1} points tried on target {2}. {3}",
                    method,
                    Probes.Length,
                    Describe(target, targetId),
                    throughGraphics
                        ? "Either it carries no Graphic with raycastTarget on, or the scene has no EventSystem."
                        : "Either it carries no Collider, or Camera.main does not draw its layer.")
                : string.Format(
                    // 좌표를 싣는다. 여기서 물러난 에이전트가 돌아갈 곳이 move_mouse 로 직접
                    // 겨누는 것이고, 그러려면 어디를 겨눴는지 알아야 한다.
                    "{0}: the pointer reached {1} instead of {2} at ({3:0}, {4:0}). "
                    + "Something is drawn or colliding on top of the target.",
                    method,
                    Describe(firstHit, firstHit.GetInstanceID()),
                    Describe(target, targetId),
                    firstHitPoint.x,
                    Screen.height - firstHitPoint.y);
            return false;
        }

        /// <summary>
        /// 대상이 uGUI raycast 로 답하는지. <c>Canvas</c> 아래의 <c>RectTransform</c> 이면 그렇다.
        /// </summary>
        private static bool AnswersAsGraphic(GameObject target)
        {
            return target.transform is RectTransform rect &&
                   rect.GetComponentInParent<Canvas>() != null;
        }

        /// <summary>
        /// 대상이 화면에서 차지하는 축 정렬 면적. Unity 좌표(좌하단 기준)다.
        /// </summary>
        /// <param name="throughGraphics">
        /// <see cref="AnswersAsGraphic"/> 가 답한 그대로를 받는다. 재는 방법과 hit 을 묻는 방법이
        /// 갈리지 않게 하는 것이 이 인자의 전부다.
        /// </param>
        private static bool TryScreenRect(
            GameObject target, bool throughGraphics, out Rect area, out string error)
        {
            if (throughGraphics)
            {
                error = null;
                area = RectTransformArea((RectTransform)target.transform);
                return true;
            }

            return TryBoundsArea(target, out area, out error);
        }

        private static Rect RectTransformArea(RectTransform rect)
        {
            rect.GetWorldCorners(Corners);
            var camera = CanvasCamera.For(rect);

            var min = (Vector2)RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
            var max = min;
            for (var index = 1; index < 4; index++)
            {
                var point = (Vector2)RectTransformUtility.WorldToScreenPoint(camera, Corners[index]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// collider 나 renderer 의 world bounds 를 화면으로 투영한다.
        /// </summary>
        /// <remarks>
        /// collider 를 renderer 보다 먼저 본다. 포인터가 맞히는 것은 collider 이고, 스프라이트의
        /// 그림이 collider 보다 큰 경우가 흔하다 — renderer 를 먼저 보면 collider 밖을 겨눈다.
        /// </remarks>
        private static bool TryBoundsArea(GameObject target, out Rect area, out string error)
        {
            area = default;

            var camera = Camera.main;
            if (camera == null)
            {
                // 태그를 안 단 카메라는 흔한 설정 실수다. 대상 탓으로 말하면 엉뚱한 데를 고치게
                // 된다.
                error = "cannot be aimed at: the scene has no Camera tagged MainCamera.";
                return false;
            }

            var flat = target.GetComponentInChildren<Collider2D>();
            var spatial = flat == null ? target.GetComponentInChildren<Collider>() : null;
            var renderer = flat == null && spatial == null
                ? target.GetComponentInChildren<Renderer>()
                : null;

            Bounds bounds;
            if (flat != null)
            {
                bounds = flat.bounds;
            }
            else if (spatial != null)
            {
                bounds = spatial.bounds;
            }
            else if (renderer != null)
            {
                bounds = renderer.bounds;
            }
            else
            {
                error = "has no Collider, Collider2D, or Renderer to aim at, "
                        + "and no RectTransform under a Canvas either.";
                return false;
            }

            error = null;
            var min = Vector2.zero;
            var max = Vector2.zero;
            for (var index = 0; index < 8; index++)
            {
                var corner = new Vector3(
                    (index & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (index & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (index & 4) == 0 ? bounds.min.z : bounds.max.z);

                var point = (Vector2)camera.WorldToScreenPoint(corner);
                if (index == 0)
                {
                    min = point;
                    max = point;
                    continue;
                }

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            area = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        /// <summary>에러 문장에 들어갈 이름. 이름만으로는 어느 것인지 모르므로 id 를 붙인다.</summary>
        public static string Describe(GameObject subject, int id)
        {
            return subject == null ? "#" + id : subject.name + "#" + id;
        }
    }
}
