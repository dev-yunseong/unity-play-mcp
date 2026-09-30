using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityPlayMcp.Affordances.Scan;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 감시 대상 멤버의 현재 값을 리포트와 같은 이름으로 pulse 문서에 쓴다.
    /// </summary>
    /// <remarks>
    /// static 값은 <c>statics</c> 목록에, 인스턴스 값은 그것을 가진 객체의 경로 아래에 쓴다. 객체마다 값이 다르므로 합치거나
    /// 고르지 않는다.
    ///
    /// 값은 해석하지 않는다. 필드 값을 그대로 쓰고 분석이 선언한 타입을 붙인다.
    /// </remarks>
    internal static class LiveState
    {
        /// <summary>감시 대상 멤버 하나를 읽는 최대 객체 수. 넘는 객체는 버린다.</summary>
        /// <remarks>
        /// 풀링된 발사체처럼 수백 개가 생기면 문서가 커지고 매 pulse 가 변화로 판정된다. 버린 수는 그 멤버에 적는다.
        /// </remarks>
        private const int MaxHolders = 16;

        /// <summary>객체마다 <c>ledger</c> 에 하나씩 두는 key. 사라진 객체를 찾는 데 쓴다.</summary>
        private const string Active = "|active";

        /// <summary>
        /// 감시 대상 멤버를 전부 읽고 문서 하나를 쓴다.
        /// </summary>
        /// <remarks>
        /// 타입마다 계층을 찾지 않고 씬을 한 번 walk 하며 모든 컴포넌트를 대조한다. 계층 walk 가 비용의 큰 부분이다.
        /// </remarks>
        internal static string Compose(
            long reading, Scene persistent, Restless restless, Restless pixels,
            Dictionary<string, string> since, bool repair, out bool settled)
        {
            var watched = WatchList.All();
            var now = new Dictionary<string, string>(since.Count, StringComparer.Ordinal);
            var moved = new List<string>();
            var byOwner = new Dictionary<Type, List<Watched>>();
            var statics = new List<Watched>();

            foreach (var member in watched)
            {
                if (member.Static)
                {
                    statics.Add(member);
                    continue;
                }

                if (!byOwner.TryGetValue(member.Owner, out var list))
                {
                    list = new List<Watched>();
                    byOwner[member.Owner] = list;
                }

                list.Add(member);
            }

            var active = SceneManager.GetActiveScene();
            var text = new StringBuilder(4096);

            text.Append("{\"schema\":").Append(Pulse.SchemaVersion);

            // 화면 캡처 등 다른 관측과 같은 시점인지 맞춰 볼 수 있도록 게임의 frame 번호를 싣는다.
            text.Append(",\"reading\":").Append(reading);
            text.Append(",\"frame\":").Append(Time.frameCount);

            text.Append(",\"scene\":");
            Json.String(text, active.IsValid() ? active.name : null);

            // whole pulse 를 보내는 경우: 첫 pulse, 씬이 바뀔 때, 전달 실패나 요청 뒤(repair). 나머지는 움직인 값만 싣는다.
            // 놓친 차이는 다른 차이로 복구되지 않으므로 whole 로 한 번 보낸다.
            since.TryGetValue("scene", out var was);

            var everything = repair || since.Count == 0 ||
                             was != (active.IsValid() ? active.name : null);

            var ledger = new Ledger
            {
                Restless = restless, Pixels = pixels, Since = since, Now = now, Moved = moved,
                Everything = everything
            };

            // 다른 값이 모두 같아도 씬이 바뀌면 변화로 본다.
            ledger.Say("scene", active.IsValid() ? active.name : null);

            text.Append(",\"statics\":[");
            Statics(text, statics, ledger);
            text.Append(']');

            var showing = new Bin();
            var hidden = new Bin();

            // Camera.main 은 태그로 씬 전체를 조회하므로 객체마다가 아니라 pulse 마다 한 번 구한다.
            ScreenArea.Begin();

            var truncated = Objects(
                persistent, active.IsValid() ? active.name : null, byOwner, ledger, showing, hidden);

            ScreenArea.Forget();

            showing.WriteTo(text, "active");
            hidden.WriteTo(text, "deactive");

            // 읽는 쪽이 차이 pulse 와 whole pulse 를 구분해야 상태를 잘못 버리거나 남기지 않는다.
            text.Append(",\"whole\":").Append(everything ? "true" : "false");

            text.Append(",\"watching\":").Append(watched.Count);
            text.Append(",\"unresolved\":").Append(WatchList.Unresolved);
            text.Append(",\"unwatchable\":").Append(WatchList.Unwatchable);

            if (truncated > 0)
            {
                text.Append(",\"gaps\":[\"holder-limit:").Append(truncated).Append("\"]");
            }

            // 직전 pulse 에 있고 이번에 없는 key 도 변화로 센다. 그러지 않으면 객체가 파괴되거나 씬을 떠날 때 변화가 없다고
            // 판정된다.
            foreach (var pair in since)
            {
                if (!now.ContainsKey(pair.Key))
                {
                    moved.Add(pair.Key);
                }
            }

            // `changed` 의 key 는 "값이 바뀌었다" 로 읽히므로, 객체가 사라진 것은 `gone` 으로 따로 싣는다.
            var gone = Gone(since, now, everything, truncated);

            if (gone != null)
            {
                text.Append(",\"gone\":[");

                for (var at = 0; at < gone.Count; at++)
                {
                    if (at > 0)
                    {
                        text.Append(',');
                    }

                    Json.String(text, gone[at]);
                }

                text.Append(']');
            }

            text.Append(",\"changed\":[");
            moved.Sort(StringComparer.Ordinal);

            for (var at = 0; at < moved.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                Json.String(text, moved[at]);
            }

            text.Append("]}");

            settled = moved.Count == 0;

            since.Clear();

            foreach (var pair in now)
            {
                since[pair.Key] = pair.Value;
            }

            return text.ToString();
        }

        /// <summary>
        /// pulse 의 <c>active</c>/<c>deactive</c> 목록 중 하나.
        /// </summary>
        /// <remarks>
        /// 꺼진 객체도 확인 대상이라 싣는다. 활성 여부는 필드가 아니라 목록으로 나눠 읽는 쪽이 필터링하지 않게 한다.
        /// </remarks>
        private sealed class Bin
        {
            private readonly StringBuilder _text = new StringBuilder(1024);
            private int _written;

            internal void Add(StringBuilder said)
            {
                if (_written > 0)
                {
                    _text.Append(',');
                }

                _text.Append(said);
                _written++;
            }

            internal void WriteTo(StringBuilder text, string name)
            {
                text.Append(",\"").Append(name).Append("\":[").Append(_text).Append(']');
            }
        }

        /// <summary>이번 pulse 의 값, 직전 pulse 의 값, 그 차이.</summary>
        private sealed class Ledger
        {
            internal Restless Restless;

            /// <summary>화면 좌표용 <see cref="Restless"/>. 경계는 픽셀이다.</summary>
            /// <remarks>
            /// 월드 경계 0.001 은 픽셀에서는 아무것도 거르지 못하고, 사각형은 모든 객체에 붙으므로 따로 둔다.
            /// </remarks>
            internal Restless Pixels;

            internal Dictionary<string, string> Since;
            internal Dictionary<string, string> Now;
            internal List<string> Moved;

            /// <summary>
            /// 값을 key 아래에 기록하고, 직전과 다르면 <c>Moved</c> 에 넣는다.
            /// </summary>
            /// <remarks>
            /// key 는 멤버 이름이 아니라 값이 있는 위치(객체 경로)를 포함하므로 두 객체의 같은 필드는 따로 판정된다.
            /// </remarks>
            internal bool Say(string key, string value)
            {
                Now[key] = value;

                if (Since.TryGetValue(key, out var before) && before == value)
                {
                    return false;
                }

                Moved.Add(key);
                return true;
            }

            /// <summary>
            /// 이 pulse 가 움직인 값만이 아니라 전부를 싣는지(<c>whole</c>).
            /// </summary>
            /// <remarks>
            /// 읽을 수 있는 멤버 전부를 매 pulse 보내면 트래픽이 커서 평소에는 차이만 싣는다. 차이만으로는 처음 값이나 놓친 값을
            /// 알 수 없으므로 첫 pulse, 씬 전환, 복구 요청 때 전부 싣는다.
            /// </remarks>
            internal bool Everything;

            /// <summary>값을 기록하고, 이번 pulse 에 실을지 돌려준다.</summary>
            internal bool Keep(string key, string value)
            {
                return Say(key, value) | Everything;
            }
        }

        /// <summary>직전 pulse 에 있었고 이번 walk 에서 만나지 못한 객체의 경로.</summary>
        /// <remarks>
        /// 파괴된 객체는 <c>active</c>/<c>deactive</c> 어디에도 실리지 않아, 알려 주지 않으면 읽는 쪽이 마지막 값을 계속 쥔다.
        ///
        /// 객체마다 <c>|active</c> key 를 기록하므로 멤버가 없는 객체도 이 key 가 사라진 것으로 찾는다.
        ///
        /// 잘린 pulse 에서는 null 을 돌려준다. 한도 때문에 walk 하지 못한 객체를 사라졌다고 하면 읽는 쪽이 살아 있는 객체를
        /// 지운다. whole pulse 에서도 읽는 쪽이 전부 교체하므로 필요 없다.
        ///
        /// statics 는 watch list 에서 오므로 key 가 사라지지 않고, 소유자가 없으면 값이 <c>null</c> 이 된다.
        /// </remarks>
        internal static List<string> Gone(
            Dictionary<string, string> since, Dictionary<string, string> now, bool everything, int truncated)
        {
            if (everything || truncated > 0)
            {
                return null;
            }

            List<string> gone = null;

            foreach (var pair in since)
            {
                if (now.ContainsKey(pair.Key) || !pair.Key.EndsWith(Active, StringComparison.Ordinal))
                {
                    continue;
                }

                if (gone == null)
                {
                    gone = new List<string>();
                }

                gone.Add(pair.Key.Substring(0, pair.Key.Length - Active.Length));
            }

            if (gone != null)
            {
                gone.Sort(StringComparer.Ordinal);
            }

            return gone;
        }

        private static void Statics(StringBuilder text, List<Watched> statics, Ledger ledger)
        {
            var written = 0;

            foreach (var member in statics)
            {
                var said = new StringBuilder(96);

                said.Append('{');
                Json.Property(said, "declaring", member.Declaring);
                said.Append(',');
                Json.Property(said, "member", Named(member));
                said.Append(',');
                Json.Property(said, "type", member.Type);
                said.Append(',');

                if (!Value(said, member, null, ledger, member.Key))
                {
                    continue;
                }

                said.Append('}');

                if (written > 0)
                {
                    text.Append(',');
                }

                text.Append(said);
                written++;
            }
        }

        /// <summary>
        /// 쓸 객체를 경로 아래에 쓴다.
        /// </summary>
        /// <remarks>
        /// 리포트와 같은 규칙(<see cref="Worth"/>)으로 객체를 고른다. 더 좁으면 리포트가 이름 댄 객체가 pulse 에 없다.
        /// watch list 는 무엇을 읽을지만 정하고 무엇을 방문할지는 정하지 않는다.
        ///
        /// 로드된 모든 씬과 DontDestroyOnLoad 씬을 walk 한다. 로드된 씬의 비활성 객체도 읽는다. 없는 버튼과 꺼진 버튼은
        /// 화면에서 구분되지 않는다.
        /// </remarks>
        private static int Objects(
            Scene persistent, string top, Dictionary<Type, List<Watched>> byOwner, Ledger ledger,
            Bin showing, Bin hidden)
        {
            var seen = new Dictionary<Type, int>();
            var dropped = 0;

            for (var at = 0; at < SceneManager.sceneCount; at++)
            {
                var scene = SceneManager.GetSceneAt(at);

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                dropped += In(scene, top, byOwner, seen, ledger, showing, hidden);
            }

            // DontDestroyOnLoad 씬은 sceneCount 에 포함되지 않아 따로 walk 한다. 씬을 넘어 유지되는 게임 상태가 여기 있다.
            if (persistent.IsValid() && persistent.isLoaded)
            {
                dropped += In(persistent, top, byOwner, seen, ledger, showing, hidden);
            }

            return dropped;
        }

        private static int In(
            Scene scene,
            string top,
            Dictionary<Type, List<Watched>> byOwner,
            Dictionary<Type, int> seen,
            Ledger ledger,
            Bin showing,
            Bin hidden)
        {
            var dropped = 0;
            var roots = scene.GetRootGameObjects();

            // 가려짐은 뒤에 그려지는 요소를 알아야 판정되므로 walk 와 쓰기를 나눈다. walk 는 여전히 한 번이다.
            var walked = new List<Transform>();
            var from = new List<int>();

            for (var index = 0; index < roots.Length; index++)
            {
                if (roots[index] == null || roots[index].hideFlags != HideFlags.None)
                {
                    // pulse 자신의 carrier 같은 숨겨진 객체는 게임이 아니므로 뺀다.
                    continue;
                }

                foreach (var transform in roots[index].GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    var admitted = Worth.Writing(transform.gameObject, byOwner);

                    if (admitted == Worth.Admitted.No)
                    {
                        continue;
                    }

                    // 화면 요소는 별도 예산으로 센다. `seen` 의 상한은 모든 씬을 합쳐 pulse 당 256 개라, 같이 세면 canvas 하나가
                    // 예산을 다 써서 evidence 객체가 밀려난다.
                    var kind = admitted == Worth.Admitted.Drawn
                        ? typeof(Drawn)
                        : transform.gameObject.GetType();

                    seen.TryGetValue(kind, out var already);
                    seen[kind] = already + 1;

                    if (already >= MaxHolders * MaxHolders)
                    {
                        dropped++;
                        continue;
                    }

                    walked.Add(transform);
                    from.Add(index);
                }
            }

            var sight = Sight.Survey(walked, Screen.width, Screen.height);

            for (var at = 0; at < walked.Count; at++)
            {
                var said = new StringBuilder(256);

                if (!Object(said, walked[at], scene, top, from[at], byOwner, ledger, sight))
                {
                    continue;
                }

                // 활성 여부는 들어가는 목록이 나타내므로 플래그를 따로 싣지 않는다. 활성 여부가 바뀌면 그 자체가 차이라 객체가
                // 이번 pulse 에 실린다.
                (walked[at].gameObject.activeInHierarchy ? showing : hidden).Add(said);
            }

            return dropped;
        }

        /// <summary>객체 하나의 위치, 보임 여부, 멤버 값을 쓴다.</summary>
        /// <remarks>
        /// 리포트와 같이 컴포넌트가 아니라 객체마다 기록 하나를 쓴다.
        /// </remarks>
        /// <returns>이 객체에 대해 pulse 에 실을 것이 있으면 true.</returns>
        private static bool Object(
            StringBuilder into,
            Transform transform,
            Scene scene,
            string top,
            int rootIndex,
            Dictionary<Type, List<Watched>> byOwner,
            Ledger ledger,
            Dictionary<int, string> sight)
        {
            var selector = ScenePath.SelectorOf(transform, rootIndex);

            // 같은 이름의 복제 객체는 경로를 공유하므로 selector 로 key 를 잡는다. 경로로 잡으면 서로 덮어써 매 pulse 가
            // 변화로 판정된다.
            var identity = scene.name + "/" + selector;

            // 멤버를 읽은 뒤에야 객체를 쓸지 알 수 있으므로 별도 버퍼에 쓴다. 바뀐 값이 없는 객체는 싣지 않는다.
            var text = new StringBuilder(256);

            // id, path, selector 는 매 기록에 쓴다. 차이만 받는 쪽이 이것 없이는 대상을 특정하거나 액션을 보낼 수 없다.
            // instance id 는 프로세스를 넘어 유지되지 않으므로 selector 도 싣는다.
            text.Append('{');

            // 문서 최상위의 씬과 다른 씬(DontDestroyOnLoad 등)의 객체만 씬 이름을 싣는다.
            if (scene.name != top)
            {
                Json.Property(text, "scene", scene.name);
                text.Append(',');
            }

            text.Append("\"id\":").Append(transform.gameObject.GetInstanceID());
            text.Append(',');
            Json.Property(text, "path", ScenePath.Of(transform));
            text.Append(',');
            Json.Property(text, "selector", selector);

            // 활성 여부는 문서가 아니라 ledger 에만 기록한다. 문서에서는 목록이 나타내고, ledger 는 바뀔 때 객체를 싣기 위해 필요하다.
            var live = transform.gameObject.activeInHierarchy;

            var flipped = ledger.Keep(identity + Active, live ? "true" : "false");
            var moved = flipped;

            // 꺼진 객체는 화면에 없고 조작할 수 없으므로 멤버 값을 싣지 않는다(풀에서 대기하는 객체가 문서를 키운다).
            // 대신 켜지는 pulse 에서 값을 전부 싣는다. 그러지 않으면 꺼진 동안 안 바뀐 값은 읽는 쪽에 영영 전달되지 않는다.
            var silent = !live && !flipped;
            var everything = live && flipped;

            moved |= Tagged(text, transform, ledger, identity);

            moved |= Where(text, transform, ledger, identity);

            moved |= Sighted(text, transform, ledger, identity, sight);

            moved |= Offered(text, transform, ledger, identity);

            // 컴포넌트별로 묶어 `on` 을 멤버마다 되풀이하지 않는다.
            text.Append(",\"by\":[");

            var written = 0;

            // 이 객체에서 타입별로 지나간 컴포넌트 수. 한 객체에 같은 behaviour 가 둘 있으면 ledger key 가 겹쳐 서로 덮어쓰므로
            // 순번으로 구분한다.
            var counted = new Dictionary<Type, int>();

            foreach (var component in transform.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                counted.TryGetValue(type, out var ordinal);
                counted[type] = ordinal + 1;

                // 둘째 이후만 표시하므로 타입당 하나인 대부분의 객체는 key 가 바뀌지 않는다.
                var among = ordinal == 0 ? string.Empty : ordinal.ToString(Invariant) + "#";

                byOwner.TryGetValue(type, out var named);

                // evidence 가 청한 멤버와 이 컴포넌트에서 읽을 수 있는 나머지 멤버.
                var members = Readable.On(type, named);

                if (members == null)
                {
                    continue;
                }

                // `on` 을 한 번만 쓰도록 이 컴포넌트의 멤버를 모은다.
                var mine = new StringBuilder(128);
                var count = 0;

                foreach (var member in members)
                {
                    var said = new StringBuilder(96);

                    said.Append('{');
                    Json.Property(said, "member", Named(member));

                    // `type` 은 싣지 않는다. 멤버마다 200~350 B 인데 MCP server 의 `PulseMember` 가 읽지 않는다.

                    // 같은 타입의 둘째 이후 컴포넌트를 읽는 쪽도 구분할 수 있게 문서에도 순번을 싣는다.
                    if (ordinal > 0)
                    {
                        said.Append(",\"among\":").Append(ordinal.ToString(Invariant));
                    }

                    if (!member.Asked)
                    {
                        said.Append(",\"asked\":false");
                    }

                    said.Append(',');

                    // 싣지 않는 값도 ledger 에는 기록한다. 빠지면 다음 pulse 가 그 값을 새 값으로 보고 변화로 판정한다.
                    var wrote = Value(
                        said, member, component, ledger, identity + "|" + among + member.Key,
                        everything);

                    if (!wrote || silent)
                    {
                        continue;
                    }

                    said.Append('}');

                    if (count > 0)
                    {
                        mine.Append(',');
                    }

                    mine.Append(said);
                    count++;
                }

                if (count > 0)
                {
                    if (written > 0)
                    {
                        text.Append(',');
                    }

                    text.Append("{");
                    Json.Property(text, "on", type.FullName);
                    text.Append(",\"m\":[").Append(mine).Append("]}");
                    written++;
                }
            }

            text.Append("]}");

            if (written == 0 && !moved)
            {
                return false;
            }

            into.Append(text);
            return true;
        }

        /// <summary>
        /// 값, 또는 값을 읽지 못한 이유를 쓴다.
        /// </summary>
        /// <remarks>
        /// 읽다 예외가 나면 0 같은 값이 아니라 읽지 못했다고 쓴다. 잘못된 값은 조건 판정을 틀리게 한다.
        ///
        /// 참조는 내용이 아니라 있음/없음만 쓴다. 조건은 대개 <c>null</c> 비교이고, 내용을 따라가면 게임 데이터 전체가 덤프된다.
        /// </remarks>
        /// <returns>값이 바뀌었거나 whole pulse 라서 이번 pulse 에 실리면 true.</returns>
        private static bool Value(
            StringBuilder text, Watched member, Component on, Ledger ledger, string key,
            bool always = false)
        {
            // 값이 아니라 쓸 JSON 조각을 ledger 로 비교하므로 보낸 내용과 변화 판정이 어긋나지 않는다.
            var said = new StringBuilder(64);

            Read(said, member, on, ledger, key);

            // 싣지 않는 값도 ledger 에 기록해야 다음 pulse 가 변화로 오판하지 않는다.
            var kept = ledger.Keep(key, said.ToString());

            // `always` 는 객체가 방금 켜졌다는 뜻이다. 꺼진 동안 보내지 않은 값은 ledger 상 안 바뀌었어도 보낸다.
            if (!kept && !always)
            {
                return false;
            }

            text.Append(said);
            return true;
        }

        /// <summary>
        /// pulse 에 쓸 멤버 이름. 필드 이름 뒤에 <see cref="Watched.Via"/> 경로를 붙인다.
        /// </summary>
        /// <remarks>
        /// <c>chatWindowController</c> 에서 <c>IsStreaming</c> 을 읽었다면 필드 이름만으로는 다른 값과 구분되지 않는다.
        /// 필드 이름을 앞에 남기는 것은 두 필드가 같은 프로퍼티를 가질 수 있어서다.
        /// </remarks>
        private static string Named(Watched member)
        {
            var found = member.Property ?? member.Member;

            return member.Via == null ? found : found + "." + member.Via;
        }

        /// <summary>
        /// 필드 값에서 분석이 기록한 경로를 따라간다. 각 단계는 필드이거나 인자 없는 프로퍼티다.
        /// </summary>
        private static object Along(object from, string path)
        {
            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var step in path.Split('.'))
            {
                if (from == null)
                {
                    return null;
                }

                var type = from.GetType();
                var field = type.GetField(step, Flags);

                if (field != null)
                {
                    from = field.GetValue(from);
                    continue;
                }

                var property = type.GetProperty(step, Flags);

                if (property == null || !property.CanRead ||
                    property.GetIndexParameters().Length != 0)
                {
                    return null;
                }

                from = property.GetValue(from, null);
            }

            return from;
        }

        private static void Read(
            StringBuilder text, Watched member, Component on, Ledger ledger, string key)
        {
            object held;

            try
            {
                // `Field` 가 null 이면 컴포넌트에서 `Member` 프로퍼티를 읽는다(`Drawn` 멤버). 이 경우 `Along` 이 pulse 마다
                // `GetProperty` 를 다시 조회한다. `PropertyInfo` 캐시는 비용을 잰 뒤에 판단한다.
                held = member.Field == null
                    ? Along(on, member.Member)
                    : member.Field.GetValue(on);

                // evidence 가 필드에서 닿는 값을 청했으면 경로를 따라간다. 게임 상태를 바꾸지 않도록 메서드는 호출하지 않는다.
                if (member.Via != null && held != null)
                {
                    held = Along(held, member.Via);
                }
            }
            catch (Exception exception)
            {
                Json.Property(text, "unread", exception.GetType().Name);
                return;
            }

            if (held == null)
            {
                text.Append("\"value\":null");
                return;
            }

            switch (held)
            {
                case bool flag:
                    text.Append("\"value\":").Append(flag ? "true" : "false");
                    return;

                case int number:
                    text.Append("\"value\":").Append(number.ToString(Invariant));
                    return;

                case long number:
                    text.Append("\"value\":").Append(number.ToString(Invariant));
                    return;

                case float number:
                    Number(text, ledger.Restless.Settle(key, number));
                    return;

                case double number:
                    Number(text, number);
                    return;

                case string words:
                    Json.Property(text, "value", words);
                    return;

                case Enum member_:
                    Json.Property(text, "value", member_.ToString());
                    return;
            }

            if (held is UnityEngine.Object reference)
            {
                // Unity 의 == 오버로드로 파괴된 객체도 null 로 본다. 게임 코드의 null 비교와 같은 답이다.
                if (reference == null)
                {
                    text.Append("\"value\":null");
                    return;
                }

                Held(text, reference, ledger.Restless, key);
                return;
            }

            if (held is System.Collections.ICollection collection)
            {
                // 컬렉션은 개수만 싣는다. 조건은 대개 개수를 묻고, 내용물은 게임 데이터 덤프가 된다.
                text.Append("\"count\":").Append(collection.Count.ToString(Invariant));
                return;
            }

            // 일반 객체는 구체 타입 이름을 싣는다. 상태 기계의 현재 상태나 튜토리얼 단계처럼 어떤 클래스가 들어 있는지가 곧
            // 상태인 경우가 많다. "있음" 만 쓰면 null 이 아닌 참조는 늘 같은 값이라 아무것도 알려 주지 않는다.
            text.Append("\"value\":{");
            Json.Property(text, "is", held.GetType().FullName);
            text.Append('}');
        }

        /// <summary>
        /// Unity 객체 참조가 가리키는 대상을 쓴다: 경로, 활성 여부, 월드 위치.
        /// </summary>
        /// <remarks>
        /// <c>MapMove.battle2.transform.position</c> 같은 조건은 이름 붙은 객체가 다른 객체 위치에 도착했는지를 묻는다.
        /// 경로만으로는 움직였는지, 위치만으로는 무엇이 움직였는지 알 수 없어 둘 다 싣는다.
        /// </remarks>
        private static void Held(
            StringBuilder text, UnityEngine.Object reference, Restless restless, string key)
        {
            if (reference is Animator animator)
            {
                Playing(text, animator);
                return;
            }

            if (Showing(text, reference as Component))
            {
                return;
            }

            var transform = reference as Transform
                            ?? (reference as GameObject)?.transform
                            ?? (reference as Component)?.transform;

            if (transform == null)
            {
                // 스프라이트, 클립, ScriptableObject 같은 에셋은 씬 위치가 없어 이름만 싣는다.
                text.Append("\"value\":{");
                Json.Property(text, "name", reference.name);
                text.Append('}');
                return;
            }

            var world = transform.position;

            text.Append("\"value\":{");
            Json.Property(text, "path", ScenePath.Of(transform));
            text.Append(",\"active\":").Append(transform.gameObject.activeInHierarchy ? "true" : "false");
            text.Append(",\"world\":{\"x\":");
            Coordinate(text, restless.Settle(key + "|x", world.x));
            text.Append(",\"y\":");
            Coordinate(text, restless.Settle(key + "|y", world.y));
            text.Append(",\"z\":");
            Coordinate(text, restless.Settle(key + "|z", world.z));
            text.Append("}}");
        }

        /// <summary>
        /// 객체의 tag 를 쓴다.
        /// </summary>
        /// <remarks>
        /// <c>CompareTag</c> 로 갈리는 게임 규칙이 흔하다. tag 는 위치나 <c>active</c> 처럼 Unity 가 모든 객체에 주는 값이라
        /// 조건과 무관하게 싣는다.
        ///
        /// 대다수인 <c>Untagged</c> 는 문서 크기만 늘리므로 싣지 않는다. 런타임에 tag 가 바뀔 수 있어 ledger 로 변화를 판정한다.
        /// </remarks>
        private static bool Tagged(
            StringBuilder text, Transform transform, Ledger ledger, string identity)
        {
            var tag = transform.gameObject.tag;

            if (string.IsNullOrEmpty(tag) || tag == "Untagged")
            {
                return false;
            }

            var said = new StringBuilder(32);
            said.Append(',');
            Json.Property(said, "tag", tag);

            var rendered = said.ToString();

            if (!ledger.Keep(identity + "|tag", rendered))
            {
                return false;
            }

            text.Append(rendered);
            return true;
        }

        /// <summary>
        /// 객체의 월드 위치와 화면 사각형을 쓴다.
        /// </summary>
        /// <remarks>
        /// 화면 캡처와 pulse 를 맞춰 보려면 공통 좌표가 필요하다. 포인터 액션은 픽셀을 받는데 월드→화면 변환은 엔진 밖에서
        /// 할 수 없으므로 <c>rect</c> 도 싣는다. 사각형은 무엇이든 움직이면 바뀌므로 매 pulse 변화를 판정한다.
        ///
        /// 모든 객체에 붙으므로 마지막 소수 자리 흔들림이 매 pulse 를 변화로 만들지 않도록 <see cref="Restless"/> 를 거친다.
        /// 월드는 <c>Restless</c>, 화면은 픽셀 경계의 <c>Pixels</c> 를 쓴다.
        /// </remarks>
        private static bool Where(
            StringBuilder text, Transform transform, Ledger ledger, string identity)
        {
            var world = transform.position;
            var said = new StringBuilder(64);

            said.Append(",\"world\":{\"x\":");
            Coordinate(said, ledger.Restless.Settle(identity + "|wx", world.x));
            said.Append(",\"y\":");
            Coordinate(said, ledger.Restless.Settle(identity + "|wy", world.y));
            said.Append(",\"z\":");
            Coordinate(said, ledger.Restless.Settle(identity + "|wz", world.z));
            said.Append('}');

            var area = ScreenArea.Of(transform);

            said.Append(",\"rect\":{\"x\":");
            Coordinate(said, ledger.Pixels.Settle(identity + "|sx", area.x));
            said.Append(",\"y\":");
            Coordinate(said, ledger.Pixels.Settle(identity + "|sy", area.y));
            said.Append(",\"w\":");
            Coordinate(said, ledger.Pixels.Settle(identity + "|sw", area.width));
            said.Append(",\"h\":");
            Coordinate(said, ledger.Pixels.Settle(identity + "|sh", area.height));
            said.Append('}');

            var rendered = said.ToString();

            if (!ledger.Keep(identity + "|world", rendered))
            {
                return false;
            }

            text.Append(rendered);
            return true;
        }

        /// <summary>
        /// 보이는 요소의 <c>onScreen</c>, <c>covered</c> 를 쓴다.
        /// </summary>
        /// <remarks>
        /// 화면 크기와 그 순간의 모든 요소가 필요해 차이만 받는 읽는 쪽은 계산할 수 없으므로 SDK 가 판정한다.
        /// 가려짐은 추정이다(<see cref="Sight"/>).
        /// </remarks>
        /// <returns>보이는 요소이고 값이 이번 pulse 에 실리면 true.</returns>
        private static bool Sighted(
            StringBuilder text, Transform transform, Ledger ledger, string identity,
            Dictionary<int, string> sight)
        {
            if (!sight.TryGetValue(transform.gameObject.GetInstanceID(), out var rendered))
            {
                return false;
            }

            if (!ledger.Keep(identity + "|sight", rendered))
            {
                return false;
            }

            text.Append(rendered);
            return true;
        }

        /// <summary>
        /// 객체별 offer JSON 조각 캐시의 최대 크기.
        /// </summary>
        /// <remarks>
        /// persistent call 을 읽는 리플렉션을 매 pulse 반복하지 않도록 객체마다 한 번 계산해 기억한다. 인스펙터 wiring 과 컴포넌트
        /// 타입은 실행 중 바뀌지 않는다고 가정한다. 같은 타입의 버튼도 연결이 다를 수 있어 인스턴스 단위로 기억한다.
        ///
        /// 한도에 닿으면 전부 비운다(<see cref="Worth"/> 와 같다).
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, string> Offers = new Dictionary<int, string>();

        /// <summary>
        /// 이 객체에 줄 수 있는 입력(클릭, 키, 포인터)을 쓴다.
        /// </summary>
        /// <remarks>
        /// 클릭은 인스턴스마다 다른 인스펙터 wiring 이라 여기서 읽는다. 키와 포인터 handler 는 분석이 타입별로 구워 두었고,
        /// 그 타입이 붙은 객체에서만 싣는다.
        ///
        /// 버튼이 생기거나 사라지거나 연결이 바뀌는 것도 변화이므로 ledger 로 판정한다.
        /// </remarks>
        /// <returns>이 객체의 입력이 이번 pulse 에 실리면 true.</returns>
        private static bool Offered(
            StringBuilder text, Transform transform, Ledger ledger, string identity)
        {
            var id = transform.gameObject.GetInstanceID();

            if (Offers.TryGetValue(id, out var remembered))
            {
                if (remembered.Length == 0)
                {
                    return false;
                }

                if (!ledger.Keep(identity + "|offers", remembered))
                {
                    return false;
                }

                text.Append(remembered);
                return true;
            }

            if (Offers.Count >= MaxRemembered)
            {
                Offers.Clear();
            }

            var said = new StringBuilder(128);
            var calls = new List<PersistentCall>();
            var keys = new List<WatchList.KeyOffer>();
            var pointers = new List<string>();

            foreach (var component in transform.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                try
                {
                    PersistentCallReader.Read(component, calls);
                }
                catch (Exception)
                {
                    // 이 컴포넌트만 건너뛰고 나머지 입력은 읽는다.
                }

                var offer = WatchList.OfferedBy(component.GetType().FullName);

                if (offer == null)
                {
                    continue;
                }

                AddKeys(keys, offer.Keys);
                Add(pointers, offer.Pointers);
            }

            if (calls.Count == 0 && keys.Count == 0 && pointers.Count == 0)
            {
                // 입력이 없는 객체가 대부분이므로 빈 값도 기억해 다시 계산하지 않는다.
                Offers[id] = string.Empty;
                return false;
            }

            said.Append(",\"offers\":{");
            var written = 0;

            if (calls.Count > 0)
            {
                said.Append("\"clicks\":[");

                for (var at = 0; at < calls.Count; at++)
                {
                    if (at > 0)
                    {
                        said.Append(',');
                    }

                    said.Append('{');
                    Json.Property(said, "event", calls[at].Event);
                    said.Append(',');
                    Json.Property(said, "method", calls[at].Method);
                    said.Append(',');
                    Json.Property(said, "on", calls[at].TargetPath);
                    said.Append('}');
                }

                said.Append(']');
                written++;
            }

            written += Keys(said, keys, written);
            Flat(said, "pointers", pointers, written);

            said.Append('}');

            var rendered = said.ToString();
            Offers[id] = rendered;

            if (!ledger.Keep(identity + "|offers", rendered))
            {
                return false;
            }

            text.Append(rendered);
            return true;
        }

        private static int Flat(StringBuilder text, string name, List<string> offered, int written)
        {
            if (offered.Count == 0)
            {
                return 0;
            }

            if (written > 0)
            {
                text.Append(',');
            }

            offered.Sort(StringComparer.Ordinal);
            text.Append('"').Append(name).Append("\":[");

            for (var at = 0; at < offered.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                Json.String(text, offered[at]);
            }

            text.Append(']');
            return 1;
        }

        /// <summary>
        /// 키와 그 효과를 <c>clicks</c> 와 같은 객체 배열 모양으로 쓴다.
        /// </summary>
        /// <remarks>
        /// 효과를 모르는 키는 <c>does</c> 를 생략한다. 빈 배열은 "아무 일도 안 한다" 로 읽히지만 실제로는 "분석이 못 읽었다" 이다.
        /// </remarks>
        private static int Keys(StringBuilder text, List<WatchList.KeyOffer> offered, int written)
        {
            if (offered.Count == 0)
            {
                return 0;
            }

            if (written > 0)
            {
                text.Append(',');
            }

            // 순서가 바뀌면 그 자체가 변화로 판정되므로 정렬한다.
            offered.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            text.Append("\"keys\":[");

            for (var at = 0; at < offered.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                text.Append('{');
                Json.Property(text, "key", offered[at].Key);

                var does = offered[at].Does;

                if (does.Count > 0)
                {
                    text.Append(",\"does\":[");

                    for (var which = 0; which < does.Count; which++)
                    {
                        if (which > 0)
                        {
                            text.Append(',');
                        }

                        Json.String(text, does[which]);
                    }

                    text.Append(']');
                }

                text.Append('}');
            }

            text.Append(']');
            return 1;
        }

        private static void AddKeys(
            List<WatchList.KeyOffer> into, List<WatchList.KeyOffer> more)
        {
            foreach (var one in more)
            {
                if (!into.Exists(seen => seen.Key == one.Key))
                {
                    into.Add(one);
                }
            }
        }

        private static void Add(List<string> into, List<string> more)
        {
            foreach (var one in more)
            {
                if (!into.Contains(one))
                {
                    into.Add(one);
                }
            }
        }

        /// <summary>
        /// 참조가 라벨이나 그림이면 표시 내용을 쓴다.
        /// </summary>
        /// <remarks>
        /// 라벨은 위치가 아니라 내용을 묻는 대상이다. 경로와 활성 여부는 남기고, 월드 위치는 조건에 쓰이지 않고 흔들림만 더하므로
        /// 뺀다.
        ///
        /// uGUI 와 TextMeshPro 는 없을 수 있는 패키지라 <see cref="SceneEvidenceScan"/> 을 거쳐 타입 이름으로 맞춘다.
        /// </remarks>
        /// <returns>참조가 라벨이나 그림이어서 썼으면 true.</returns>
        private static bool Showing(StringBuilder text, Component component)
        {
            if (component == null)
            {
                return false;
            }

            var shown = SceneEvidenceScan.TextOf(component);
            var role = "label";

            if (shown == null)
            {
                shown = SceneEvidenceScan.SpriteOf(component);
                role = "sprite";
            }

            if (shown == null)
            {
                return false;
            }

            text.Append("\"value\":{");
            Json.Property(text, "path", ScenePath.Of(component.transform));
            text.Append(",\"active\":")
                .Append(component.gameObject.activeInHierarchy ? "true" : "false")
                .Append(',');
            Json.Property(text, role, shown);
            text.Append('}');
            return true;
        }

        /// <summary>
        /// animator 의 현재 상태를 쓴다. 해시는 항상, 이름은 알 수 있을 때만 싣는다.
        /// </summary>
        /// <remarks>
        /// Unity 는 상태를 해시로만 돌려주므로 분석이 모은 animator 이름 후보를 <c>IsName</c> 으로 확인한다. 코드가 언급하지 않은
        /// 상태도 바뀐 것은 보이도록 해시는 항상 싣는다.
        ///
        /// 트리거 이름과 상태 이름은 같다는 보장이 없어, Unity 가 확인한 이름만 쓴다.
        ///
        /// 매개변수 값은 읽지 않는다. 트리거는 한 frame 안에 소비되어 pulse 로 읽으면 거의 항상 false 로 보인다.
        /// </remarks>
        private static void Playing(StringBuilder text, Animator animator)
        {
            text.Append("\"value\":{");
            Json.Property(text, "path", ScenePath.Of(animator.transform));

            AnimatorStateInfo state;

            try
            {
                state = animator.GetCurrentAnimatorStateInfo(0);
            }
            catch (Exception exception)
            {
                // 컨트롤러나 레이어 0 이 없는 animator 다. animator 가 없는 경우와 구분되도록 unread 로 쓴다.
                text.Append(',');
                Json.Property(text, "unread", exception.GetType().Name);
                text.Append('}');
                return;
            }

            text.Append(",\"stateHash\":").Append(state.shortNameHash.ToString(Invariant));

            foreach (var name in WatchList.AnimatorNames)
            {
                if (!state.IsName(name))
                {
                    continue;
                }

                text.Append(',');
                Json.Property(text, "state", name);
                break;
            }

            Parameters(text, animator);
            text.Append('}');
        }

        /// <summary>
        /// animator 의 매개변수 이름을 모두 쓴다.
        /// </summary>
        /// <remarks>
        /// 코드의 <c>SetTrigger("Attack")</c> 에 맞는 매개변수가 animator 에 실제로 있는지 확인할 수 있게 한다. 오타나 바뀐
        /// 컨트롤러는 컴파일 오류 없이 애니메이션만 재생되지 않는다.
        ///
        /// 일치하는 것만 쓰면 "이름이 없음" 과 "매개변수가 하나도 없음" 이 구분되지 않아 전부 쓴다.
        ///
        /// 값은 쓰지 않는다. 트리거는 거의 항상 false 로 읽히고, 움직임이 구동하는 float 는 매 pulse 변화로 판정된다.
        /// </remarks>
        private static void Parameters(StringBuilder text, Animator animator)
        {
            AnimatorControllerParameter[] parameters;

            try
            {
                parameters = animator.parameters;
            }
            catch (Exception exception)
            {
                text.Append(',');
                Json.Property(text, "parametersUnread", exception.GetType().Name);
                return;
            }

            if (parameters == null)
            {
                return;
            }

            text.Append(",\"parameters\":[");

            for (var at = 0; at < parameters.Length; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                Json.String(text, parameters[at].name);
            }

            text.Append(']');
        }

        private static void Coordinate(StringBuilder text, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                text.Append("null");
                return;
            }

            text.Append(Math.Round(value, Decimals).ToString("0.####", Invariant));
        }

        private static readonly System.Globalization.CultureInfo Invariant =
            System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>
        /// float 을 반올림할 소수 자리 수.
        /// </summary>
        /// <remarks>
        /// 반올림하지 않으면 idle 애니메이션 같은 미세한 흔들림이 매 pulse 를 변화로 만든다. 네 자리는 <c>evidence</c> 의
        /// 비교보다 충분히 곱다.
        /// </remarks>
        private const int Decimals = 4;

        private static void Number(StringBuilder text, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // NaN 과 무한대는 JSON 숫자로 쓸 수 없다. 0 으로 바꾸지 않고 unread 로 쓴다.
                Json.Property(text, "unread", "not-a-number");
                return;
            }

            text.Append("\"value\":").Append(Math.Round(value, Decimals).ToString("0.####", Invariant));
        }
    }
}
