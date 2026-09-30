using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp.Play
{
    internal sealed class SpacePointDto
    {
        [JsonProperty("space")] public string Space { get; set; }
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("z")] public float Z { get; set; }
    }

    internal sealed class SpaceRegionDto
    {
        [JsonProperty("shape")] public string Shape { get; set; }
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("width")] public float Width { get; set; }
        [JsonProperty("height")] public float Height { get; set; }
        [JsonProperty("center")] public Vec3Dto Center { get; set; }
        [JsonProperty("radius")] public float Radius { get; set; }
        [JsonProperty("size")] public Vec3Dto Size { get; set; }
        [JsonProperty("dimension")] public string Dimension { get; set; }
    }

    internal sealed class SpacePlaneDto
    {
        [JsonProperty("point")] public Vec3Dto Point { get; set; }
        [JsonProperty("normal")] public Vec3Dto Normal { get; set; }
    }

    /// <summary>line_test 의 양 끝. 엔터티 handle 이나 world 점 중 하나다.</summary>
    internal sealed class SpaceEndpointDto
    {
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("generation")] public int Generation { get; set; }
        [JsonProperty("space")] public string Space { get; set; }
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("z")] public float Z { get; set; }

        public bool IsEntity { get { return string.IsNullOrEmpty(Space); } }
    }

    internal sealed class SpaceQueryDto
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("scope")] public string Scope { get; set; } = "player";
        [JsonProperty("point")] public SpacePointDto Point { get; set; }
        [JsonProperty("region")] public SpaceRegionDto Region { get; set; }
        [JsonProperty("camera")] public EntityRefDto Camera { get; set; }
        [JsonProperty("plane")] public SpacePlaneDto Plane { get; set; }
        [JsonProperty("colliderRef")] public EntityRefDto ColliderRef { get; set; }
        [JsonProperty("ref")] public EntityRefDto Ref { get; set; }
        [JsonProperty("from")] public SpaceEndpointDto From { get; set; }
        [JsonProperty("to")] public SpaceEndpointDto To { get; set; }
        [JsonProperty("dimension")] public string Dimension { get; set; }
        [JsonProperty("layerMask")] public int? LayerMask { get; set; }
        [JsonProperty("includeTriggers")] public bool IncludeTriggers { get; set; }
        [JsonProperty("limit")] public int Limit { get; set; } = 50;
    }

    internal sealed class HitDto
    {
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("entity")] public EntityRefDto Entity { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("distance", NullValueHandling = NullValueHandling.Ignore)] public float? Distance { get; set; }
        [JsonProperty("order")] public int Order { get; set; }
    }

    internal sealed class SpaceResultDto
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("stamp")] public StampDto Stamp { get; set; }
        [JsonProperty("units")] public string Units { get; set; }
        [JsonProperty("hits", NullValueHandling = NullValueHandling.Ignore)] public List<HitDto> Hits { get; set; }
        [JsonProperty("topHit", NullValueHandling = NullValueHandling.Ignore)] public HitDto TopHit { get; set; }
        [JsonProperty("blockers", NullValueHandling = NullValueHandling.Ignore)] public List<HitDto> Blockers { get; set; }
        [JsonProperty("entities", NullValueHandling = NullValueHandling.Ignore)] public List<EntityDto> Entities { get; set; }
        [JsonProperty("distances", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, float> Distances { get; set; }
        [JsonProperty("screen", NullValueHandling = NullValueHandling.Ignore)] public SpacePointDto Screen { get; set; }
        [JsonProperty("world", NullValueHandling = NullValueHandling.Ignore)] public SpacePointDto World { get; set; }
        [JsonProperty("onScreen", NullValueHandling = NullValueHandling.Ignore)] public bool? OnScreen { get; set; }
        [JsonProperty("behindCamera", NullValueHandling = NullValueHandling.Ignore)] public bool? BehindCamera { get; set; }
        [JsonProperty("camera", NullValueHandling = NullValueHandling.Ignore)] public string Camera { get; set; }
        [JsonProperty("orthographic", NullValueHandling = NullValueHandling.Ignore)] public bool? Orthographic { get; set; }
        [JsonProperty("blocked", NullValueHandling = NullValueHandling.Ignore)] public bool? Blocked { get; set; }
        [JsonProperty("dimension", NullValueHandling = NullValueHandling.Ignore)] public string Dimension { get; set; }
        [JsonProperty("ok", NullValueHandling = NullValueHandling.Ignore)] public bool? Ok { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note { get; set; }
    }

    /// <summary>질의 실패. 코드는 MCP server 가 그대로 agent 에게 전한다.</summary>
    internal sealed class SpaceQueryException : Exception
    {
        public SpaceQueryException(string code, string message)
            : base(code + ": " + message)
        {
        }
    }

    /// <summary>
    /// 범용 기하 질의. 2D 는 XY, 3D 는 XYZ 이고, 바닥 법선이나 "바닥 중심" 을 가정하지 않는다.
    /// </summary>
    /// <remarks>
    /// 화면 점은 좌상단 기준 Unity Screen 픽셀이다. 보행 가능성, 대상 유효성, 최적 배치는 알지 못한다.
    /// 물리 line test 결과는 physics occlusion 이고 게임 안의 공격 가능성이 아니다.
    /// </remarks>
    internal sealed class PlaySpaceQueries
    {
        private const string PhysicsNote = "Physics occlusion only; this is not a game rule about attacks or line of sight.";

        private readonly PlayEntityScanner scanner;
        private readonly EntityRegistry registry;

        public PlaySpaceQueries(PlayEntityScanner scanner, EntityRegistry registry)
        {
            this.scanner = scanner;
            this.registry = registry;
        }

        public SpaceResultDto Run(SpaceQueryDto query, StampDto stamp)
        {
            SpaceResultDto result;
            switch (query.Kind)
            {
                case "hit_test":
                    result = HitTest(query);
                    break;
                case "entities_in_region":
                    result = EntitiesInRegion(query);
                    break;
                case "project_world_to_screen":
                    result = WorldToScreen(query);
                    break;
                case "project_screen_to_world":
                    result = ScreenToWorld(query);
                    break;
                case "line_test":
                    result = LineTest(query);
                    break;
                case "entity_screen_point":
                    result = EntityScreenPoint(query);
                    break;
                case "entity_input_check":
                    result = EntityInputCheck(query);
                    break;
                default:
                    throw new SpaceQueryException("invalid_request", "unknown query kind " + query.Kind);
            }

            result.Kind = query.Kind;
            result.Stamp = stamp;
            return result;
        }

        // ---- hit test ---------------------------------------------------------------------------------------

        private SpaceResultDto HitTest(SpaceQueryDto query)
        {
            if (query.Point == null || query.Point.Space != "screen")
            {
                throw new SpaceQueryException("invalid_request", "hit_test needs a screen point");
            }

            var hits = HitsAt(new Vector2(query.Point.X, Screen.height - query.Point.Y), query.Scope == "player");
            var result = new SpaceResultDto { Hits = hits, Units = "screen pixels, top-left origin" };
            if (hits.Count > 0)
            {
                result.TopHit = hits[0];
                result.Blockers = new List<HitDto>();
            }

            result.Note = "hits are ordered by input priority: UI first, then physics by distance. "
                + "The first hit is what would receive a click, subject to the game's own handlers.";
            return result;
        }

        /// <summary>화면 점(좌하단 기준)에 닿는 대상들. UI 는 raycaster 순서, 그 뒤 물리를 거리순으로 둔다.</summary>
        private List<HitDto> HitsAt(Vector2 bottomLeft, bool playerScope)
        {
            var hits = new List<HitDto>();
            var order = 0;

            if (EventSystem.current != null)
            {
                var data = new PointerEventData(EventSystem.current) { position = bottomLeft };
                var uiHits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(data, uiHits);
                foreach (var hit in uiHits)
                {
                    if (hit.gameObject == null || PlayEntityScanner.IsOwn(hit.gameObject))
                    {
                        continue;
                    }

                    if (playerScope && !scanner.VisibleToPlayer(hit.gameObject))
                    {
                        continue;
                    }

                    hits.Add(Hit("ui", hit.gameObject, hit.distance, order++));
                }
            }

            var camera = PlayGeometry.WorldCamera();
            if (camera != null)
            {
                var ray = camera.ScreenPointToRay(bottomLeft);
                var physics = new List<KeyValuePair<HitDto, GameObject>>();
                foreach (var hit in Physics.RaycastAll(ray, camera.farClipPlane, camera.eventMask))
                {
                    physics.Add(new KeyValuePair<HitDto, GameObject>(
                        Hit("physics3d", hit.collider.gameObject, hit.distance, 0), hit.collider.gameObject));
                }

                foreach (var hit in Physics2D.GetRayIntersectionAll(ray, camera.farClipPlane, camera.eventMask))
                {
                    physics.Add(new KeyValuePair<HitDto, GameObject>(
                        Hit("physics2d", hit.collider.gameObject, hit.distance, 0), hit.collider.gameObject));
                }

                physics.Sort((a, b) => (a.Key.Distance ?? 0f).CompareTo(b.Key.Distance ?? 0f));
                foreach (var pair in physics)
                {
                    if (PlayEntityScanner.IsOwn(pair.Value) || (playerScope && !scanner.VisibleToPlayer(pair.Value)))
                    {
                        continue;
                    }

                    pair.Key.Order = order++;
                    hits.Add(pair.Key);
                }
            }

            return hits;
        }

        private HitDto Hit(string layer, GameObject target, float distance, int order)
        {
            return new HitDto
            {
                Layer = layer,
                Entity = registry.Track(target),
                Label = target.name,
                Distance = distance,
                Order = order
            };
        }

        // ---- region -----------------------------------------------------------------------------------------

        private SpaceResultDto EntitiesInRegion(SpaceQueryDto query)
        {
            if (query.Region == null)
            {
                throw new SpaceQueryException("invalid_request", "entities_in_region needs a region");
            }

            var options = new ScanOptions
            {
                PlayerScope = query.Scope == "player",
                MaxEntities = 1000,
                FactsPerEntity = 5,
                IncludeActions = false,
                IncludeFacts = false
            };
            var scan = scanner.Scan(options, Time.frameCount, string.Empty, Time.timeAsDouble);
            var region = query.Region;
            var found = new List<EntityDto>();
            var distances = new Dictionary<string, float>();
            var limit = Mathf.Clamp(query.Limit, 1, 200);

            for (var i = 0; i < scan.Objects.Count && found.Count < limit; i++)
            {
                var entity = scan.Entities[i];
                float distance;
                if (!InRegion(scan.Objects[i], entity, region, out distance))
                {
                    continue;
                }

                found.Add(entity);
                distances[EntityRegistry.Key(entity.Ref)] = distance;
            }

            var result = new SpaceResultDto { Entities = found, Distances = distances };
            result.Units = region.Shape == "screen_rect"
                ? "distance in screen pixels from the region center; screen is top-left origin Unity Screen pixels"
                : "distance in world units from the shape center to the entity bounds center; "
                    + (region.Dimension == "2d" ? "2D uses XY only" : "3D uses XYZ");
            result.Dimension = region.Dimension;
            return result;
        }

        private static bool InRegion(GameObject entity, EntityDto dto, SpaceRegionDto region, out float distance)
        {
            distance = 0f;
            if (region.Shape == "screen_rect")
            {
                if (dto.ScreenRect == null)
                {
                    return false;
                }

                var rect = new Rect(region.X, region.Y, region.Width, region.Height);
                var entityRect = new Rect(dto.ScreenRect.X, dto.ScreenRect.Y, dto.ScreenRect.Width, dto.ScreenRect.Height);
                distance = Vector2.Distance(rect.center, entityRect.center);
                return rect.Overlaps(entityRect);
            }

            Bounds bounds;
            if (!PlayGeometry.TryWorldBounds(entity, out bounds) || region.Center == null)
            {
                return false;
            }

            var flat = region.Dimension == "2d";
            var center = new Vector3(region.Center.X, region.Center.Y, flat ? 0f : region.Center.Z);
            var point = flat ? new Vector3(bounds.center.x, bounds.center.y, 0f) : bounds.center;
            distance = Vector3.Distance(center, point);

            if (region.Shape == "world_sphere")
            {
                var reach = flat
                    ? new Bounds(new Vector3(bounds.center.x, bounds.center.y, 0f), new Vector3(bounds.size.x, bounds.size.y, 0f))
                    : bounds;
                return (reach.ClosestPoint(center) - center).magnitude <= region.Radius;
            }

            if (region.Shape == "world_box" && region.Size != null)
            {
                var box = new Bounds(center, new Vector3(region.Size.X, region.Size.Y, flat ? 0f : region.Size.Z));
                var subject = flat
                    ? new Bounds(new Vector3(bounds.center.x, bounds.center.y, 0f), new Vector3(bounds.size.x, bounds.size.y, 0f))
                    : bounds;
                return box.Intersects(subject);
            }

            return false;
        }

        // ---- projection -------------------------------------------------------------------------------------

        private Camera ChooseCamera(EntityRefDto explicitCamera)
        {
            if (explicitCamera != null)
            {
                GameObject cameraObject;
                var lifecycle = registry.Resolve(explicitCamera, out cameraObject);
                if (lifecycle != EntityLifecycle.Present)
                {
                    throw new SpaceQueryException("stale_ref", "the camera handle no longer points at a live camera");
                }

                var chosen = cameraObject.GetComponent<Camera>();
                if (chosen == null)
                {
                    throw new SpaceQueryException("invalid_request", "the referenced entity is not a camera");
                }

                return chosen;
            }

            string problem;
            var only = PlayGeometry.OnlyCamera(out problem);
            if (only != null)
            {
                return only;
            }

            if (problem == "ambiguous_camera")
            {
                throw new SpaceQueryException(
                    "ambiguous_camera", "several cameras are active; pass camera with the entity ref of the one to use");
            }

            throw new SpaceQueryException("invalid_request", "no active camera");
        }

        private SpaceResultDto WorldToScreen(SpaceQueryDto query)
        {
            if (query.Point == null || (query.Point.Space != "world2d" && query.Point.Space != "world3d"))
            {
                throw new SpaceQueryException("invalid_request", "project_world_to_screen needs a world2d or world3d point");
            }

            var camera = ChooseCamera(query.Camera);
            var world = new Vector3(query.Point.X, query.Point.Y, query.Point.Space == "world2d" ? 0f : query.Point.Z);
            var screen = camera.WorldToScreenPoint(world);
            return new SpaceResultDto
            {
                Screen = new SpacePointDto { Space = "screen", X = screen.x, Y = Screen.height - screen.y },
                OnScreen = screen.z >= 0f && screen.x >= 0f && screen.x <= Screen.width
                    && screen.y >= 0f && screen.y <= Screen.height,
                BehindCamera = screen.z < 0f,
                Camera = camera.name,
                Orthographic = camera.orthographic,
                Units = "world units in; screen pixels out, top-left origin",
                Dimension = query.Point.Space == "world2d" ? "2d" : "3d"
            };
        }

        private SpaceResultDto ScreenToWorld(SpaceQueryDto query)
        {
            if (query.Point == null || query.Point.Space != "screen")
            {
                throw new SpaceQueryException("invalid_request", "project_screen_to_world needs a screen point");
            }

            var camera = ChooseCamera(query.Camera);
            var ray = camera.ScreenPointToRay(new Vector2(query.Point.X, Screen.height - query.Point.Y));

            Vector3? hit = null;
            string basis = null;
            if (query.Plane != null && query.Plane.Point != null && query.Plane.Normal != null)
            {
                var plane = new Plane(
                    new Vector3(query.Plane.Normal.X, query.Plane.Normal.Y, query.Plane.Normal.Z),
                    new Vector3(query.Plane.Point.X, query.Plane.Point.Y, query.Plane.Point.Z));
                float enter;
                if (plane.Raycast(ray, out enter))
                {
                    hit = ray.GetPoint(enter);
                }

                basis = "explicit plane";
            }
            else if (query.ColliderRef != null)
            {
                GameObject target;
                if (registry.Resolve(query.ColliderRef, out target) != EntityLifecycle.Present)
                {
                    throw new SpaceQueryException("stale_ref", "the collider handle no longer points at a live object");
                }

                var collider = target.GetComponent<Collider>();
                RaycastHit info;
                if (collider != null && collider.Raycast(ray, out info, camera.farClipPlane))
                {
                    hit = info.point;
                }

                basis = "explicit collider";
            }

            if (basis == null)
            {
                throw new SpaceQueryException("invalid_request", "give an explicit plane or colliderRef; no ground is assumed");
            }

            var result = new SpaceResultDto
            {
                Camera = camera.name,
                Orthographic = camera.orthographic,
                Units = "screen pixels in (top-left origin); world units out, 3D XYZ",
                Note = "projected onto the " + basis + "; this is geometry, not a statement about walkable or valid positions"
            };
            if (hit.HasValue)
            {
                result.World = new SpacePointDto { Space = "world3d", X = hit.Value.x, Y = hit.Value.y, Z = hit.Value.z };
                result.Ok = true;
            }
            else
            {
                result.Ok = false;
                result.Reason = "the camera ray does not meet the " + basis;
            }

            return result;
        }

        // ---- line test --------------------------------------------------------------------------------------

        private SpaceResultDto LineTest(SpaceQueryDto query)
        {
            if (query.From == null || query.To == null || (query.Dimension != "2d" && query.Dimension != "3d"))
            {
                throw new SpaceQueryException("invalid_request", "line_test needs from, to and dimension 2d or 3d");
            }

            GameObject fromObject, toObject;
            var from = Endpoint(query.From, query.Dimension, out fromObject);
            var to = Endpoint(query.To, query.Dimension, out toObject);
            var mask = query.LayerMask ?? Physics.DefaultRaycastLayers;

            var blockers = new List<HitDto>();
            var direction = to - from;
            var length = direction.magnitude;
            var flat = query.Dimension == "2d";

            if (length > 0f)
            {
                if (flat)
                {
                    var previous = Physics2D.queriesHitTriggers;
                    Physics2D.queriesHitTriggers = query.IncludeTriggers;
                    try
                    {
                        var all = Physics2D.RaycastAll(from, direction.normalized, length, mask);
                        AddBlockers(blockers, all.Length, i => all[i].collider, fromObject, toObject, i => all[i].distance);
                    }
                    finally
                    {
                        Physics2D.queriesHitTriggers = previous;
                    }
                }
                else
                {
                    var all = Physics.RaycastAll(
                        from, direction.normalized, length, mask,
                        query.IncludeTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
                    AddBlockers(blockers, all.Length, i => all[i].collider, fromObject, toObject, i => all[i].distance);
                }
            }

            blockers.Sort((a, b) => (a.Distance ?? 0f).CompareTo(b.Distance ?? 0f));
            return new SpaceResultDto
            {
                Blocked = blockers.Count > 0,
                Blockers = blockers,
                Dimension = query.Dimension,
                Units = "world units; " + (flat ? "2D uses XY only" : "3D uses XYZ")
                    + "; triggers " + (query.IncludeTriggers ? "included" : "ignored"),
                Note = PhysicsNote
            };
        }

        private void AddBlockers(
            List<HitDto> blockers, int count, Func<int, Component> collider, GameObject a, GameObject b, Func<int, float> distance)
        {
            for (var i = 0; i < count; i++)
            {
                var component = collider(i);
                if (component == null)
                {
                    continue;
                }

                var target = component.gameObject;
                // 양 끝 엔터티 자신의 collider 는 가로막는 것이 아니다.
                if ((a != null && target.transform.IsChildOf(a.transform)) || (b != null && target.transform.IsChildOf(b.transform)))
                {
                    continue;
                }

                blockers.Add(Hit(target.GetComponent<Collider2D>() != null ? "physics2d" : "physics3d", target, distance(i), 0));
            }
        }

        private Vector3 Endpoint(SpaceEndpointDto endpoint, string dimension, out GameObject entity)
        {
            entity = null;
            if (!endpoint.IsEntity)
            {
                var expected = dimension == "2d" ? "world2d" : "world3d";
                if (endpoint.Space != expected)
                {
                    throw new SpaceQueryException("invalid_request", "endpoint space " + endpoint.Space + " does not match dimension " + dimension);
                }

                return new Vector3(endpoint.X, endpoint.Y, dimension == "2d" ? 0f : endpoint.Z);
            }

            var handle = new EntityRefDto { SessionId = endpoint.SessionId, Id = endpoint.Id, Generation = endpoint.Generation };
            if (registry.Resolve(handle, out entity) != EntityLifecycle.Present)
            {
                throw new SpaceQueryException("stale_ref", "an endpoint handle no longer points at a live object");
            }

            Bounds bounds;
            var center = PlayGeometry.TryWorldBounds(entity, out bounds) ? bounds.center : entity.transform.position;
            return dimension == "2d" ? new Vector3(center.x, center.y, 0f) : center;
        }

        // ---- entity input -----------------------------------------------------------------------------------

        private SpaceResultDto EntityScreenPoint(SpaceQueryDto query)
        {
            GameObject entity;
            var lifecycle = registry.Resolve(query.Ref, out entity);
            if (lifecycle == EntityLifecycle.WrongSession)
            {
                throw new SpaceQueryException("stale_ref", "the handle belongs to another Play session");
            }

            if (lifecycle != EntityLifecycle.Present)
            {
                throw new SpaceQueryException("stale_ref", "the target is " + lifecycle.ToString().ToLowerInvariant());
            }

            float x, y;
            if (!PlayGeometry.TryScreenCenter(entity, out x, out y))
            {
                throw new SpaceQueryException("blocked", "the target has no on-screen point");
            }

            return new SpaceResultDto
            {
                Screen = new SpacePointDto { Space = "screen", X = x, Y = y },
                Units = "screen pixels, top-left origin"
            };
        }

        /// <summary>대상이 지금 입력을 받을 수 있는지: 살아 있음, active, interactable, 그 점에서 맨 위에 있음.</summary>
        private SpaceResultDto EntityInputCheck(SpaceQueryDto query)
        {
            GameObject entity;
            var lifecycle = registry.Resolve(query.Ref, out entity);
            if (lifecycle == EntityLifecycle.WrongSession || lifecycle == EntityLifecycle.Unknown)
            {
                throw new SpaceQueryException("stale_ref", "the handle is not from this Play session's observations");
            }

            if (lifecycle == EntityLifecycle.Destroyed)
            {
                throw new SpaceQueryException("stale_ref", "the target was destroyed, or its id now belongs to a different object");
            }

            if (lifecycle == EntityLifecycle.Inactive)
            {
                return Refuse("the target is not active in the scene");
            }

            var selectable = entity.GetComponent<UnityEngine.UI.Selectable>();
            if (selectable != null && !selectable.IsInteractable())
            {
                return Refuse("the target is not interactable");
            }

            if (query.Point == null)
            {
                return new SpaceResultDto { Ok = true, Note = "no point given, so blocking was not checked" };
            }

            var hits = HitsAt(new Vector2(query.Point.X, Screen.height - query.Point.Y), false);
            if (hits.Count == 0)
            {
                return Refuse("nothing would receive input at that point");
            }

            GameObject top;
            registry.Resolve(hits[0].Entity, out top);
            if (top != null && (top == entity || top.transform.IsChildOf(entity.transform)))
            {
                return new SpaceResultDto { Ok = true, TopHit = hits[0] };
            }

            var result = Refuse("blocked by " + (top == null ? "another object" : PlayEntityScanner.PathOf(top)));
            result.TopHit = hits[0];
            return result;
        }

        private static SpaceResultDto Refuse(string reason)
        {
            return new SpaceResultDto { Ok = false, Reason = reason };
        }
    }
}
