using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp
{
    /// <summary>
    /// 겨눈 화면 좌표와 그 좌표에서 실제로 맞은 오브젝트다.
    /// </summary>
    /// <remarks>
    /// 좌표는 Unity 의 좌하단 기준이다. <c>CursorController</c> 가 이 좌표계를 쓰므로 좌상단 변환은 결과를 보고할 때만 한다.
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
        /// 겨눈 좌표에서 raycast 가 맞힌 오브젝트다. 대상 자신이거나 그 자식이다.
        /// </summary>
        /// <remarks>
        /// 결과 보고 전에 게임이 대상을 파괴할 수 있으므로 <c>GameObject</c> 대신 id 와 이름을 복사해 둔다.
        /// 파괴된 오브젝트의 <c>name</c> 을 읽으면 <c>MissingReferenceException</c> 이 coroutine 밖으로 나가
        /// host 의 action queue 가 <c>processingActions</c> 가 true 인 채로 멈추고 이후 action 이 모두 버려진다.
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
        /// 대상 면적 안에서 시험할 좌표다. 면적에 대한 비율로 적는다.
        /// </summary>
        /// <remarks>
        /// 가운데가 비었거나 가려진 대상이 있으므로 한 점만 보지 않는다 (#59). 다섯 점으로 못 맞히는 모양은
        /// <c>move_mouse</c> 로 좌표를 직접 겨누게 한다.
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
        /// 엔진이 그 좌표에서 <c>OnMouse*</c> 를 보낼 오브젝트다. 없으면 null 이다.
        /// </summary>
        /// <remarks>
        /// <c>Camera.main</c> 의 ray 로 2D 와 3D hit 을 거리로 비교하고 <c>Camera.eventMask</c> 로 거른다.
        /// <see cref="VirtualMouseMessenger"/> 와 targeting 이 모두 이 규칙을 쓴다. 둘이 다르면 확인한 대상과
        /// 실제로 이벤트를 받는 대상이 달라진다.
        /// <para>
        /// 엔진과 같은 대상을 고르도록 2D overlap 이 아니라 ray 를 쓴다. 엔진은 모든 카메라를 보지만 여기서는
        /// <c>Camera.main</c> 만 보므로, 두 번째 카메라가 그리는 상호작용 오브젝트는 지원하지 않는다.
        /// </para>
        /// <para>
        /// static 버퍼는 main thread 에서만 쓰고 <c>yield</c> 를 넘어 들고 가지 않으므로 안전하다.
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
        /// raycast 가 맞힌 <paramref name="hit"/> 을 <paramref name="subject"/> 를 겨눈 것으로 볼지 판정한다.
        /// </summary>
        /// <remarks>
        /// 자기 자신과 자손은 두 경우 모두 true 다. collider 나 graphic 이 자식에 있으면 엔진도 그 자식을 고른다.
        /// <para>
        /// 조상은 <paramref name="throughGraphics"/> 이고 click handler 가 같을 때만 true 다.
        /// <c>VirtualMouseMessenger.Send</c> 는 맞은 오브젝트에만 <c>SendMessage</c> 하므로, collider 경우에
        /// 조상이 맞으면 대상이 가려진 것이다. uGUI 는 <c>ExecuteHierarchy</c> 로 올라가지만, <c>raycastTarget</c>
        /// 이 켜진 전체 화면 부모 panel 을 성공으로 보지 않도록 handler 가 같은지 확인한다.
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
        /// hover 를 바꾸지 않는다. 커서를 옮기며 고르면 후보점마다 게임에 hover 이벤트가 나간다.
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

            // 면적 측정과 hit 확인은 같은 경우(uGUI 또는 collider)를 따라야 한다. 다르면 Canvas 밖
            // RectTransform 의 월드 단위를 화면 픽셀로 읽어 잘못된 면적을 겨눈다.
            var throughGraphics = AnswersAsGraphic(target);

            if (!TryScreenRect(target, throughGraphics, out var area, out var areaError))
            {
                error = method + ": target " + Describe(target, targetId) + " " + areaError;
                return false;
            }

            // Rect.Overlaps 는 너비 0 인 면적을 겹치지 않는 것으로 보아 크기 0 collider 를 화면 밖으로 판정한다.
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
                    // agent 가 move_mouse 로 직접 겨눌 수 있도록 좌표를 포함한다.
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
        /// 포인터가 <paramref name="point"/> 에 있을 때 여전히 대상에 닿는지 확인한다.
        /// </summary>
        /// <remarks>
        /// <see cref="TryAim"/> 과 같은 경우(uGUI 또는 collider)로 확인한다. 커서가 이동하는 사이 게임이
        /// 대상을 옮기거나 가릴 수 있다.
        /// </remarks>
        /// <param name="hit">그 자리에서 실제로 맞은 오브젝트다. 없으면 null 이다.</param>
        public static bool StillReaches(
            GameObject target, Vector2 point, PointerEventDispatcher graphics, out GameObject hit)
        {
            var throughGraphics = AnswersAsGraphic(target);
            hit = throughGraphics ? graphics.GraphicUnder(point) : ColliderUnder(point);
            return Reaches(target, hit, throughGraphics);
        }

        /// <summary>
        /// <c>Canvas</c> 아래의 <c>RectTransform</c> 이면 uGUI raycast 로 판정한다.
        /// </summary>
        private static bool AnswersAsGraphic(GameObject target)
        {
            return target.transform is RectTransform rect &&
                   rect.GetComponentInParent<Canvas>() != null;
        }

        /// <summary>
        /// 대상이 화면에서 차지하는 축 정렬 면적이다. 좌하단 기준 Unity 좌표다.
        /// </summary>
        /// <param name="throughGraphics">
        /// <see cref="AnswersAsGraphic"/> 의 결과다. 면적 측정과 hit 확인이 같은 경우를 따르게 한다.
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
        /// 포인터가 맞히는 것은 collider 이고 sprite 가 collider 보다 큰 경우가 흔하므로 collider 를 먼저 본다.
        /// </remarks>
        private static bool TryBoundsArea(GameObject target, out Rect area, out string error)
        {
            area = default;

            var camera = Camera.main;
            if (camera == null)
            {
                // MainCamera 태그 누락은 흔한 설정 실수이므로 대상 문제와 구분해 보고한다.
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

        /// <summary>에러 메시지용 이름이다. 이름이 겹칠 수 있으므로 id 를 붙인다.</summary>
        public static string Describe(GameObject subject, int id)
        {
            return subject == null ? "#" + id : subject.name + "#" + id;
        }
    }
}
