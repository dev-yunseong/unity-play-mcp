using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlayMcp.Affordances.Scan;
using UnityPlayMcp.Capture;
using UnityPlayMcp.Protocol.Dto;
using Object = UnityEngine.Object;

namespace UnityPlayMcp.Play
{
    /// <summary>
    /// observe/act 도구 묶음이 쓰는 wire method 들. MCP server 가 조합해서 <c>observe</c>, <c>act_and_observe</c> 등을 만든다.
    /// </summary>
    /// <remarks>
    /// 이 클래스는 게임 상태를 바꾸지 않는다. 입력은 기존 <c>ActionExecutor</c> 경로로만 나간다. session 하나에 하나씩 만들고,
    /// assembly reload 뒤에는 새 session id 로 다시 만든다.
    /// </remarks>
    internal sealed class PlayApi
    {
        public const int ProtocolVersion = 1;

        private const int MaxWaitFrames = 600;

        private static readonly HashSet<string> WireMethods = new HashSet<string>
        {
            "play_capabilities", "play_observe", "play_sample", "play_inspect", "play_query_space",
            "play_events", "play_begin", "play_end", "play_checkpoint", "wait_frames"
        };

        private static readonly HashSet<string> MutatingMethods = new HashSet<string>
        {
            "enter_text", "move_mouse", "mouse_down", "mouse_up", "key_click", "key_down", "key_up",
            "set_axis", "set_button", "pause_time", "resume_time", "reset_game"
        };

        private readonly IScreenCapturer capturer;
        private readonly EntityRegistry registry;
        private readonly PlayEntityScanner scanner;
        private readonly PlaySpaceQueries space;
        private readonly OperationTable operations = new OperationTable();
        private long inputRevision;

        public PlayApi(IScreenCapturer capturer)
        {
            this.capturer = capturer;
            SessionId = Guid.NewGuid().ToString("N");
            registry = new EntityRegistry(SessionId);
            scanner = new PlayEntityScanner(registry);
            space = new PlaySpaceQueries(scanner, registry);
        }

        public string SessionId { get; }

        public static bool Handles(string method)
        {
            return WireMethods.Contains(method);
        }

        public static bool IsMutating(string method)
        {
            return MutatingMethods.Contains(method);
        }

        /// <summary>입력이 실행됐음을 기록한다. 관찰 뒤 다른 입력이 있었는지 알아내는 데 쓴다.</summary>
        public void NoteInput()
        {
            inputRevision++;
        }

        /// <summary>이 client 가 지금 입력을 보내도 되는지. 다른 client 의 operation 이 점유 중이면 false 다.</summary>
        public bool TryCheckInput(string clientId, out string busyOperationId)
        {
            return operations.TryCheckInput(clientId, Now(), out busyOperationId);
        }

        public static string BusyMessage(string operationId)
        {
            return "busy:" + operationId + " another operation owns the input; nothing was sent";
        }

        private static double Now()
        {
            return Time.realtimeSinceStartupAsDouble;
        }

        public IEnumerator Execute(
            int actionId, string method, List<object> parameters, string clientId, Action<ActionResultDto> completed)
        {
            switch (method)
            {
                case "wait_frames":
                    yield return WaitFrames(actionId, parameters, completed);
                    yield break;

                case "play_observe":
                    yield return Observe(actionId, parameters, completed);
                    yield break;
            }

            completed(ExecuteImmediate(actionId, method, parameters, clientId));
        }

        private ActionResultDto ExecuteImmediate(int actionId, string method, List<object> parameters, string clientId)
        {
            try
            {
                switch (method)
                {
                    case "play_capabilities":
                        return ActionResultDto.Success(actionId, Capabilities());
                    case "play_sample":
                        return WithRequest<SampleRequestDto>(actionId, method, parameters, request => Sample(request));
                    case "play_inspect":
                        return WithRequest<InspectRequestDto>(actionId, method, parameters, request => Inspect(request));
                    case "play_query_space":
                        return WithRequest<SpaceQueryDto>(actionId, method, parameters, request => space.Run(request, Stamp()));
                    case "play_events":
                        return ReadEvents(actionId, parameters);
                    case "play_begin":
                        return Begin(actionId, parameters, clientId);
                    case "play_end":
                        return End(actionId, parameters, clientId);
                    case "play_checkpoint":
                        return Checkpoint(actionId, parameters);
                }
            }
            catch (SpaceQueryException exception)
            {
                return ActionResultDto.Failure(actionId, exception.Message);
            }
            catch (StaleReferenceException exception)
            {
                return ActionResultDto.Failure(actionId, "stale_ref: " + exception.Message);
            }
            catch (Exception exception)
            {
                return ActionResultDto.Failure(actionId, method + " failed: " + exception.GetType().Name + ": " + exception.Message);
            }

            return ActionResultDto.Failure(actionId, "Unsupported method: " + method);
        }

