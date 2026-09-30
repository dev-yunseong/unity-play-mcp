using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 방문한 모든 씬에서 읽은 내용을 모은다.
    /// </summary>
    /// <remarks>
    /// 스캔 하나는 씬 하나만 다루므로 덮어쓰지 않고 누적한다.
    /// 씬 이름으로 키를 잡아 같은 씬을 다시 방문하면 마지막 방문 결과로 교체한다.
    /// </remarks>
    public static class AffordanceReport
    {
        /// <summary>
        /// 리포트 스키마 버전. 뜻이 바뀌면 올려 옛 버전만 아는 읽는 쪽이 거절하게 한다.
        /// </summary>
        /// <remarks>
        /// 뜻을 바꾸지 않는 추가는 <c>capabilities</c> 로 알린다.
        /// 6: <c>label</c> 이 플레이어가 누를 수 있는 것 위의 텍스트로 좁아졌다.
        /// 7: <c>createdBy</c> 항목이 문자열에서 객체로 바뀌었고 <c>cut</c> 이 생겼다.
        /// <c>cut</c> 이 없으면 빈 <c>createdBy</c> 가 "만드는 곳이 없다"와 "걷지 못했다"를 구분하지 못한다.
        /// </remarks>
        public const int SchemaVersion = 7;

        /// <summary>리포트가 담는 최대 씬 수. 넘치면 새 씬은 버린다.</summary>
        private const int MaxScenes = 256;

        /// <summary>
        /// <c>unplaced</c> `evidence` 에 쓰는 최대 바이트.
        /// </summary>
        /// <remarks>
        /// 분석기는 무관한 패키지까지 굽고 항목 크기 편차가 커서 개수가 아니라 바이트로 제한한다.
        /// 만드는 곳이 알려진 것, 작은 것 순으로 쓰고, 넘친 것은 <c>gaps</c> 에 센다.
        /// </remarks>
        private const int UnplacedBudget = 512 * 1024;

        private const int MaxMakers = 8;

        /// <summary>분석기가 호출 대상을 적는 방식: 어셈블리, 타입, 메서드, 시그니처.</summary>
        private const string TargetMarker = "\"targetId\":\"";

        private static readonly Dictionary<string, List<Maker>> Makers =
            new Dictionary<string, List<Maker>>(System.StringComparer.Ordinal);

        /// <summary>
        /// `evidence` 는 있지만 어느 씬에서도 만나지 못한 타입들.
        /// </summary>
        /// <remarks>
        /// 만난 타입은 순회가 끝나야 정해지므로 끝에서 계산한다.
        /// 카탈로그 키는 컴파일 시점 이름이라 난독화 후 이름과 다를 수 있어 문서도 비교한다.
        /// </remarks>
        private static List<KeyValuePair<string, string>> Unplaced()
        {
            var placed = new HashSet<string>(Types.Values, System.StringComparer.Ordinal);
            var missing = new List<KeyValuePair<string, string>>();

            foreach (var pair in AffordanceCatalog.Everything())
            {
                if (!Types.ContainsKey(pair.Key) && !placed.Contains(pair.Value))
                {
                    missing.Add(pair);
                }
            }

            // 만드는 곳이 알려진 것, 작은 것 순으로 정렬하고, 이름으로 마무리해 출력이 결정적이게 한다.
            missing.Sort((left, right) =>
            {
                var madeLeft = Makers.ContainsKey(left.Key) ? 0 : 1;
                var madeRight = Makers.ContainsKey(right.Key) ? 0 : 1;

                if (madeLeft != madeRight)
                {
                    return madeLeft - madeRight;
                }

                return left.Value.Length != right.Value.Length
                    ? left.Value.Length - right.Value.Length
                    : string.CompareOrdinal(left.Key, right.Key);
            });

            return missing;
        }

        /// <summary>
        /// 타입별 호출자 목록. `evidence` 의 호출 대상에서 읽는다.
        /// </summary>
        /// <remarks>
        /// 씬에 없고, 만드는 곳도 호출자도 없는 타입이 죽은 코드임을 가리는 데 쓴다.
        /// 호출 대상만 필요하므로 파싱하지 않고 텍스트에서 찾는다. 양쪽 모두 컴파일 시점 이름이므로 이름으로 매칭한다.
        /// 호출자도 죽었을 수 있으므로 플래그가 아니라 호출자를 나열한다.
        /// </remarks>
        private static Dictionary<string, List<string>> Callers()
        {
            var callers = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

            foreach (var pair in AffordanceCatalog.Everything())
            {
                var document = pair.Value;
                var at = document.IndexOf(TargetMarker, System.StringComparison.Ordinal);

                while (at >= 0)
                {
                    var from = at + TargetMarker.Length;
                    var bar = document.IndexOf('|', from);
                    var end = bar < 0 ? -1 : document.IndexOf('|', bar + 1);

                    if (end > bar)
                    {
                        Calls(callers, document.Substring(bar + 1, end - bar - 1), pair.Key);
                    }

                    at = document.IndexOf(TargetMarker, from, System.StringComparison.Ordinal);
                }
            }

            return callers;
        }

        private static void Calls(
            Dictionary<string, List<string>> callers, string callee, string caller)
        {
            if (callee.Length == 0 || callee == caller)
            {
                return;
            }

            if (!callers.TryGetValue(callee, out var found))
            {
                found = new List<string>();
                callers[callee] = found;
            }

            if (!found.Contains(caller) && found.Count < MaxMakers)
            {
                found.Add(caller);
            }
        }

        /// <summary>
        /// 씬의 무언가가 참조하는 프리팹이 이 타입을 가진다고 기록한다.
        /// </summary>
        /// <remarks>
        /// 아무도 만들지 않는 타입과 아직 만나지 못한 타입을 구분하는 데 쓴다.
        /// 이것이 없으면 죽은 타입의 규칙이 게임 규칙으로 보고된다.
        /// </remarks>
        internal static void Creates(
            string carriedType, string ownerType, string field, string prefabName, int prefabId)
        {
            if (string.IsNullOrEmpty(carriedType) || string.IsNullOrEmpty(ownerType))
            {
                return;
            }

            Record(carriedType, new Maker
            {
                Field = ownerType + "." + field,
                Prefab = prefabName,
                PrefabId = prefabId
            });
        }

        /// <summary>
        /// 걷기가 깊이 제한에 걸린 자리의 프리팹을 기록한다.
        /// </summary>
        /// <remarks>
        /// 기록하지 않으면 <c>createdBy</c> 가 비어 죽은 코드로 읽힌다. 빈 목록은 만드는 곳이 없다는 뜻이어야 한다.
        /// <c>cut</c> 은 이 프리팹 너머로 더 걷지 않았다는 뜻이다. 그 너머의 프리팹은 리포트에 없다.
        /// </remarks>
        internal static void CreatesCut(
            string carriedType, string ownerType, string field, string prefabName, int prefabId, string reason)
        {
            if (string.IsNullOrEmpty(carriedType) || string.IsNullOrEmpty(ownerType))
            {
                return;
            }

            Record(carriedType, new Maker
            {
                Field = ownerType + "." + field,
                Prefab = prefabName,
                PrefabId = prefabId,
                Cut = reason
            });

            WalkGap("trace-depth-exceeded:" + carriedType);
        }

        /// <summary>같은 프리팹을 두 번 적지 않으면서, 넘친 자리를 gap 으로 남긴다.</summary>
        private static void Record(string key, Maker maker)
        {
            if (!Makers.TryGetValue(key, out var makers))
            {
                makers = new List<Maker>();
                Makers[key] = makers;
            }

            for (var at = 0; at < makers.Count; at++)
            {
                if (makers[at].Field == maker.Field && makers[at].PrefabId == maker.PrefabId)
                {
                    return;
                }
            }

            if (makers.Count >= MaxMakers)
            {
                // 개수만으로는 잘렸는지 알 수 없으므로 gap 을 남긴다.
                WalkGap("makers-truncated:" + key);
                return;
            }

            makers.Add(maker);
        }

        /// <summary>
        /// 한 프리팹이 나르는 컴포넌트 목록이 한계에 걸렸다.
        /// </summary>
        internal static void CarriedTruncated(string prefabName)
        {
            if (!string.IsNullOrEmpty(prefabName))
            {
                WalkGap("carried-truncated:" + prefabName);
            }
        }

        /// <summary>
        /// <c>createdBy</c> 한 항목. 어느 필드가 어느 프리팹을 쥐고 있는가.
        /// </summary>
        /// <remarks>
        /// <see cref="PrefabId"/> 는 <c>refs[].id</c> 와 같은 값이라 리포트 안에서 프리팹 단위로 조인할 수 있다.
        /// 실행이 바뀌면 뜻이 없으며, 실행을 넘는 비교는 이름과 <c>carries</c> 로 한다.
        /// </remarks>
        internal struct Maker
        {
            /// <summary>이 프리팹을 참조하는 필드. 깊이 제한에 걸린 항목은 출발 필드다.</summary>
            public string Field;
            public string Prefab;
            public int PrefabId;

            /// <summary>걷기가 멈춘 이유. null 이면 끝까지 읽었다.</summary>
            public string Cut;
        }

        private static readonly List<string> Order = new List<string>();
        private static readonly Dictionary<string, string> Objects = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<string>> Gaps = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>();

        /// <summary>리포트에 담긴 씬 수.</summary>
        public static int SceneCount => Order.Count;

        internal static void Merge(string scene, string objects, List<string> gaps)
        {
            var name = string.IsNullOrEmpty(scene) ? "(unnamed)" : scene;

            if (!Objects.ContainsKey(name))
            {
                if (Order.Count >= MaxScenes)
                {
                    return;
                }

                Order.Add(name);
            }

            Objects[name] = objects;
            Gaps[name] = gaps;
        }

        /// <summary>씬 로드 사이에 남는 객체(DontDestroyOnLoad). 리포트 전체에 한 번 읽는다.</summary>
        internal static void Persistent(string objects, List<string> gaps)
        {
            _persistent = objects ?? string.Empty;
            _persistentRead = true;

            if (gaps == null)
            {
                return;
            }

            foreach (var gap in gaps)
            {
                if (!_persistentGaps.Contains(gap))
                {
                    _persistentGaps.Add(gap);
                }
            }
        }

        private static string _persistent = string.Empty;
        private static bool _persistentRead;
        private static readonly List<string> _persistentGaps = new List<string>();

        /// <summary>
        /// 걷기가 버린 것. 프리팹과 타입에 대한 것이라 씬에 귀속하지 않는다.
        /// </summary>
        /// <remarks>
        /// 같은 한계가 씬마다 반복되므로 집합으로 중복을 없앤다. 반복하면 읽는 쪽이 빈도로 오해한다.
        /// </remarks>
        private static readonly HashSet<string> _walkGaps = new HashSet<string>();

        private static void WalkGap(string gap) => _walkGaps.Add(gap);

        /// <summary>이미 읽은 씬에 gap 을 하나 더한다.</summary>
        internal static void Note(string scene, string gap)
        {
            var name = string.IsNullOrEmpty(scene) ? "(unnamed)" : scene;

            if (Gaps.TryGetValue(name, out var already) && !already.Contains(gap))
            {
                already.Add(gap);
            }
        }

        /// <summary>
        /// 타입의 `evidence` 를 이미 기록했는지.
        /// </summary>
        /// <remarks>
        /// `evidence` 는 컴파일 시점에 정해져 인스턴스마다 같으므로 타입당 한 번만 기록한다.
        /// </remarks>
        internal static bool Knows(string type)
        {
            return Types.ContainsKey(type);
        }

        internal static void Learn(string type, string evidenceArray)
        {
            if (!string.IsNullOrEmpty(type) && !Types.ContainsKey(type))
            {
                Types[type] = evidenceArray;
            }
        }

        /// <summary>
        /// 씬의 `wiring` 이 호출하는 타입을 기록한다.
        /// </summary>
        /// <remarks>
        /// 그 타입은 뒤의 씬에서 만날 수 있으므로 `evidence` 여부는 모든 씬을 방문한 뒤에 판단한다.
        /// </remarks>
        internal static void Wired(string type)
        {
            if (!string.IsNullOrEmpty(type))
            {
                WiredTo.Add(type);
            }
        }

        private static readonly HashSet<string> WiredTo = new HashSet<string>();

        private static int _unplacedOmitted;

        /// <summary>모은 내용을 모두 버린다. 새 순회가 이전 결과를 이어받지 않게 한다.</summary>
        public static void Forget()
        {
            Order.Clear();
            Objects.Clear();
            Gaps.Clear();
            Types.Clear();
            WiredTo.Clear();
            Makers.Clear();
            _unplacedOmitted = 0;
            _persistent = string.Empty;
            _persistentRead = false;
            _persistentGaps.Clear();
            _walkGaps.Clear();
            SerializedReferences.Forget();
        }

        /// <summary>
        /// 씬을 읽을 때 게임 코드가 돌았는지.
        /// </summary>
        /// <remarks>
        /// 에디터 순회는 저장된 값을, 플레이어는 <c>Awake</c>/<c>Start</c> 이후의 값을 읽으므로 같은 필드도 뜻이 다르다.
        /// 읽는 쪽이 한순간의 값을 규칙으로 오해하지 않도록 문서 안에 적는다.
        /// </remarks>
        private static string Capture()
        {
            if (!Application.isEditor)
            {
                return "player";
            }

            return Application.isPlaying ? "editor-play" : "editor";
        }

        /// <summary>
        /// <c>createdBy</c> 한 항목을 쓴다.
        /// </summary>
        /// <remarks>
        /// <c>cut</c> 항목에 <c>carries</c> 가 없는 것은 걷지 않았다는 뜻이지 컴포넌트가 없다는 뜻이 아니다.
        /// </remarks>
        internal static void WriteMaker(StringBuilder text, Maker maker)
        {
            text.Append('{');

            var wrote = false;

            if (!string.IsNullOrEmpty(maker.Field))
            {
                Json.Property(text, "field", maker.Field);
                wrote = true;
            }

            if (!string.IsNullOrEmpty(maker.Prefab))
            {
                if (wrote)
                {
                    text.Append(',');
                }

                Json.Property(text, "prefab", maker.Prefab);
                text.Append(',');
                Json.Property(text, "prefabId", maker.PrefabId);
                wrote = true;
            }

            if (!string.IsNullOrEmpty(maker.Cut))
            {
                if (wrote)
                {
                    text.Append(',');
                }

                Json.Property(text, "cut", maker.Cut);
            }

            text.Append('}');
        }

        /// <summary>
        /// 이 문서의 필드가 지키는 약속 목록.
        /// </summary>
        /// <remarks>
        /// 버전을 올리면 모든 읽는 쪽이 거절하므로, 필드 뜻의 변화는 약속 이름으로 알리고 읽는 쪽은 필요한 것을 확인한다.
        /// 약속은 필드의 뜻에 대한 것이라 이번 실행에서 값이 비어 있어도 목록에 남는다.
        /// </remarks>
        private static void Promises(StringBuilder text)
        {
            text.Append("\"capabilities\":[");

            for (var index = 0; index < Promised.Length; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                Json.String(text, Promised[index]);
            }

            text.Append(']');
        }

        private static readonly string[] Promised =
        {
            // `build` 가 있고 무엇이 이 문서를 만들었는지 말한다.
            "build-info-v1",

            // 모든 객체가 `selector` 를 나르고, 이번 pulse 에 한해 제 씬 안에서 유일하다.
            "selector-v1",

            // `visuals[]` 가 모든 텍스트와 그림에 역할을 주고, `label` 과 `sprite` 는 컨트롤의 이름이거나 없다.
            "visual-roles-v1",

            // `persistentObjects` 가 씬 로드 사이에 남는 객체를 담고, 관련 gap 은 읽지 못했을 때만 쓴다.
            "persistent-objects-v1"
        };

        /// <summary>
        /// 이 문서를 만든 빌드 정보를 쓴다. 두 리포트가 다를 때 게임, 분석기, 빌드 중 무엇이 바뀌었는지 가리게 한다.
        /// </summary>
        /// <remarks>
        /// 시각은 무엇이 분석됐는지 말하지 않고 모든 파일 쌍에 차이를 만들므로 넣지 않는다. 비교할 값은 <c>evidence</c> 지문이다.
        /// 씬 참조는 세션마다 바뀌는 인스턴스 id 를 쓰므로 같은 게임을 두 번 스캔해도 문서 전체는 다르다.
        /// </remarks>
        private static void Built(StringBuilder text)
        {
            text.Append("\"build\":{");
            Json.Property(text, "unity", Application.unityVersion);
            text.Append(',');
            Json.Property(text, "platform", Application.platform.ToString());
            text.Append(',');
            Json.Property(text, "backend", Backend());
            text.Append(',');
            Json.Property(text, "development", Debug.isDebugBuild);
            text.Append(',');
            Json.Property(text, "sdk", PackageVersion.Value);
            text.Append(',');
            Json.Property(text, "evidence", Fingerprint());
            text.Append('}');
        }

        private static string Backend()
        {
#if ENABLE_IL2CPP
            return "il2cpp";
#elif ENABLE_MONO
            return "mono";
#else
            return "unknown";
#endif
        }

        /// <summary>
        /// 게임의 모든 `evidence` 에 대한 FNV-1a 지문.
        /// </summary>
        /// <remarks>
        /// 이름순으로 정렬해 어셈블리 로드 순서에 영향받지 않는다. 보안용 다이제스트가 아니다.
        /// </remarks>
        private static string Fingerprint()
        {
            var named = new List<string>(AffordanceCatalog.Everything().Keys);
            named.Sort(System.StringComparer.Ordinal);

            var everything = AffordanceCatalog.Everything();
            var hash = 14695981039346656037UL;

            foreach (var name in named)
            {
                hash = Mixed(hash, name);
                hash = Mixed(hash, everything[name]);
            }

            return hash.ToString("x16");
        }

        private static ulong Mixed(ulong hash, string text)
        {
            foreach (var letter in text)
            {
                hash = (hash ^ letter) * 1099511628211UL;
            }

            return (hash ^ '\n') * 1099511628211UL;
        }

        public static string Compose()
        {
            // 아래 표와 gaps 가 함께 쓰므로 한 번만 계산한다.
            var missing = Unplaced();
            var callers = Callers();

            var text = new StringBuilder(16384);
            text.Append("{\"schema\":").Append(SchemaVersion).Append(",\"capture\":");
            Json.String(text, Capture());
            text.Append(',');
            Promises(text);
            text.Append(',');
            Built(text);
            text.Append(",\"scenes\":[");

            for (var index = 0; index < Order.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                Json.String(text, Order[index]);
            }

            text.Append("],\"types\":{");

            // 출력이 결정적이도록 정렬한다.
            var named = new List<string>(Types.Keys);
            named.Sort(System.StringComparer.Ordinal);

            for (var index = 0; index < named.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                Json.String(text, named[index]);
                text.Append(':').Append(Types[named[index]]);
            }

            // `types` 를 "씬에 있는 것"으로 읽는 쪽이 틀리지 않도록 플래그가 아니라 별도 표로 쓴다.
            text.Append("},\"unplaced\":{");

            var spent = 0;
            var written = 0;

            for (var index = 0; index < missing.Count; index++)
            {
                var entry = missing[index];

                if (spent > 0 && spent + entry.Value.Length > UnplacedBudget)
                {
                    continue;
                }

                spent += entry.Value.Length;

                if (written > 0)
                {
                    text.Append(',');
                }

                written++;
                Json.String(text, entry.Key);
                text.Append(":{\"evidence\":").Append(entry.Value);

                // 빈 목록은 죽은 코드일 수 있다는 신호다.
                text.Append(",\"createdBy\":[");

                if (Makers.TryGetValue(entry.Key, out var makers))
                {
                    for (var maker = 0; maker < makers.Count; maker++)
                    {
                        if (maker > 0)
                        {
                            text.Append(',');
                        }

                        WriteMaker(text, makers[maker]);
                    }
                }

                text.Append("],\"calledBy\":[");

                if (callers.TryGetValue(entry.Key, out var calling))
                {
                    for (var caller = 0; caller < calling.Count; caller++)
                    {
                        if (caller > 0)
                        {
                            text.Append(',');
                        }

                        Json.String(text, calling[caller]);
                    }
                }

                text.Append("]}");
            }

            _unplacedOmitted = missing.Count - written;

            text.Append("},\"objects\":[");

            var wrote = false;

            foreach (var scene in Order)
            {
                var objects = Objects[scene];

                if (string.IsNullOrEmpty(objects))
                {
                    continue;
                }

                if (wrote)
                {
                    text.Append(',');
                }

                text.Append(objects);
                wrote = true;
            }

            text.Append("],\"persistentObjects\":[").Append(_persistent).Append("],\"gaps\":[");

            var said = new HashSet<string>();
            var first = true;

            foreach (var scene in Order)
            {
                foreach (var gap in Gaps[scene])
                {
                    // 씬마다 다른 gap 을 구분하도록 씬 이름을 붙인다.
                    var scoped = scene + ":" + gap;

                    if (!said.Add(scoped))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        text.Append(',');
                    }

                    Json.String(text, scoped);
                    first = false;
                }
            }

            // 씬 로드 사이에 남는 객체는 특정 씬에 속하지 않으므로 리포트에 한 번만 쓴다.
            if (!_persistentRead)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, "dont-destroy-on-load-not-walked");
                first = false;
            }

            foreach (var gap in _walkGaps)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, gap);
                first = false;
            }

            foreach (var gap in _persistentGaps)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, "persistent:" + gap);
                first = false;
            }

            // 항목은 `unplaced` 에 있고 전체 개수만 gap 으로 남긴다.
            if (missing.Count > 0)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, "evidence-never-placed-count:" + missing.Count);
                first = false;
            }

            if (_unplacedOmitted > 0)
            {
                text.Append(',');
                Json.String(text, "unplaced-evidence-omitted:" + _unplacedOmitted);
            }

            // `evidence` 가 없는 타입을 호출하는 `wiring` 을 알린다. 대상 타입은 뒤의 씬에서 만날 수 있으므로 순회가 끝난 여기서 판단한다.
            var dangling = new List<string>();

            foreach (var type in WiredTo)
            {
                if (!Types.ContainsKey(type))
                {
                    dangling.Add("wired-target-has-no-evidence:" + type);
                }
            }

            dangling.Sort(System.StringComparer.Ordinal);

            foreach (var gap in dangling)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, gap);
                first = false;
            }

            text.Append("]}");
            return text.ToString();
        }
    }
}
