using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 방문한 모든 씬을 통틀어, 여태 본 모든 것.
    /// </summary>
    /// <remarks>
    /// 게임은 언제나 화면 하나만 보여 주고 있으므로 스캔 하나는 언제나 화면 하나에 대한 리포트다. 스캔마다 앞의 것 위에
    /// 덮어쓰면 마침 가장 최근에 로드된 씬을 서술하는 파일이 남았다 — 참이면서, 게임을 덮어야 하는 명세에는 쓸모없다.
    ///
    /// 씬으로 키를 잡아 한 화면을 두 번 방문하면 항목이 중복되는 대신 고쳐지도록 한다. 게임 뒤쪽에서 다른 상태를 뒤에 두고
    /// 닿은 씬은, 실제로 거기 있었던 마지막 때에 발견된 대로 그 화면을 서술해야 한다.
    /// </remarks>
    public static class AffordanceReport
    {
        /// <summary>
        /// 여섯: <c>label</c> 이 뜻하던 것을 그만두었다.
        /// </summary>
        /// <remarks>
        /// 이전의 모든 버전은 늘어났다. 여섯은 좁아진 첫 번째다: <c>label</c> 은 객체가 보여 주는 그 한 가지였고 이제는 플레이어가
        /// 누를 수 있는 것 위에 쓰인 것이다. 그래서 옛 뜻을 알던 독자가 새 문서를 받으면 적의 남은 체력을 컨트롤의 이름으로 읽는다.
        /// 샘플 게임의 스물둘 중 열여섯이 정확히 그것이었다.
        ///
        /// 독자가 알아보지 못할 때 거절하는 숫자가 그렇다고 말할 옳은 자리다. 여기서는 요란하게 거절하는 편이 옳다 — 대안은 다섯에
        /// 그대로 두고, 차이를 가리지 못하는 독자들이 조용히 계속 틀리게 두는 것이었다.
        ///
        /// <c>capabilities</c> 가 그 옆에서 다른 일을 한다: 숫자는 문서가 어느 세대에 속하는지를 말하고 목록은 그것이 어떤 약속을
        /// 하는지를 말하므로, 아무 뜻도 바꾸지 않는 나중의 추가는 누구에게도 문을 닫지 않고 알릴 수 있다. 여섯은 <c>build</c>,
        /// <c>selector</c>, <c>visuals</c>, <c>persistentObjects</c> 도 함께 들여오는데 전부 더하기만 한다.
        ///
        /// 둘은 근거를 컴포넌트 밖으로 빼 제 표로 옮겼고, 셋은 각 컴포넌트의 인스펙터 필드가 가리키는 것을 더했다.
        ///
        /// 넷은 <c>types</c> 옆에 <c>unplaced</c> 를 더한다. 독자가 알던 모든 것은 있던 자리에 뜻하던 대로 남는다 —
        /// <c>types</c> 는 여전히 GameObject 위에서 만난 것뿐이다 — 그것이 첫 표 안의 플래그가 아니라 두 번째 표인 이유다.
        /// <c>unplaced</c> 를 모르는 독자는 그것에 속을 수 없고, 아는 독자는 실행이 결코 닿지 못한 타입의 규칙과 함께 그것들이
        /// 존재하려면 무엇이 일어나야 하는지를 얻는다.
        ///
        /// 다섯은 <c>createdBy</c> 옆에 <c>calledBy</c> 를, 그리고 아무도 읽을 수 없던 조건에 <c>unread</c> 를 더한다. 둘 다
        /// 더하기만 하고 그것들을 무시하는 독자는 전에 읽던 것을 읽는다 — 그래도 버전은 움직인다. 숫자를 기준으로 삼는 독자는
        /// 모양이 늘었다는 것을 발견하는 것이 아니라 들어야 하기 때문이다.
        /// 일곱은 <c>createdBy</c> 의 <b>항목 타입을 바꾼다</b> — 문자열에서 객체로. 여섯까지의 변화가 전부 더하기였던 것과
        /// 다르다. 문자열 하나로는 두 항목이 같은 프리팹인지 답할 수 없었고, 실측에서
        /// <c>MagicEnemy.fireShoot</c> 와 <c>BossEnemy.fireShoot</c> 는 서로 다른 프리팹이었다.
        ///
        /// 같은 세대에서 <c>cut</c> 이 붙은 항목이 생긴다. 걷기가 깊이에 막혀 읽지 못한 프리팹이고, 이것이 없으면 빈
        /// <c>createdBy</c> 가 "아무도 만들지 않는다"와 "우리가 못 걸어갔다" 둘 다를 뜻해 <b>살아 있는 타입이 폐기로
        /// 적재된다</b>. 버린 자리는 <c>gaps</c> 에도 남는다.
        /// </remarks>
        public const int SchemaVersion = 7;

        /// <summary>더 오래된 것이 갈아치워지기를 그만두기 전까지 몇 개의 씬을 쥐고 있는지.</summary>
        private const int MaxScenes = 256;

        /// <summary>
        /// 한 번도 놓이지 않은 근거를 리포트가 몇 바이트까지 나를 것인지.
        /// </summary>
        /// <remarks>
        /// 개수가 아니라 예산인 것은, 값을 치르게 하는 것이 타입의 수가 아니라 근거이기 때문이다. 샘플 프로젝트에서 실측하니
        /// 무관한 패키지에 속한 컴포넌트 둘만으로 2 메가바이트였고 게임 자신의 놓이지 않은 근거 전체는 그 10분의 1이었다 —
        /// 분석기는 주어진 모든 어셈블리를 굽고 그중 무엇이 게임인지 알 수 없다.
        ///
        /// 가장 값진 항목부터 쓴다: 씬 안의 무언가가 그것을 만든다고 알려진 것들, 그다음 작은 것들. 그래야 예산으로 살 수 있는
        /// 타입이 가장 많아진다. 들어가지 못한 것은 공백에 센다.
        /// </remarks>
        private const int UnplacedBudget = 512 * 1024;

        private const int MaxMakers = 8;

        /// <summary>분석기가 호출 대상을 적는 방식: 어셈블리, 타입, 메서드, 시그니처.</summary>
        private const string TargetMarker = "\"targetId\":\"";

        private static readonly Dictionary<string, List<Maker>> Makers =
            new Dictionary<string, List<Maker>>(System.StringComparer.Ordinal);

        /// <summary>
        /// 어셈블리가 근거를 나르지만 어느 씬도 쥐고 있는 것으로 발견되지 않은 타입들.
        /// </summary>
        /// <remarks>
        /// 매달린 배선과 같은 이유로 끝에 묻는다: 어떤 타입을 만났는지는 순회가 끝나야 비로소 결정된다.
        ///
        /// 이름이 아니라 문서를 비교해 정한다. 카탈로그의 키는 타입이 컴파일될 때 가지고 있던 이름이고 스캔이 만난 것은 그 뒤에
        /// 이름이 바뀌었을 수 있다 — 그러지 않으면 난독화된 빌드는 만난 모든 타입을 한 번도 놓이지 않은 것으로 보고하게 된다.
        /// 한 타입에 대한 두 항목은 그때 무엇이라 불리든 같은 문서를 나른다.
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

            // 예산이 무엇을 먼저 사야 하는지로 정렬한다 — 씬 안의 무언가가 그것을 만든다고 알려진 것, 그다음 가장 작은 것 —
            // 그리고 마지막으로 이름으로 정렬해 두 실행이 바이트까지 일치하게 한다.
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
        /// 어떤 타입들이 각 타입을 호출하는가. 분석기가 이미 구워 둔 근거에서 읽어낸다.
        /// </summary>
        /// <remarks>
        /// 어느 씬도 쥐고 있지 않고, 아무것도 만들지 않으며, 아무것도 부르지 않는 타입은 죽은 코드다. 앞의 둘은 이미 알려져
        /// 있었고 충분하지 않았다: 샘플 게임은 대체된 네임스페이스 하나를 통째로 나르는데, 그 타입들은 실행이 아직 이르지 못한
        /// 타입과 똑같이 읽혔고 그 규칙들이 게임의 규칙으로 적히고 있었다.
        ///
        /// 파싱하지 않고 문서 텍스트에서 읽어낸다. 찾는 것이 호출 대상뿐이고, 문서는 어차피 문자열로 쥐고 있으며, 여기에 파서를
        /// 두는 것은 이미 독자가 있는 형식의 두 번째 독자를 두는 일이다.
        ///
        /// 문서가 아니라 이름이다 — 이름이 바뀐 타입도 알아봐야 해서 문서를 비교하는 <see cref="Unplaced"/> 와는 다르다.
        /// 여기서는 양쪽이 같은 구워진 근거에서 나오므로, 둘 다 컴파일러가 본 이름을 나르고 서로 일치한다.
        ///
        /// 불린다고 해서 타입이 살아 있는 것은 아니다: 그 호출자도 죽었을 수 있다. 플래그로 줄이지 않고 호출자를 나열하는 이유가
        /// 그것이다 — 독자는 그것들이 이 같은 표 안에 있는지를 스스로 볼 수 있다.
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
        /// 씬 안의 무언가가 쥔 프리팹이 이 타입을 나른다고 적어 둔다.
        /// </summary>
        /// <remarks>
        /// 아무도 만들지 않는 타입과 실행이 아직 이르지 못한 타입을 가르는 유일한 사실이다. 그것이 없으면 리포트에는 한 번도 보지
        /// 못한 타입 목록만 있고 그중 무엇이 죽은 코드인지 가릴 방법이 없다 — 그리고 죽은 타입의 규칙을 게임의 규칙으로 발행하는
        /// 것이, 이 표가 그냥 거짓인 명세를 만들어낼 수 있는 유일한 길이다.
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
        /// 걷기가 깊이에 막힌 자리에서 만난 프리팹을 적어 둔다.
        /// </summary>
        /// <remarks>
        /// 반환하는 그 순간에도 프리팹은 손에 있었다 — 읽을 수 없어서 없는 것이 아니라 이미 본 것을
        /// 안 적는 것이었다. 여기 남기지 않으면 <c>createdBy</c> 가 비고, 소비자는 그것을 죽은
        /// 코드로 읽는다. <b>빈 목록은 아무도 만들지 않는다는 뜻이어야 한다.</b>
        ///
        /// <c>cut</c> 이 말하는 것은 "이 프리팹을 못 봤다"가 아니라 <b>"이 프리팹 뒤로 더 걷지
        /// 않았다"</b>이다. 그 너머에 또 다른 프리팹이 있었다면 그것은 여전히 이 리포트에 없다.
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
                // 8 이라는 숫자만으로는 잘렸는지 알 수 없다. 실측에서 SpellObj 는 여덟이 적히고
                // 일곱이 사라졌으며, 사라졌다는 표시가 없었다.
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
        /// 문자열 하나였을 때는 두 항목이 같은 프리팹인지 다른 프리팹인지 리포트만 봐서는 답이
        /// 없었다 — 실측에서 <c>MagicEnemy.fireShoot</c> 와 <c>BossEnemy.fireShoot</c> 는 서로
        /// 다른 프리팹(YellowProjectile / LightGreenProjectile)이었다.
        ///
        /// <see cref="PrefabId"/> 는 <c>refs[].id</c> 와 같은 값이라 리포트 한 벌 안에서 프리팹
        /// 단위 조인이 성립한다. 실행 밖에서는 뜻이 없다 — 실행을 넘는 지문은 이름과
        /// <c>carries</c> 이고, 그 조인은 소비자 몫이다.
        /// </remarks>
        internal struct Maker
        {
            /// <summary>씬 안에서 이 프리팹을 쥔 필드. 깊이에 막힌 항목은 출발 필드다.</summary>
            public string Field;
            public string Prefab;
            public int PrefabId;

            /// <summary>걷기가 왜 멈췄나. null 이면 끝까지 읽었다는 뜻이다.</summary>
            public string Cut;
        }

        private static readonly List<string> Order = new List<string>();
        private static readonly Dictionary<string, string> Objects = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<string>> Gaps = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>();

        /// <summary>리포트가 무언가 할 말이 있는 씬들.</summary>
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

        /// <summary>게임이 씬 로드를 건너 쥐고 있던 것. 리포트 전체에 대해 한 번 읽는다.</summary>
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
        /// 걷기가 버린 것. 씬이 아니라 프리팹과 타입에 대한 사실이라 화면 하나에 귀속시킬 수 없다.
        /// </summary>
        /// <remarks>
        /// 같은 프리팹이 여러 씬에서 같은 한계에 걸리므로 집합으로 든다 — 같은 말을 씬 수만큼
        /// 되풀이하면 읽는 쪽이 그 수를 빈도로 오해한다.
        /// </remarks>
        private static readonly HashSet<string> _walkGaps = new HashSet<string>();

        private static void WalkGap(string gap) => _walkGaps.Add(gap);

        /// <summary>이미 읽은 씬에 대해 할 말을 하나 더한다.</summary>
        internal static void Note(string scene, string gap)
        {
            var name = string.IsNullOrEmpty(scene) ? "(unnamed)" : scene;

            if (Gaps.TryGetValue(name, out var already) && !already.Contains(gap))
            {
                already.Add(gap);
            }
        }

        /// <summary>
        /// 한 씬이 그것을 몇 개나 쥐고 있든, 타입의 근거가 말하는 바를 한 번 기록한다.
        /// </summary>
        /// <remarks>
        /// 그 타입을 처음 만났을 때 듣고 다시 묻지 않는다. 근거는 컴파일 시점에 타입 위에 구워지고 그 두 인스턴스 사이에서 다를 수
        /// 없다.
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
        /// 씬의 배선이 호출해 들어가는 타입을 적어 둔다.
        /// </summary>
        /// <remarks>
        /// 그것에 근거가 있는지는 여기서 답할 수 없다 — 그 타입을 아직 만나지 못했을 수 있고, 그것이 사는 자리가 뒤의 씬일 수
        /// 있다. 대신 방문할 씬을 전부 방문한 끝에 묻는다.
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

        /// <summary>다시 시작한다. 순회가 이전 순회의 답을 들고 오지 않도록.</summary>
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
        /// 씬이 읽힐 무렵 무엇이든 돌아 있었는지.
        /// </summary>
        /// <remarks>
        /// 리포트의 대부분은 코드에 대한 것이고 언제 읽히든 성립한다. 객체가 무엇을 보여 주고 있었는지는 그렇지 않다: 에디터
        /// 순회는 씬을 열어 저장된 대로 읽고, 플레이어는 <c>Awake</c> 와 <c>Start</c> 와 순회 전의 플레이를 거쳐 왔다. 같은
        /// 필드가 한쪽에서는 "씬이 말하는 것" 을 뜻하고 다른 쪽에서는 "그 순간 그것이 말한 것" 을 뜻한다 — 적의 라벨은 한쪽에서
        /// 작성된 <c>20</c> 이고 다른 쪽에서 남은 체력이다.
        ///
        /// 파일 이름을 지은 쪽에 맡기지 않고 문서 안에 적는다. 리포트 하나를 쥔 독자는 다른 방법으로는 알 수 없고, 한순간을
        /// 규칙으로 읽는 것이 다음번에는 거기 없을 숫자에 대고 테스트가 쓰이는 방식이기 때문이다.
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
        /// 무엇이 이 문서를 만들었는가. 둘을 가릴 수 있도록 말한다.
        /// </summary>
        /// <remarks>
        /// 어디서 왔는지 말하지 않는 리포트는 따질 수가 없다. 두 파일이 어긋나는데 게임이 바뀐 것인지, 분석기가 바뀐 것인지,
        /// 둘 중 하나가 다른 빌드에서 나온 것인지 아무도 가릴 수 없다 — 지금까지 독자가 놓여 있던 자리가 그것이고, 이 문서들에
        /// 대한 리뷰가 그 출처를 확인할 수 없다고 말해야 했던 이유다.
        ///
        /// 시계도 세션 번호도 일부러 두지 않는다. 시각은 그 물음에 답하지 않고 — "언제" 는 무엇이 분석됐는지를 말하지 않는다 —
        /// 그것은 독자가 그다음 넘겨봐야 할 차이를 모든 파일 쌍에 넣게 된다. 대신 <c>evidence</c> 가 답한다: 구워진 문서들
        /// 자체의 지문이라, 같은 게임을 같은 분석기가 읽으면 같은 값이 나오고 어느 한쪽이 바뀌면 다른 값이 나온다. 두 파일을
        /// 비교해야 할 숫자가 그것이다.
        ///
        /// 이것이 무엇을 안정되게 만들지 않는지도 말할 값이 있다. 바뀌지 않은 게임을 두 번 스캔해도 여전히 다르다. 씬 참조는
        /// Unity 가 준 인스턴스 id 로 쓰이고 그것은 세션마다 새로 나눠 주기 때문이다. 근거는 — 코드에서 읽어낸 모든 것 —
        /// 양쪽 다 같은 바이트이고, 그것이 Mono 빌드와 IL2CPP 빌드가 일치한다고 보인 방법이다. 같은 객체에 대해 씬 쪽 절반이
        /// 쓰는 것은 그 한 필드에서 다를 수 있다.
        /// </remarks>
        /// <summary>
        /// 이 문서의 필드들이 무엇을 뜻하기로 약속돼 있는가.
        /// </summary>
        /// <remarks>
        /// 버전 번호는 이것을 나를 수 없었다. 독자는 모르는 숫자를 거절하므로, 한 필드의 뜻이 좁아졌다고 말하려고 번호를 올리면
        /// 모든 독자에게 한꺼번에 문을 닫게 된다 — 그렇다고 그대로 두면 모양이 늘어나기만 했다고 말하는 셈인데, <c>label</c> 에
        /// 대해서는 그것이 참이 아니었다. 그것은 "이 객체가 보여 준 그 한 가지" 에서 "플레이어가 누를 수 있는 것 위에 쓰인 것" 으로
        /// 옮겨 갔고, 그 변경 전에 쓰인 문서는 그 뒤에 쓰인 것과 똑같아 보인다.
        ///
        /// 한 필드가 다른 필드를 대신할 수도 없다. <c>build</c> 는 역할들보다 한 커밋 먼저 도착했으므로, 그것을 나르는 리포트도
        /// 여전히 옛 <c>label</c> 을 뜻할 수 있다. 있는 것으로부터 계약을 유추하는 독자는 그 짝을 틀리게 본다.
        ///
        /// 그래서 약속마다 이름을 붙이고 독자는 제게 필요한 것을 청한다. 어떤 실행이 마침 무엇을 찾았는지는 여기 들지 않는다:
        /// 로드를 건너 아무것도 쥐지 못한 플레이어 스캔도 여전히 <c>persistent-objects-v1</c> 이라고 말한다. 그 주장은 무엇이
        /// 있었다면 그 필드가 무엇을 뜻했을지에 대한 것이기 때문이다.
        /// </remarks>
        /// <summary>
        /// <c>createdBy</c> 한 항목을 쓴다.
        /// </summary>
        /// <remarks>
        /// <c>cut</c> 이 붙은 항목은 그 프리팹 뒤를 걷지 않았다는 뜻이다. <c>carries</c> 가 없는
        /// 것은 결함이 아니라 사실이며, 읽는 쪽이 그것을 "컴포넌트가 없다"로 읽으면 안 된다.
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

            // 모든 객체가 `selector` 를 나르고, 이번 판독에 한해 제 씬 안에서 유일하다.
            "selector-v1",

            // `visuals[]` 가 모든 텍스트와 그림에 역할을 주고, `label` 과 `sprite` 는 컨트롤의 이름을 대거나 아예 없다 — 더는
            // 객체가 마침 보여 준 무엇이 아니다.
            "visual-roles-v1",

            // `persistentObjects` 가 게임이 씬 로드를 건너 쥐고 있던 것을 담고, 아무도 보지 않았다고 말하던 공백은 아무도 볼 수
            // 없었을 때만 쓰인다.
            "persistent-objects-v1"
        };

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
            Json.Property(text, "sdk", PackageVersion);
            text.Append(',');
            Json.Property(text, "evidence", Fingerprint());
            text.Append('}');
        }

        /// <summary>`package.json` 과 손으로 맞춘다. 읽어 올 다른 자리가 없다.</summary>
        private const string PackageVersion = "0.1.0";

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
        /// 이 게임 위에 구워진 모든 문서를 대표하는 숫자 하나.
        /// </summary>
        /// <remarks>
        /// 읽기 전에 정렬하므로 어셈블리가 마침 로드되는 순서가 답을 바꾸지 않는다. 보안 다이제스트가 아니고 그렇다고 주장하지도
        /// 않는다 — 분석 둘을 가리려고 여기 있고, 값싼 섞기 함수로 그 일은 된다.
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
            // 한 번 알아낸다: 아래 표가 그것을 쓰고 공백 목록이 그것을 센다.
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

            // 같은 게임을 두 번 돌면 같은 바이트가 나오도록 정렬한다. 씬 순서는 게임이 걸어간 자리를 따르지만, 이것은 따를 그런
            // 순서가 없다.
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

            // 어셈블리가 아는 것 중 어느 씬도 쥐고 있는 것으로 발견되지 않은 전부. 위 표 안의 플래그가 아니라 제 표인 것은,
            // `types` 를 "화면에 있는 것" 으로 읽는 무엇도 그것 때문에 조용히 틀리지 않도록 하기 위해서다.
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

                // 누가 그것을 만들어야 하는가. 씬 *안에 있는* 무언가가 쥔 프리팹은 하나의 입구다. 아무것도 없다는 것은 죽은 코드의
                // 모양이고, 그 둘이 똑같이 읽혀서는 안 된다.
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
                    // 씬 단위로 묶는다. 한 화면에는 해당되고 다른 화면에는 해당되지 않는 공백은 어디에나 해당되는 공백과 다른 사실이고,
                    // 둘을 합치면 어느 쪽인지를 잃는다.
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

            // 화면마다가 아니라 리포트에 대해 한 번 말한다. 씬 로드를 건너 쥐고 있는 객체는 어느 화면에도 없으면서 모든 화면에
            // 있고, 한 번도 돌지 않은 순회는 그것들에 닿았을 수 없다.
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

            // 개수는 리포트 전체에 대한 사실이므로 공백에 남는다. 규칙 자체는 위의 제 표로 갔다.
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

            // 근거가 없는 타입의 메서드에 연결된 버튼은 살아 있는 것처럼 보이는 막다른 길이다: 호출은 리포트에 있고 그것이 부르는
            // 것은 없다. 씬마다가 아니라 여기서 말하는 것은, 타입이 알려졌는지가 순회가 끝나야 결정되기 때문이다 — 그것을 나르는
            // 객체가 나중에 방문할 씬에 있을 수도, 어느 씬에도 없을 수도 있다.
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