        private static ActionResultDto WithRequest<T>(
            int actionId, string method, List<object> parameters, Func<T, object> handle)
            where T : class
        {
            T request;
            string error;
            if (!TryRead(parameters, 0, out request, out error))
            {
                return ActionResultDto.Failure(actionId, "invalid_request: " + method + " " + error);
            }

            return ActionResultDto.Success(actionId, handle(request));
        }

        /// <summary>params 의 한 자리를 DTO 로 읽는다. wire 에서는 JObject, 테스트에서는 사전이나 DTO 가 온다.</summary>
        private static bool TryRead<T>(List<object> parameters, int index, out T value, out string error)
            where T : class
        {
            value = null;
            error = null;
            if (parameters == null || parameters.Count <= index || parameters[index] == null)
            {
                error = "needs an object at params[" + index + "]";
                return false;
            }

            try
            {
                var raw = parameters[index];
                var token = raw as JToken ?? JToken.FromObject(raw);
                value = token.ToObject<T>();
                if (value == null)
                {
                    error = "params[" + index + "] is empty";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "params[" + index + "] is invalid: " + exception.Message;
                return false;
            }
        }

        // ---- stamp ------------------------------------------------------------------------------------------

        private StampDto Stamp()
        {
            var scene = SceneManager.GetActiveScene();
            return new StampDto
            {
                SessionId = SessionId,
                Run = AffordanceBootstrap.CurrentRun,
                Scene = scene.IsValid() ? scene.name : string.Empty,
                Frame = Time.frameCount,
                SampledAtMonotonicMs = Time.realtimeSinceStartupAsDouble * 1000d,
                GameTimeSeconds = Time.timeAsDouble
            };
        }

        // ---- capabilities -----------------------------------------------------------------------------------

        private CapabilitiesDto Capabilities()
        {
            return new CapabilitiesDto
            {
                ProtocolVersion = ProtocolVersion,
                RuntimeVersion = PackageVersion.Value,
                SessionId = SessionId,
                Tools = new List<string>
                {
                    "get_play_capabilities", "observe", "inspect_action", "query_space", "act_and_observe", "watch_events"
                },
                InputPaths = new List<string> { "mouse", "keyboard", "axis", "button", "text" },
                InputSystem = "virtual input shim over the legacy Input API; the new Input System package is not covered",
                Physics2D = true,
                Physics3D = true,
                Ui = true,
                Providers = PlaySemantics.Statuses(),
                ObservationPolicies = new List<string> { "player", "debug" },
                Limits = new Dictionary<string, int>
                {
                    { "maxEntities", 200 }, { "maxFactsPerEntity", 100 }, { "maxWaitFrames", MaxWaitFrames },
                    { "providerSlowBudgetMs", (int)PlaySemantics.SlowBudgetMilliseconds },
                    { "providerEventCapacity", PlaySemantics.EventCapacity }
                }
            };
        }

        // ---- wait / operations ------------------------------------------------------------------------------

        private static IEnumerator WaitFrames(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            int frames;
            if (parameters == null || parameters.Count < 1 || !int.TryParse(
                    Convert.ToString(parameters[0], System.Globalization.CultureInfo.InvariantCulture), out frames)
                || frames < 1 || frames > MaxWaitFrames)
            {
                completed(ActionResultDto.Failure(actionId, "invalid_request: wait_frames takes [frames] from 1 to " + MaxWaitFrames));
                yield break;
            }

            // 렌더 프레임을 센다. Time.timeScale 과 무관하므로 게임이 멈춰 있어도 진행한다.
            for (var i = 0; i < frames; i++)
            {
                yield return null;
            }

            completed(ActionResultDto.Success(actionId));
        }

        private ActionResultDto Begin(int actionId, List<object> parameters, string clientId)
        {
            var ttlMs = 30000d;
            if (parameters == null || parameters.Count < 2 || parameters[0] == null || parameters[1] == null)
            {
                return ActionResultDto.Failure(actionId, "invalid_request: play_begin takes [operationId, hash, ttlMs]");
            }

            var operationId = Convert.ToString(parameters[0]);
            var hash = Convert.ToString(parameters[1]);

            if (parameters.Count > 2)
            {
                double parsed;
                if (double.TryParse(
                        Convert.ToString(parameters[2], System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    ttlMs = parsed;
                }
            }

            var outcome = operations.Begin(operationId, hash, clientId, Now(), Math.Max(1d, ttlMs / 1000d));
            if (outcome.Status == BeginStatus.Busy)
            {
                return ActionResultDto.Failure(actionId, BusyMessage(outcome.HolderOperationId));
            }

            var stamp = Stamp();
            return ActionResultDto.Success(actionId, new BeginResultDto
            {
                Status = outcome.Status == BeginStatus.AlreadyStarted ? "already_started" : "acquired",
                State = outcome.State,
                SessionId = SessionId,
                Scene = stamp.Scene,
                InputRevision = inputRevision
            });
        }

        private ActionResultDto End(int actionId, List<object> parameters, string clientId)
        {
            var operationId = parameters != null && parameters.Count > 0 ? Convert.ToString(parameters[0]) : null;
            if (operationId == null)
            {
                return ActionResultDto.Failure(actionId, "invalid_request: play_end takes [operationId]");
            }

            operations.End(operationId, clientId);
            return ActionResultDto.Success(actionId);
        }

        private ActionResultDto Checkpoint(int actionId, List<object> parameters)
        {
            var expected = parameters != null && parameters.Count > 0 ? Convert.ToString(parameters[0]) : null;
            var stamp = Stamp();
            return ActionResultDto.Success(actionId, new CheckpointDto
            {
                SceneChanged = expected != null && expected != stamp.Scene,
                Scene = stamp.Scene,
                Frame = stamp.Frame
            });
        }

        private ActionResultDto ReadEvents(int actionId, List<object> parameters)
        {
            long after = 0;
            var limit = 100;
            if (parameters != null && parameters.Count > 0)
            {
                long.TryParse(
                    Convert.ToString(parameters[0], System.Globalization.CultureInfo.InvariantCulture), out after);
            }

            if (parameters != null && parameters.Count > 1)
            {
                int.TryParse(
                    Convert.ToString(parameters[1], System.Globalization.CultureInfo.InvariantCulture), out limit);
            }

            long next, dropped;
            var read = PlaySemantics.ReadEvents(after, Mathf.Clamp(limit, 1, 500), out next, out dropped);
            var result = new EventsResultDto { Next = next, Dropped = dropped };
            foreach (var item in read)
            {
                result.Events.Add(new ProviderEventDto
                {
                    Sequence = item.Sequence,
                    Kind = item.Kind,
                    ProviderId = item.ProviderId,
                    Name = item.Name,
                    Entity = item.Entity == null ? null : registry.Track(item.Entity),
                    Data = item.Data,
                    PlayerVisible = item.PlayerVisible,
                    Stamp = new EventStampDto { Frame = item.Frame, Scene = item.Scene, GameTimeSeconds = item.GameTimeSeconds }
                });
            }

            return ActionResultDto.Success(actionId, result);
        }

        // ---- observe ----------------------------------------------------------------------------------------

        private IEnumerator Observe(int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            ObserveRequestDto request;
            string error;
            if (!TryRead(parameters, 0, out request, out error))
            {
                completed(ActionResultDto.Failure(actionId, "invalid_request: play_observe " + error));
                yield break;
            }

            var timer = Stopwatch.StartNew();
            ObservationDto observation = null;
            var image = default(CapturedImage);
            var captureReturned = false;
            string sampleError = null;
            string imageProblem = null;
            var captureMs = 0d;

            if (request.IncludeImage && capturer != null)
            {
                var captureRequest = new CaptureRequest
                {
                    TargetId = null,
                    MaxEdge = request.ImageMaxEdge > 0 ? request.ImageMaxEdge : CaptureRequestReader.FullScreenMaxEdge,
                    Padding = 0f
                };
                var captureTimer = Stopwatch.StartNew();
                // 구조화 상태는 이미지를 읽은 같은 end-of-frame 안에서, 콜백에서 바로 수집한다. 그 사이 프레임이 넘어가지 않는다.
                yield return capturer.Capture(captureRequest, null, captured =>
                {
                    image = captured;
                    captureReturned = true;
                    observation = TrySample(request, out sampleError);
                });
                captureMs = captureTimer.Elapsed.TotalMilliseconds;
                if (!captureReturned || !image.IsSuccess)
                {
                    imageProblem = captureReturned ? image.Error : "the capture did not complete";
                }
            }
            else if (request.IncludeImage)
            {
                imageProblem = "this build cannot capture the screen";
            }

            if (observation == null && sampleError == null)
            {
                observation = TrySample(request, out sampleError);
            }

            if (observation == null)
            {
                // iterator 는 yield 를 감싼 try 를 둘 수 없어 여기서 실패로 돌려준다. 던지면 host 의 action queue 가 멈춘다.
                completed(ActionResultDto.Failure(actionId, "play_observe failed: " + sampleError));
                yield break;
            }

            observation.EncodeMs = captureMs;
            AttachImage(observation, image, imageProblem, request);
            observation.InputRevision = inputRevision;
            completed(ActionResultDto.Success(actionId, observation));
        }

        private void AttachImage(
            ObservationDto observation, CapturedImage image, string imageProblem, ObserveRequestDto request)
        {
            if (!request.IncludeImage)
            {
                return;
            }

            if (imageProblem != null)
            {
                observation.Warnings.Add("image unavailable: " + imageProblem);
                return;
            }

            var imageStamp = new StampDto
            {
                SessionId = SessionId,
                Scene = image.Scene ?? string.Empty,
                Frame = image.Frame,
                SampledAtMonotonicMs = observation.Stamp.SampledAtMonotonicMs,
                GameTimeSeconds = observation.Stamp.GameTimeSeconds
            };

            // 다른 scene 의 이미지와 상태를 한 관찰로 섞지 않는다.
            if (imageStamp.Scene != observation.Stamp.Scene)
            {
                observation.Coherent = false;
                observation.ImageStamp = imageStamp;
                observation.FrameDelta = imageStamp.Frame - observation.Stamp.Frame;
                observation.Warnings.Add(
                    "The scene changed while the image was taken; the image was dropped and is not combined with this state.");
                return;
            }

            if (imageStamp.Frame != observation.Stamp.Frame)
            {
                observation.Coherent = false;
                observation.ImageStamp = imageStamp;
                observation.FrameDelta = imageStamp.Frame - observation.Stamp.Frame;
            }

            var region = CaptureRect.TopLeft(image.Source, image.ScreenHeight);
            var scale = CaptureRect.Scale(image.Source, image.Width, image.Height);
            observation.Image = new ImageDto
            {
                MimeType = "image/jpeg", // 전체 화면 캡처는 JPEG 다(CaptureRequest.UsePng 가 false).
                Data = Convert.ToBase64String(image.Bytes),
                Width = image.Width,
                Height = image.Height,
                Screen = new ImageScreenDto { Width = image.ScreenWidth, Height = image.ScreenHeight },
                Region = new RectDto { X = region.X, Y = region.Y, Width = region.Width, Height = region.Height },
                Scale = new ImageScaleDto { X = scale.X, Y = scale.Y },
                Frame = image.Frame,
                Scene = image.Scene,
                ToScreen = "screenX = region.x + imageX / scale.x; screenY = region.y + imageY / scale.y (top-left origin)"
            };
        }

        private ObservationDto TrySample(ObserveRequestDto request, out string error)
        {
            error = null;
            try
            {
                return SampleObservation(request);
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
        }

        /// <summary>지금 이 프레임의 상태를 한 번 수집한다. 매 프레임 수집하지 않는다.</summary>
        private ObservationDto SampleObservation(ObserveRequestDto request)
        {
            var stamp = Stamp();
            var playerScope = request.Scope != "debug";
            var options = new ScanOptions
            {
                PlayerScope = playerScope,
                Filter = request.Filter,
                MaxEntities = Mathf.Clamp(request.MaxEntities, 1, 200),
                FactsPerEntity = Mathf.Clamp(request.FactsPerEntity, 1, 100),
                IncludeActions = request.IncludeActions,
                IncludeFacts = request.IncludeFacts
            };

            var scan = scanner.Scan(options, stamp.Frame, stamp.Scene, stamp.GameTimeSeconds);
            var observation = new ObservationDto
            {
                Stamp = stamp,
                Entities = scan.Entities,
                OmittedEntities = scan.Omitted,
                Policy = new PolicyDto
                {
                    Scope = playerScope ? "player" : "debug",
                    VisibilityBasis = scan.VisibilityBasis,
                    VisibilityGuarantee = scan.VisibilityGuarantee
                },
                InputRevision = inputRevision
            };
            observation.Warnings.AddRange(scan.Warnings);

            var present = new HashSet<int>();
            foreach (var entity in scan.Objects)
            {
                present.Add(entity.GetInstanceID());
            }

            foreach (var tracked in request.Track ?? new List<EntityRefDto>())
            {
                if (tracked == null || present.Contains(tracked.Id))
                {
                    continue;
                }

                observation.Lifecycles[EntityRegistry.Key(tracked)] = Classify(tracked, request.Filter);
            }

            return observation;
        }

        /// <summary>사라진 엔터티가 왜 결과에 없는지. 필터에서 빠진 것을 파괴로 간주하지 않는다.</summary>
        private string Classify(EntityRefDto tracked, FilterDto filter)
        {
            GameObject found;
            switch (registry.Resolve(tracked, out found))
            {
                case EntityLifecycle.Destroyed:
                case EntityLifecycle.Unknown:
                    return "destroyed";
                case EntityLifecycle.WrongSession:
                    return "unobserved";
                case EntityLifecycle.Inactive:
                    return "inactive";
                default:
                    return PlayEntityScanner.Matches(found, filter) ? "out_of_scope" : "unobserved";
            }
        }

        // ---- sample -----------------------------------------------------------------------------------------

        private SampleDto Sample(SampleRequestDto request)
        {
            var playerScope = request.Scope != "debug";
            var sample = new SampleDto { Stamp = Stamp() };
            var objects = new Dictionary<string, GameObject>();

            foreach (var target in request.Targets ?? new List<SampleTargetRequestDto>())
            {
                GameObject entity;
                sample.Targets[target.Key] = ResolveTarget(target, playerScope, out entity);
                if (entity != null)
                {
                    objects[target.Key] = entity;
                }
            }

            foreach (var member in request.Members ?? new List<SampleMemberRequestDto>())
            {
                GameObject entity;
                MemberSampleDto read;
                if (!objects.TryGetValue(member.Target, out entity))
                {
                    read = new MemberSampleDto
                    {
                        Component = member.Component, Member = member.Member, Status = "unknown",
                        Reason = "the target could not be resolved"
                    };
                }
                else
                {
                    read = PlayFacts.ReadMember(entity, member.Component, member.Member, playerScope);
                }

                read.Target = member.Target;
                sample.Members.Add(read);
            }

            SampleProviderFacts(request, objects, playerScope, sample);
            return sample;
        }

        private static void SampleProviderFacts(
            SampleRequestDto request, Dictionary<string, GameObject> objects, bool playerScope, SampleDto sample)
        {
            foreach (var fact in request.Facts ?? new List<SampleFactRequestDto>())
            {
                var result = new FactSampleDto
                {
                    Target = fact.Target, ProviderId = fact.ProviderId, Name = fact.Name,
                    Status = "unknown", Reason = "the provider did not report this fact"
                };

                GameObject entity;
                if (fact.Target == null || !objects.TryGetValue(fact.Target, out entity))
                {
                    result.Reason = "provider facts belong to an entity; give a target";
                    sample.Facts.Add(result);
                    continue;
                }

                var context = new PlayObservationContext(
                    playerScope, Time.frameCount, string.Empty, Time.timeAsDouble, new List<GameObject> { entity });
                foreach (var pair in PlaySemantics.Describe(context))
                {
                    if (pair.Key != fact.ProviderId)
                    {
                        continue;
                    }

                    foreach (var entry in pair.Value.Facts)
                    {
                        if (entry.Entity != entity || entry.Name != fact.Name)
                        {
                            continue;
                        }

                        if (playerScope && entry.Visibility != PlayVisibility.Player)
                        {
                            result.Status = "unsupported";
                            result.Reason = "the provider does not expose this fact to the player scope";
                            continue;
                        }

                        result.Status = entry.Status;
                        result.Value = entry.Value;
                        result.Unit = entry.Unit;
                        result.Reason = entry.Reason;
                    }
                }

                sample.Facts.Add(result);
            }
        }

        private TargetSampleDto ResolveTarget(SampleTargetRequestDto target, bool playerScope, out GameObject entity)
        {
            entity = null;
            if (target.Ref != null)
            {
                return ResolveRef(target.Ref, playerScope, out entity);
            }

            var matches = new List<GameObject>();
            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var candidate = transform.gameObject;
                if (candidate.name != target.Name || PlayEntityScanner.IsOwn(candidate))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(target.Component) && !HasComponent(candidate, target.Component))
                {
                    continue;
                }

                // player scope 는 보이는 대상만 찾는다. 가려진 것을 파괴로 착각하지 않도록 없음과 구분하는 것은 호출자 몫이다.
                if (playerScope && !scanner.VisibleToPlayer(candidate))
                {
                    continue;
                }

                matches.Add(candidate);
            }

            if (matches.Count == 0)
            {
                return new TargetSampleDto { Status = "none", Count = 0 };
            }

            if (matches.Count > 1)
            {
                return new TargetSampleDto { Status = "ambiguous", Count = matches.Count };
            }

            entity = matches[0];
            var selectable = entity.GetComponent<Selectable>();
            return new TargetSampleDto
            {
                Status = "one",
                Count = 1,
                Ref = registry.Track(entity),
                Lifecycle = entity.activeInHierarchy ? "present" : "inactive",
                Active = entity.activeInHierarchy,
                Interactable = selectable == null ? (bool?)null : selectable.IsInteractable()
            };
        }

        private TargetSampleDto ResolveRef(EntityRefDto handle, bool playerScope, out GameObject entity)
        {
            var lifecycle = registry.Resolve(handle, out entity);
            var result = new TargetSampleDto { Status = "one", Count = 1, Ref = handle };
            switch (lifecycle)
            {
                case EntityLifecycle.Destroyed:
                case EntityLifecycle.Unknown:
                    result.Lifecycle = "destroyed";
                    result.Active = false;
                    entity = null;
                    return result;
                case EntityLifecycle.WrongSession:
                    result.Lifecycle = "unobserved";
                    entity = null;
                    return result;
                case EntityLifecycle.Inactive:
                    result.Lifecycle = "inactive";
                    result.Active = false;
                    return result;
            }

            result.Active = true;
            var selectable = entity.GetComponent<Selectable>();
            result.Interactable = selectable == null ? (bool?)null : selectable.IsInteractable();
            if (playerScope && !scanner.VisibleToPlayer(entity))
            {
                // 살아 있지만 player 에게 보이지 않는다. 파괴가 아니다.
                result.Lifecycle = "unobserved";
                entity = null;
                return result;
            }

            result.Lifecycle = "present";
            return result;
        }

        private static bool HasComponent(GameObject entity, string name)
        {
            foreach (var component in entity.GetComponents<Component>())
            {
                if (component != null && (component.GetType().Name == name || component.GetType().FullName == name))
                {
                    return true;
                }
            }

            return false;
        }

        // ---- inspect ----------------------------------------------------------------------------------------

        private InspectResultDto Inspect(InspectRequestDto request)
        {
            EntityRefDto handle;
            string kind, providerId, actionId;
            if (!PlayActions.TryParse(request.ActionRef, out handle, out kind, out providerId, out actionId))
            {
                throw new SpaceQueryException("invalid_request", "the actionRef is not one this runtime issued");
            }

            GameObject entity;
            switch (registry.Resolve(handle, out entity))
            {
                case EntityLifecycle.WrongSession:
                    throw new StaleReferenceException("the actionRef belongs to another Play session");
                case EntityLifecycle.Destroyed:
                case EntityLifecycle.Unknown:
                    throw new StaleReferenceException("the actionRef's entity was destroyed, or its id now belongs to a different object");
            }

            var playerScope = request.Scope != "debug";
            return providerId == null
                ? InspectBuiltIn(handle, entity, kind)
                : InspectProvider(handle, entity, providerId, actionId, playerScope);
        }

        private InspectResultDto InspectBuiltIn(EntityRefDto handle, GameObject entity, string kind)
        {
            foreach (var descriptor in PlayActions.Describe(entity))
            {
                if (descriptor.Kind != kind)
                {
                    continue;
                }

                var result = new InspectResultDto
                {
                    ActionRef = PlayActions.BuildRef(handle, kind),
                    Kind = kind,
                    Entity = handle,
                    Availability = entity.activeInHierarchy ? descriptor.Availability : "unavailable",
                    AvailabilityEvidence = descriptor.Evidence,
                    TargetConstraints = descriptor.TargetConstraints,
                    Costs = descriptor.Costs,
                    ExpectedEffects = descriptor.ExpectedEffects,
                    AcceptsTarget = descriptor.AcceptsTarget,
                    Source = "runtime",
                    Note = PlayActions.Note
                };
                result.Recipe.AddRange(descriptor.Recipe);
                return result;
            }

            throw new StaleReferenceException("the entity no longer offers a " + kind + " action");
        }

        private InspectResultDto InspectProvider(
            EntityRefDto handle, GameObject entity, string providerId, string actionId, bool playerScope)
        {
            var context = new PlayObservationContext(
                playerScope, Time.frameCount, string.Empty, Time.timeAsDouble, new List<GameObject> { entity });
            foreach (var pair in PlaySemantics.Describe(context))
            {
                if (pair.Key != providerId)
                {
                    continue;
                }

                foreach (var entry in pair.Value.Actions)
                {
                    if (entry.Entity != entity || entry.Action.Id != actionId)
                    {
                        continue;
                    }

                    if (playerScope && entry.Action.Visibility != PlayVisibility.Player)
                    {
                        throw new SpaceQueryException("out_of_scope", "the provider does not expose this action to the player scope");
                    }

                    return FromProviderAction(handle, providerId, entry.Action);
                }
            }

            throw new StaleReferenceException("the provider no longer offers action " + actionId + " on this entity");
        }

        private static InspectResultDto FromProviderAction(EntityRefDto handle, string providerId, PlayProviderAction action)
        {
            var result = new InspectResultDto
            {
                ActionRef = PlayActions.BuildProviderRef(handle, providerId, action.Id),
                Kind = "provider",
                Entity = handle,
                Availability = action.Available.HasValue ? (action.Available.Value ? "available" : "unavailable") : "unknown",
                AcceptsTarget = action.AcceptsTarget,
                Source = "provider",
                Note = action.Description
            };

            foreach (var step in action.Recipe)
            {
                result.Recipe.Add(step.Fields);
            }

            foreach (var json in action.PreconditionJson)
            {
                object predicate;
                if (TryParseJson(json, out predicate))
                {
                    result.Preconditions.Add(new PreconditionDto { Predicate = predicate });
                }
            }

            foreach (var json in action.OutcomeJson)
            {
                object predicate;
                if (TryParseJson(json, out predicate))
                {
                    result.OutcomePredicates.Add(predicate);
                }
            }

            foreach (var cost in action.Costs)
            {
                result.Costs.Add(ProviderFact(handle, providerId, cost));
            }

            foreach (var effect in action.ExpectedEffects)
            {
                result.ExpectedEffects.Add(ProviderFact(handle, providerId, effect));
            }

            return result;
        }

        private static FactDto ProviderFact(EntityRefDto handle, string providerId, PlayFactDeclaration declaration)
        {
            return new FactDto
            {
                Name = declaration.Name,
                Value = declaration.Value,
                Unit = declaration.Unit,
                Status = "known",
                Source = "provider",
                Evidence = new EvidenceDto { Entity = handle, ProviderId = providerId }
            };
        }

        private static bool TryParseJson(string json, out object value)
        {
            value = null;
            try
            {
                value = JToken.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>낡은 handle 이나 actionRef. MCP server 에는 <c>stale_ref</c> 로 전해진다.</summary>
    internal sealed class StaleReferenceException : Exception
    {
        public StaleReferenceException(string message)
            : base(message)
        {
        }
    }
}
