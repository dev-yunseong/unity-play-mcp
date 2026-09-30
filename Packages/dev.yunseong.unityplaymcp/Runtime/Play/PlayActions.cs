using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityPlayMcp.Play
{
    /// <summary>행동 하나의 서술. 기본 어댑터가 만드는 확인 가능한 사실만 담는다.</summary>
    internal sealed class ActionDescriptor
    {
        public string Kind;
        public string Label;
        public string Availability;
        public string AcceptsTarget;
        public string Source = "runtime";
        public List<Dictionary<string, object>> Recipe = new List<Dictionary<string, object>>();
        public List<FactDto> Evidence = new List<FactDto>();
        public List<FactDto> TargetConstraints = new List<FactDto>();
        public List<FactDto> Costs = new List<FactDto>();
        public List<FactDto> ExpectedEffects = new List<FactDto>();
        public List<object> OutcomePredicates = new List<object>();
        public List<PreconditionDto> Preconditions = new List<PreconditionDto>();
        public string ProviderId;
        public string ActionId;
    }

    /// <summary>
    /// actionRef 의 형식과 기본 행동 목록.
    /// </summary>
    /// <remarks>
    /// actionRef 는 호출하는 쪽에는 불투명한 문자열이다. 안에 session, 엔터티 handle(generation 포함), 행동 종류를 담아
    /// 낡은 ref 로 다른 대상에 입력하지 않게 한다.
    /// <para>
    /// 기본 어댑터는 게임 규칙을 모른다. UI Selectable 의 interactable, 포인터 handler 의 유무, collider 의 유무 같은
    /// 확인 가능한 사실로 가능 여부를 정하고, 그것이 게임 안의 성공을 뜻하지 않는다고 <c>note</c> 에 적는다.
    /// </para>
    /// </remarks>
    internal static class PlayActions
    {
        public const string Note =
            "Availability comes from observable input facts (active, interactable, on-screen, handlers). "
            + "It does not tell whether the game rules will accept the action or what it will do.";

        private const char Separator = '|';

        public static string BuildRef(EntityRefDto entity, string kind)
        {
            return "act" + Separator + entity.SessionId + Separator + entity.Id + Separator + entity.Generation
                + Separator + kind;
        }

        public static string BuildProviderRef(EntityRefDto entity, string providerId, string actionId)
        {
            return "pact" + Separator + entity.SessionId + Separator + entity.Id + Separator + entity.Generation
                + Separator + providerId + Separator + actionId;
        }

        /// <summary>actionRef 를 나눈다. 형식이 틀리면 false 다.</summary>
        public static bool TryParse(
            string actionRef, out EntityRefDto entity, out string kind, out string providerId, out string actionId)
        {
            entity = null;
            kind = null;
            providerId = null;
            actionId = null;
            if (string.IsNullOrEmpty(actionRef))
            {
                return false;
            }

            var parts = actionRef.Split(Separator);
            int id, generation;
            if (parts.Length < 5 || !int.TryParse(parts[2], out id) || !int.TryParse(parts[3], out generation))
            {
                return false;
            }

            entity = new EntityRefDto { SessionId = parts[1], Id = id, Generation = generation };
            if (parts[0] == "act" && parts.Length == 5)
            {
                kind = parts[4];
                return true;
            }

            if (parts[0] == "pact" && parts.Length == 6)
            {
                providerId = parts[4];
                actionId = parts[5];
                kind = "provider";
                return true;
            }

            return false;
        }

        /// <summary>기본 어댑터가 이 엔터티에 대해 말할 수 있는 행동들.</summary>
        public static List<ActionDescriptor> Describe(GameObject entity)
        {
            var list = new List<ActionDescriptor>();
            var selectable = entity.GetComponent<Selectable>();
            var hasCollider = entity.GetComponent<Collider>() != null || entity.GetComponent<Collider2D>() != null;
            var clickable = entity.GetComponent<IPointerClickHandler>() != null
                || entity.GetComponent<IPointerDownHandler>() != null;
            var draggable = entity.GetComponent<IDragHandler>() != null || entity.GetComponent<IBeginDragHandler>() != null;
            var hoverable = entity.GetComponent<IPointerEnterHandler>() != null;

            if (selectable != null || hasCollider || clickable)
            {
                list.Add(Pointer(entity, "click", "Click " + entity.name, selectable, null));
            }

            if (draggable)
            {
                var drag = Pointer(entity, "drag", "Drag " + entity.name, selectable, "entity");
                drag.Recipe.Clear();
                float x, y;
                if (PlayGeometry.TryScreenCenter(entity, out x, out y))
                {
                    drag.Recipe.Add(new Dictionary<string, object>
                    {
                        { "method", "pointerDrag" }, { "fromX", x }, { "fromY", y },
                        { "toX", "$target.x" }, { "toY", "$target.y" }
                    });
                }

                drag.TargetConstraints.Add(Unknown("targetValidity", "the drop target's rules are game specific"));
                list.Add(drag);
            }

            if (hoverable)
            {
                list.Add(Pointer(entity, "hover", "Hover " + entity.name, selectable, null, hover: true));
            }

            return list;
        }

        private static ActionDescriptor Pointer(
            GameObject entity, string kind, string label, Selectable selectable, string acceptsTarget, bool hover = false)
        {
            var descriptor = new ActionDescriptor { Kind = kind, Label = label, AcceptsTarget = acceptsTarget };
            float x, y;
            var onScreen = PlayGeometry.TryScreenCenter(entity, out x, out y);
            var active = entity.activeInHierarchy;
            var interactable = selectable == null ? (bool?)null : selectable.IsInteractable();

            descriptor.Evidence.Add(Known("active", active));
            descriptor.Evidence.Add(Known("onScreen", onScreen));
            if (interactable.HasValue)
            {
                descriptor.Evidence.Add(Known("interactable", interactable.Value));
            }

            if (!active || !onScreen || interactable == false)
            {
                descriptor.Availability = "unavailable";
            }
            else
            {
                descriptor.Availability = "available";
            }

            if (onScreen)
            {
                var step = new Dictionary<string, object>
                {
                    { "method", hover ? "pointerHover" : "pointerClick" }, { "x", x }, { "y", y }
                };
                descriptor.Recipe.Add(step);
            }

            // 비용, 쿨다운, 효과는 기본 어댑터가 알 수 없다. 0 이 아니라 unknown 으로 남긴다.
            descriptor.Costs.Add(Unknown("cost", "no provider declares a cost"));
            descriptor.ExpectedEffects.Add(Unknown("effect", "no provider declares an effect; the game decides what the input does"));
            return descriptor;
        }

        private static FactDto Known(string name, object value)
        {
            return new FactDto
            {
                Name = name, Value = value, Status = "known", Source = "runtime", Evidence = new EvidenceDto()
            };
        }

        internal static FactDto Unknown(string name, string reason)
        {
            return new FactDto
            {
                Name = name, Status = "unknown", Source = "runtime", Evidence = new EvidenceDto(), Reason = reason
            };
        }
    }
}
