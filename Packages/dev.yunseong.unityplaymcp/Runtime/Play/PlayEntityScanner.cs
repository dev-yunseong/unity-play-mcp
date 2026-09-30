using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnityPlayMcp.Play
{
    internal sealed class ScanOptions
    {
        public bool PlayerScope = true;
        public FilterDto Filter;
        public int MaxEntities = 50;
        public int FactsPerEntity = 20;
        public bool IncludeActions = true;
        public bool IncludeFacts = true;
    }

    internal sealed class ScanResult
    {
        public List<EntityDto> Entities = new List<EntityDto>();
        public List<GameObject> Objects = new List<GameObject>();
        public int Omitted;
        public string VisibilityBasis;
        public string VisibilityGuarantee;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 어댑터 없이 장면의 상호작용 가능한 엔터티를 모은다: UI Selectable, 포인터 handler 가 있는 UI, 3D/2D collider,
    /// 화면에 글자를 보이는 Text/TMP. 게임 클래스나 필드 이름을 알지 못한다.
    /// </summary>
    /// <remarks>
    /// 매 프레임 scan 하지 않는다. 호출할 때 한 번 훑는다. 순서는 instance id 오름차순이라 같은 장면에서 안정적이다.
    /// </remarks>
    internal sealed class PlayEntityScanner
    {
        private const string OwnRootPrefix = "Unity Play MCP";
        private const int MaxTypes = 8;

        private readonly EntityRegistry registry;

        public PlayEntityScanner(EntityRegistry registry)
        {
            this.registry = registry;
        }

        public EntityRegistry Registry { get { return registry; } }

        public ScanResult Scan(ScanOptions options, int frame, string scene, double gameTime)
        {
            var result = new ScanResult();
            var strict = options.PlayerScope && PlaySemantics.HasVisibilityProvider();
            result.VisibilityBasis = options.PlayerScope
                ? (strict ? "provider" : "ui.screenRect|renderer.isVisible")
                : "none";
            result.VisibilityGuarantee = strict ? "provider" : "none";
            if (options.PlayerScope && !strict)
            {
                result.Warnings.Add(
                    "Renderer visibility is not game-rule visibility (no fog-of-war guarantee). "
                    + "In the editor the Scene view also counts as a camera for Renderer.isVisible.");
            }

            var candidates = Candidates(options.PlayerScope);
            var matching = new List<GameObject>();
            foreach (var candidate in candidates)
            {
                if (!Matches(candidate, options.Filter))
                {
                    continue;
                }

                if (options.PlayerScope && !VisibleToPlayer(candidate))
                {
                    continue;
                }

                matching.Add(candidate);
            }

            var kept = Math.Min(matching.Count, Math.Max(1, options.MaxEntities));
            result.Omitted = matching.Count - kept;
            for (var i = 0; i < kept; i++)
            {
                result.Objects.Add(matching[i]);
            }

            var providerOutputs = PlaySemantics.Describe(new PlayObservationContext(
                options.PlayerScope, frame, scene, gameTime, result.Objects));

            foreach (var entity in result.Objects)
            {
                result.Entities.Add(Describe(entity, options, providerOutputs));
            }

            foreach (var pair in providerOutputs)
            {
                foreach (var error in pair.Value.Errors)
                {
                    result.Warnings.Add("provider " + pair.Key + ": " + error);
                }
            }

            return result;
        }

        /// <summary>id 오름차순으로 정렬된 후보. player scope 는 비활성 오브젝트를 처음부터 뺀다.</summary>
        public List<GameObject> Candidates(bool playerScope)
        {
            var include = playerScope ? FindObjectsInactive.Exclude : FindObjectsInactive.Include;
            var found = new Dictionary<int, GameObject>();
            foreach (var item in Object.FindObjectsByType<Selectable>(include, FindObjectsSortMode.None))
            {
                Add(found, item);
            }

            foreach (var item in Object.FindObjectsByType<Collider>(include, FindObjectsSortMode.None))
            {
                Add(found, item);
            }

            foreach (var item in Object.FindObjectsByType<Collider2D>(include, FindObjectsSortMode.None))
            {
                Add(found, item);
            }

            foreach (var item in Object.FindObjectsByType<Text>(include, FindObjectsSortMode.None))
            {
                Add(found, item);
            }

            foreach (var item in Object.FindObjectsByType<TMP_Text>(include, FindObjectsSortMode.None))
            {
                Add(found, item);
            }

            foreach (var item in Object.FindObjectsByType<Graphic>(include, FindObjectsSortMode.None))
            {
                if (item.GetComponent<IPointerClickHandler>() != null || item.GetComponent<IDragHandler>() != null
                    || item.GetComponent<IPointerEnterHandler>() != null)
                {
                    Add(found, item);
                }
            }

            var list = new List<GameObject>(found.Values);
            list.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            return list;
        }

        private static void Add(Dictionary<int, GameObject> found, Component component)
        {
            if (component == null)
            {
                return;
            }

            var target = component.gameObject;
            if ((target.hideFlags & HideFlags.HideInHierarchy) != 0 || IsOwn(target))
            {
                return;
            }

            found[target.GetInstanceID()] = target;
        }

        /// <summary>SDK 자신이 만든 오버레이(가상 커서, 키보드 표시)는 관찰 대상이 아니다.</summary>
        internal static bool IsOwn(GameObject target)
        {
            return target.transform.root.name.StartsWith(OwnRootPrefix, StringComparison.Ordinal);
        }

        /// <summary>기본 filter 와 provider filter 를 모두 통과해야 player 에게 보인다. 더 제한적인 쪽을 따른다.</summary>
        public bool VisibleToPlayer(GameObject entity)
        {
            string basis;
            if (!PlayGeometry.IsRendered(entity, out basis))
            {
                // 렌더러가 없는 엔터티도 provider 가 엄격한 가시성을 선언하면 provider 판단을 따른다.
                if (!(PlaySemantics.HasVisibilityProvider() && entity.activeInHierarchy && entity.GetComponentInChildren<Renderer>() == null
                    && entity.GetComponent<RectTransform>() == null))
                {
                    return false;
                }
            }

            return PlaySemantics.VisibleToPlayer(entity);
        }

        public static bool Matches(GameObject entity, FilterDto filter)
        {
            if (filter == null)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(filter.Name)
                && entity.name.IndexOf(filter.Name, StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(filter.Selector)
                && PathOf(entity).IndexOf(filter.Selector, StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(filter.Component))
            {
                var has = false;
                foreach (var component in entity.GetComponents<Component>())
                {
                    if (component != null && component.GetType().FullName != null
                        && component.GetType().FullName.IndexOf(filter.Component, StringComparison.Ordinal) >= 0)
                    {
                        has = true;
                        break;
                    }
                }

                if (!has)
                {
                    return false;
                }
            }

            return true;
        }

        public static string PathOf(GameObject entity)
        {
            var builder = new StringBuilder(entity.name);
            var parent = entity.transform.parent;
            var guard = 0;
            while (parent != null && guard++ < 64)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            return builder.ToString();
        }

        private EntityDto Describe(
            GameObject entity, ScanOptions options,
            List<KeyValuePair<string, PlayProviderOutput>> providerOutputs)
        {
            var handle = registry.Track(entity);
            string basis;
            var visible = PlayGeometry.IsRendered(entity, out basis);
            var selectable = entity.GetComponent<Selectable>();

            var dto = new EntityDto
            {
                Ref = handle,
                Label = LabelOf(entity),
                Path = PathOf(entity),
                Active = entity.activeInHierarchy,
                Types = TypesOf(entity),
                State = new EntityStateDto
                {
                    Interactable = selectable == null ? (bool?)null : selectable.IsInteractable(),
                    Visible = visible,
                    VisibilityBasis = basis
                }
            };

            var transform = entity.transform;
            dto.Transform = new TransformDto
            {
                Position = Vec(transform.position),
                Scale = Vec(transform.lossyScale),
                EulerAngles = Vec(transform.eulerAngles),
                Layer = entity.layer
            };

            Bounds bounds;
            string boundsSource;
            if (PlayGeometry.TryWorldBounds(entity, out bounds, out boundsSource))
            {
                dto.Bounds = new BoundsDto { Source = boundsSource, Center = Vec(bounds.center), Size = Vec(bounds.size) };
            }

            Rect rect;
            if (PlayGeometry.TryScreenRect(entity, out rect))
            {
                dto.ScreenRect = new RectDto { X = rect.x, Y = rect.y, Width = rect.width, Height = rect.height };
            }

            if (options.IncludeFacts)
            {
                PlayFacts.AddRuntimeFacts(entity, handle, dto.Facts);
            }

            if (options.IncludeActions)
            {
                foreach (var descriptor in PlayActions.Describe(entity))
                {
                    dto.Actions.Add(new ActionSummaryDto
                    {
                        ActionRef = PlayActions.BuildRef(handle, descriptor.Kind),
                        Kind = descriptor.Kind,
                        Label = descriptor.Label,
                        Availability = descriptor.Availability,
                        Source = descriptor.Source
                    });
                }
            }

            AppendProviderOutput(entity, handle, dto, options, providerOutputs);
            if (dto.Facts.Count > options.FactsPerEntity)
            {
                dto.Facts.RemoveRange(options.FactsPerEntity, dto.Facts.Count - options.FactsPerEntity);
            }

            return dto;
        }

        private void AppendProviderOutput(
            GameObject entity, EntityRefDto handle, EntityDto dto, ScanOptions options,
            List<KeyValuePair<string, PlayProviderOutput>> providerOutputs)
        {
            foreach (var pair in providerOutputs)
            {
                foreach (var fact in pair.Value.Facts)
                {
                    if (fact.Entity != entity || !AllowedFor(fact.Visibility, options))
                    {
                        continue;
                    }

                    if (!options.IncludeFacts)
                    {
                        continue;
                    }

                    dto.Facts.Add(new FactDto
                    {
                        Name = pair.Key + "/" + fact.Name,
                        Value = fact.Value,
                        Unit = fact.Unit,
                        Status = fact.Status,
                        Source = "provider",
                        Reason = fact.Reason,
                        Evidence = new EvidenceDto { Entity = handle, ProviderId = pair.Key }
                    });
                }

                foreach (var relation in pair.Value.Relations)
                {
                    if (relation.Entity != entity || !AllowedFor(relation.Visibility, options))
                    {
                        continue;
                    }

                    if (options.PlayerScope && !VisibleToPlayer(relation.Target))
                    {
                        continue;
                    }

                    if (dto.Relations == null)
                    {
                        dto.Relations = new List<RelationDto>();
                    }

                    dto.Relations.Add(new RelationDto
                    {
                        Name = relation.Name,
                        Target = registry.Track(relation.Target),
                        ProviderId = pair.Key
                    });
                }

                if (!options.IncludeActions)
                {
                    continue;
                }

                foreach (var entry in pair.Value.Actions)
                {
                    if (entry.Entity != entity || !AllowedFor(entry.Action.Visibility, options))
                    {
                        continue;
                    }

                    dto.Actions.Add(new ActionSummaryDto
                    {
                        ActionRef = PlayActions.BuildProviderRef(handle, pair.Key, entry.Action.Id),
                        Kind = "provider",
                        Label = entry.Action.Label,
                        Availability = entry.Action.Available.HasValue
                            ? (entry.Action.Available.Value ? "available" : "unavailable")
                            : "unknown",
                        Source = "provider"
                    });
                }
            }
        }

        internal static bool AllowedFor(PlayVisibility visibility, ScanOptions options)
        {
            return !options.PlayerScope || visibility == PlayVisibility.Player;
        }

        private static string LabelOf(GameObject entity)
        {
            var text = entity.GetComponent<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                return text.text;
            }

            var tmp = entity.GetComponent<TMP_Text>();
            if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            {
                return tmp.text;
            }

            return entity.name;
        }

        private static List<string> TypesOf(GameObject entity)
        {
            var types = new List<string>();
            foreach (var component in entity.GetComponents<Component>())
            {
                if (component == null || component is Transform || component is CanvasRenderer)
                {
                    continue;
                }

                types.Add(component.GetType().Name);
                if (types.Count >= MaxTypes)
                {
                    break;
                }
            }

            return types;
        }

        private static Vec3Dto Vec(Vector3 value)
        {
            return new Vec3Dto { X = value.x, Y = value.y, Z = value.z };
        }
    }
}
