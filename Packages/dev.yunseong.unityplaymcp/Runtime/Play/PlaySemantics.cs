using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace UnityPlayMcp.Play
{
    /// <summary>provider 가 내놓는 정보를 어느 scope 에 보여 줄지 선언한다. 안 정하면 <see cref="Debug"/> 다.</summary>
    public enum PlayVisibility
    {
        /// <summary>debug scope 에만 보인다. 기본값이다.</summary>
        Debug = 0,

        /// <summary>player scope 에도 보인다. 플레이어가 게임 안에서 알 수 있는 정보만 이 값을 쓴다.</summary>
        Player = 1
    }

    /// <summary>provider 가 <see cref="PlaySemanticProvider.Describe"/> 에서 읽는 관찰 맥락. 읽기 전용이다.</summary>
    public sealed class PlayObservationContext
    {
        internal PlayObservationContext(
            bool isPlayerScope, int frame, string sceneName, double gameTimeSeconds, IReadOnlyList<GameObject> entities)
        {
            IsPlayerScope = isPlayerScope;
            Frame = frame;
            SceneName = sceneName;
            GameTimeSeconds = gameTimeSeconds;
            Entities = entities;
        }

        public bool IsPlayerScope { get; }
        public int Frame { get; }
        public string SceneName { get; }
        public double GameTimeSeconds { get; }

        /// <summary>이번 관찰에 들어간 엔터티들.</summary>
        public IReadOnlyList<GameObject> Entities { get; }
    }

    /// <summary>
    /// 게임이 선택적으로 등록하는 의미 정보 provider.
    /// </summary>
    /// <remarks>
    /// provider 는 게임 상태를 바꾸거나 입력을 직접 실행하지 않는다. 사실, 관계, 행동 설명(기존 입력 recipe), 의미 있는
    /// 이벤트만 더한다. Unity main thread 에서 불리므로 짧고 blocking 이 없어야 한다. 한 번 부르는 데
    /// <see cref="PlaySemantics.SlowBudgetMilliseconds"/> 를 넘기는 일이 연달아 <see cref="PlaySemantics.MaxSlowRuns"/>번 이어지면
    /// 비활성화된다. 예외를 던져도 기본 관찰은 계속 동작하고 그 provider 의 오류로만 기록된다.
    /// </remarks>
    public abstract class PlaySemanticProvider
    {
        /// <summary>provider 를 구분하는 이름. 등록된 것과 겹치면 등록이 거부된다.</summary>
        public abstract string Id { get; }

        /// <summary>true 이면 <see cref="IsVisibleToPlayer"/> 가 게임 규칙상 가시성을 판정한다.</summary>
        /// <remarks>
        /// 렌더러 가시성은 fog-of-war 를 보증하지 않는다. 엄격한 player 가시성은 이 값을 true 로 하는 provider 가 있을 때만
        /// 보증하고, 응답의 <c>visibilityGuarantee</c> 가 <c>provider</c> 가 된다.
        /// </remarks>
        public virtual bool DeclaresPlayerVisibility { get { return false; } }

        /// <summary>player scope 에서 이 엔터티를 보여도 되는지. 기본 필터와 함께 적용되며 더 제한적인 쪽을 따른다.</summary>
        public virtual bool IsVisibleToPlayer(GameObject entity)
        {
            return true;
        }

        /// <summary>엔터티에 사실, 관계, 행동을 더한다.</summary>
        public abstract void Describe(PlayObservationContext context, PlayProviderOutput output);
    }

    /// <summary>recipe 를 이루는 step. 기존 입력 경로만 만들 수 있다. 실행은 MCP server 가 한다.</summary>
    public sealed class PlayRecipeStep
    {
        internal PlayRecipeStep(Dictionary<string, object> fields)
        {
            Fields = fields;
        }

        internal Dictionary<string, object> Fields { get; }

        /// <summary>Unity Screen 픽셀(좌상단 기준)을 누르고 뗀다.</summary>
        public static PlayRecipeStep PointerClick(float x, float y)
        {
            return new PlayRecipeStep(new Dictionary<string, object>
            {
                { "method", "pointerClick" }, { "x", x }, { "y", y }
            });
        }

        /// <summary>엔터티의 지금 화면 위 중심을 누른다. 화면에 없으면 null 이다.</summary>
        public static PlayRecipeStep PointerClickOn(GameObject entity)
        {
            float x, y;
            return PlayGeometry.TryScreenCenter(entity, out x, out y) ? PointerClick(x, y) : null;
        }

        public static PlayRecipeStep PointerDrag(float fromX, float fromY, float toX, float toY)
        {
            return new PlayRecipeStep(new Dictionary<string, object>
            {
                { "method", "pointerDrag" }, { "fromX", fromX }, { "fromY", fromY }, { "toX", toX }, { "toY", toY }
            });
        }

        /// <summary>한 엔터티에서 다른 엔터티로 끈다. 어느 쪽이든 화면에 없으면 null 이다.</summary>
        public static PlayRecipeStep PointerDragBetween(GameObject from, GameObject to)
        {
            float fx, fy, tx, ty;
            if (!PlayGeometry.TryScreenCenter(from, out fx, out fy) || !PlayGeometry.TryScreenCenter(to, out tx, out ty))
            {
                return null;
            }

            return PointerDrag(fx, fy, tx, ty);
        }

        public static PlayRecipeStep KeyClick(KeyCode key, float seconds)
        {
            return new PlayRecipeStep(new Dictionary<string, object>
            {
                { "method", "key_click" }, { "key", key.ToString() }, { "seconds", seconds }
            });
        }

        public static PlayRecipeStep KeyDown(KeyCode key)
        {
            return new PlayRecipeStep(new Dictionary<string, object> { { "method", "key_down" }, { "key", key.ToString() } });
        }

        public static PlayRecipeStep KeyUp(KeyCode key)
        {
            return new PlayRecipeStep(new Dictionary<string, object> { { "method", "key_up" }, { "key", key.ToString() } });
        }

        public static PlayRecipeStep SetAxis(string name, float value)
        {
            return new PlayRecipeStep(new Dictionary<string, object>
            {
                { "method", "set_axis" }, { "name", name }, { "value", value }
            });
        }

        /// <summary>프레임을 기다린다. 애니메이션이 끝나기를 기다릴 때 고정 sleep 대신 쓴다.</summary>
        public static PlayRecipeStep WaitFrames(int frames)
        {
            return new PlayRecipeStep(new Dictionary<string, object>
            {
                { "method", "waitFrames" }, { "frames", Mathf.Clamp(frames, 1, 600) }
            });
        }
    }

    /// <summary>MCP server 가 읽는 predicate v1 을 JSON 으로 만드는 도우미. 값 비교는 같은 단위끼리만 통한다.</summary>
    public static class PlayPredicates
    {
        public static string SceneIs(string scene)
        {
            return "{\"sceneIs\":" + Quote(scene) + "}";
        }

        /// <param name="op">eq, ne, lt, lte, gt, gte, changed 중 하나.</param>
        public static string FactCompare(string providerId, string factName, string op, double value, string unit = null)
        {
            return "{\"fact\":{\"providerId\":" + Quote(providerId) + ",\"name\":" + Quote(factName)
                + ",\"op\":" + Quote(op) + ",\"value\":" + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + (unit == null ? string.Empty : ",\"unit\":" + Quote(unit)) + "}}";
        }

        public static string FactChanged(string providerId, string factName)
        {
            return "{\"fact\":{\"providerId\":" + Quote(providerId) + ",\"name\":" + Quote(factName) + ",\"op\":\"changed\"}}";
        }

        public static string EventMatches(string providerId, string kind)
        {
            return "{\"eventMatches\":{\"providerId\":" + Quote(providerId) + ",\"kind\":" + Quote(kind) + "}}";
        }

        private static string Quote(string text)
        {
            return Newtonsoft.Json.JsonConvert.ToString(text ?? string.Empty);
        }
    }

    /// <summary>provider 가 등록하는 행동 하나. 실행 코드가 아니라 기존 입력으로 이루어진 recipe 만 담는다.</summary>
    public sealed class PlayProviderAction
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public PlayVisibility Visibility { get; set; }

        /// <summary>null 이면 가능 여부를 모른다(unknown).</summary>
        public bool? Available { get; set; }

        public List<PlayRecipeStep> Recipe { get; } = new List<PlayRecipeStep>();

        /// <summary>실행 직전에 참이어야 하는 조건들(<see cref="PlayPredicates"/>). 각 항목은 JSON 하나다.</summary>
        public List<string> PreconditionJson { get; } = new List<string>();

        /// <summary>성공을 관찰할 수 있는 조건들. 모든 조건이 참이어야 confirmed 가 된다.</summary>
        public List<string> OutcomeJson { get; } = new List<string>();

        /// <summary>비용, 쿨다운 같은 알려진 사실. 모르면 넣지 않는다(0 으로 쓰지 않는다).</summary>
        public List<PlayFactDeclaration> Costs { get; } = new List<PlayFactDeclaration>();

        /// <summary>게임이 선언한 예상 효과. 실제로 일어났다는 뜻이 아니다.</summary>
        public List<PlayFactDeclaration> ExpectedEffects { get; } = new List<PlayFactDeclaration>();

        /// <summary>대상이 필요하면 "entity", "screen", "world". 필요 없으면 null.</summary>
        public string AcceptsTarget { get; set; }
    }

    /// <summary>이름·값·단위가 있는 선언 하나.</summary>
    public sealed class PlayFactDeclaration
    {
        public PlayFactDeclaration(string name, object value, string unit = null)
        {
            Name = name;
            Value = value;
            Unit = unit;
        }

        public string Name { get; }
        public object Value { get; }
        public string Unit { get; }
    }

    /// <summary>provider 가 채우는 출력. 검증에 실패한 항목은 버리고 그 provider 의 오류로 기록한다.</summary>
    public sealed class PlayProviderOutput
    {
        internal sealed class FactEntry
        {
            public GameObject Entity;
            public string Name;
            public object Value;
            public string Unit;
            public string Status;
            public string Reason;
            public PlayVisibility Visibility;
        }

        internal sealed class RelationEntry
        {
            public GameObject Entity;
            public string Name;
            public GameObject Target;
            public PlayVisibility Visibility;
        }

        internal sealed class ActionEntry
        {
            public GameObject Entity;
            public PlayProviderAction Action;
        }

        internal readonly List<FactEntry> Facts = new List<FactEntry>();
        internal readonly List<RelationEntry> Relations = new List<RelationEntry>();
        internal readonly List<ActionEntry> Actions = new List<ActionEntry>();
        internal readonly List<string> Errors = new List<string>();

        /// <summary>공개해도 되는 값 하나를 더한다. 값은 bool, 숫자, 문자열, enum 만 받는다.</summary>
        public void AddFact(
            GameObject entity, string name, object value, string unit = null,
            PlayVisibility visibility = PlayVisibility.Debug)
        {
            if (entity == null || !PlaySemantics.ValidName(name))
            {
                Errors.Add("fact has no entity or an invalid name: " + name);
                return;
            }

            if (!PlaySemantics.IsPlainValue(value))
            {
                Errors.Add("fact " + name + " has a value that is not a bool, number, string or enum");
                return;
            }

            Facts.Add(new FactEntry
            {
                Entity = entity,
                Name = name,
                Value = value is Enum ? value.ToString() : value,
                Unit = unit,
                Status = "known",
                Visibility = visibility
            });
        }

        /// <summary>값을 알 수 없다고 알린다. 모르는 값을 0 이나 빈 값으로 채우지 않는다.</summary>
        public void AddUnknownFact(
            GameObject entity, string name, string reason, PlayVisibility visibility = PlayVisibility.Debug)
        {
            if (entity == null || !PlaySemantics.ValidName(name))
            {
                Errors.Add("fact has no entity or an invalid name: " + name);
                return;
            }

            Facts.Add(new FactEntry
            {
                Entity = entity, Name = name, Status = "unknown", Reason = reason, Visibility = visibility
            });
        }

        /// <summary>엔터티 사이의 관계. 이름은 <c>namespace:name</c> 꼴이고, 코어는 그 전술적 의미를 가정하지 않는다.</summary>
        public void AddRelation(
            GameObject entity, string namespacedName, GameObject target,
            PlayVisibility visibility = PlayVisibility.Debug)
        {
            if (entity == null || target == null || namespacedName == null || namespacedName.IndexOf(':') <= 0
                || !PlaySemantics.ValidName(namespacedName))
            {
                Errors.Add("relation needs both entities and a namespaced name like ns:name: " + namespacedName);
                return;
            }

            Relations.Add(new RelationEntry
            {
                Entity = entity, Name = namespacedName, Target = target, Visibility = visibility
            });
        }

        public void AddAction(GameObject entity, PlayProviderAction action)
        {
            if (entity == null || action == null || !PlaySemantics.ValidName(action.Id))
            {
                Errors.Add("action has no entity or an invalid id");
                return;
            }

            foreach (var step in action.Recipe)
            {
                if (step == null)
                {
                    // 화면에 없는 엔터티를 겨눈 step 이다. 일부만 실행하지 않도록 행동째 버린다.
                    Errors.Add("action " + action.Id + " has a recipe step that could not be built (target off screen?)");
                    return;
                }
            }

            Actions.Add(new ActionEntry { Entity = entity, Action = action });
        }
    }

    /// <summary>
    /// provider 등록소와 provider event 의 ring buffer.
    /// </summary>
    public static class PlaySemantics
    {
        /// <summary>provider 한 번 호출에 허용하는 시간(밀리초).</summary>
        public const double SlowBudgetMilliseconds = 4d;

        /// <summary>이만큼 연달아 예산을 넘기면 provider 를 비활성화한다.</summary>
        public const int MaxSlowRuns = 3;

        public const int EventCapacity = 1024;

        internal sealed class ProviderState
        {
            public PlaySemanticProvider Provider;
            public string Status = "active";
            public int SlowCount;
            public int ConsecutiveSlow;
            public string LastError;
            public double LastElapsedMs;
        }

        internal sealed class BufferedEvent
        {
            public long Sequence;
            public string ProviderId;
            public string Kind;
            public string Name;
            public GameObject Entity;
            public object Data;
            public int Frame;
            public string Scene;
            public double GameTimeSeconds;
            public bool PlayerVisible;
        }

        private static readonly List<ProviderState> Providers = new List<ProviderState>();
        private static readonly LinkedList<BufferedEvent> Events = new LinkedList<BufferedEvent>();
        private static long nextSequence = 1;
        private static long dropped;

        /// <summary>등록한다. id 가 비었거나 이미 있으면 false 를 돌려주고 기존 provider 는 그대로 둔다.</summary>
        public static bool Register(PlaySemanticProvider provider)
        {
            if (provider == null)
            {
                return false;
            }

            string id;
            try
            {
                id = provider.Id;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Unity Play MCP] A semantic provider threw while reading its id: " + exception.Message);
                return false;
            }

            if (!ValidName(id))
            {
                Debug.LogWarning("[Unity Play MCP] A semantic provider has an invalid id and was not registered.");
                return false;
            }

            lock (Providers)
            {
                if (Providers.Exists(state => state.Provider.Id == id))
                {
                    Debug.LogWarning("[Unity Play MCP] A semantic provider with id '" + id + "' is already registered.");
                    return false;
                }

                Providers.Add(new ProviderState { Provider = provider });
                return true;
            }
        }

        public static bool Unregister(string id)
        {
            lock (Providers)
            {
                return Providers.RemoveAll(state => state.Provider.Id == id) > 0;
            }
        }

        /// <summary>provider 가 명시적으로 알리는 의미 있는 사건. 발생한 프레임과 시각을 함께 기록한다.</summary>
        /// <remarks>피해나 승리 같은 의미는 provider 가 정한다. 코어는 일반 상태 변화에서 이런 사건을 만들지 않는다.</remarks>
        public static void Emit(
            string providerId, string kind, string name = null, GameObject entity = null,
            object data = null, PlayVisibility visibility = PlayVisibility.Debug)
        {
            if (!ValidName(providerId) || !ValidName(kind))
            {
                return;
            }

            if (data != null && !IsPlainValue(data) && !(data is System.Collections.IDictionary))
            {
                data = data.ToString();
            }

            var scene = SceneManager.GetActiveScene();
            lock (Events)
            {
                Events.AddLast(new BufferedEvent
                {
                    Sequence = nextSequence++,
                    ProviderId = providerId,
                    Kind = kind,
                    Name = name,
                    Entity = entity,
                    Data = data,
                    Frame = Time.frameCount,
                    Scene = scene.IsValid() ? scene.name : string.Empty,
                    GameTimeSeconds = Time.timeAsDouble,
                    PlayerVisible = visibility == PlayVisibility.Player
                });
                while (Events.Count > EventCapacity)
                {
                    Events.RemoveFirst();
                    dropped++;
                }
            }
        }

        internal static List<BufferedEvent> ReadEvents(long after, int limit, out long next, out long droppedTotal)
        {
            var found = new List<BufferedEvent>();
            lock (Events)
            {
                next = nextSequence - 1;
                droppedTotal = dropped;
                foreach (var item in Events)
                {
                    if (item.Sequence <= after)
                    {
                        continue;
                    }

                    if (found.Count >= limit)
                    {
                        next = found[found.Count - 1].Sequence;
                        break;
                    }

                    found.Add(item);
                }
            }

            return found;
        }

        internal static List<ProviderStatusDto> Statuses()
        {
            var list = new List<ProviderStatusDto>();
            lock (Providers)
            {
                foreach (var state in Providers)
                {
                    list.Add(new ProviderStatusDto
                    {
                        Id = state.Provider.Id,
                        Status = state.Status,
                        SlowCount = state.SlowCount,
                        LastError = state.LastError,
                        LastElapsedMs = state.LastElapsedMs
                    });
                }
            }

            return list;
        }

        /// <summary>strict player 가시성을 판정하는 provider 가 하나라도 있는지.</summary>
        internal static bool HasVisibilityProvider()
        {
            lock (Providers)
            {
                return Providers.Exists(state => state.Status == "active" && SafeDeclares(state));
            }
        }

        /// <summary>등록된 모든 활성 provider 가 이 엔터티를 player 에게 보여도 된다고 하는지. 가장 제한적인 쪽을 따른다.</summary>
        internal static bool VisibleToPlayer(GameObject entity)
        {
            lock (Providers)
            {
                foreach (var state in Providers)
                {
                    if (state.Status != "active" || !SafeDeclares(state))
                    {
                        continue;
                    }

                    try
                    {
                        if (!state.Provider.IsVisibleToPlayer(entity))
                        {
                            return false;
                        }
                    }
                    catch (Exception exception)
                    {
                        // 판정하지 못하면 보여 주지 않는다. 숨긴 값이 새는 것보다 낫다.
                        state.LastError = exception.Message;
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SafeDeclares(ProviderState state)
        {
            try
            {
                return state.Provider.DeclaresPlayerVisibility;
            }
            catch (Exception exception)
            {
                state.LastError = exception.Message;
                return false;
            }
        }

        /// <summary>활성 provider 를 모두 부르고 출력을 모은다. 예외와 느린 호출은 그 provider 안에 가둔다.</summary>
        internal static List<KeyValuePair<string, PlayProviderOutput>> Describe(PlayObservationContext context)
        {
            var outputs = new List<KeyValuePair<string, PlayProviderOutput>>();
            List<ProviderState> snapshot;
            lock (Providers)
            {
                snapshot = new List<ProviderState>(Providers);
            }

            foreach (var state in snapshot)
            {
                if (state.Status != "active")
                {
                    continue;
                }

                var output = new PlayProviderOutput();
                var timer = Stopwatch.StartNew();
                try
                {
                    state.Provider.Describe(context, output);
                }
                catch (Exception exception)
                {
                    state.LastError = exception.GetType().Name + ": " + exception.Message;
                    output = new PlayProviderOutput();
                }

                timer.Stop();
                state.LastElapsedMs = timer.Elapsed.TotalMilliseconds;
                NoteDuration(state);
                if (output.Errors.Count > 0)
                {
                    state.LastError = output.Errors[0];
                }

                outputs.Add(new KeyValuePair<string, PlayProviderOutput>(state.Provider.Id, output));
            }

            return outputs;
        }

        private static void NoteDuration(ProviderState state)
        {
            if (state.LastElapsedMs <= SlowBudgetMilliseconds)
            {
                state.ConsecutiveSlow = 0;
                return;
            }

            state.SlowCount++;
            state.ConsecutiveSlow++;
            if (state.ConsecutiveSlow >= MaxSlowRuns)
            {
                state.Status = "disabled_slow";
                state.LastError = "exceeded " + SlowBudgetMilliseconds + "ms " + MaxSlowRuns + " times in a row";
                Debug.LogWarning("[Unity Play MCP] Semantic provider '" + state.Provider.Id + "' was disabled: " + state.LastError);
            }
        }

        /// <summary>테스트가 등록소를 처음 상태로 되돌린다.</summary>
        internal static void ResetForTests()
        {
            lock (Providers)
            {
                Providers.Clear();
            }

            lock (Events)
            {
                Events.Clear();
                nextSequence = 1;
                dropped = 0;
            }
        }

        internal static bool ValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64)
            {
                return false;
            }

            foreach (var character in name)
            {
                var ok = char.IsLetterOrDigit(character) || character == '_' || character == '.' || character == ':'
                    || character == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsPlainValue(object value)
        {
            return value is string || value is bool || value is Enum || value is int || value is long
                || value is float || value is double || value is short || value is byte || value is uint;
        }
    }
}
