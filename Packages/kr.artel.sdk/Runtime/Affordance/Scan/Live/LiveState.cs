using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Artel.Affordances.Scan;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 감시 대상 멤버가 지금 쥐고 있는 것을, 리포트가 그것들을 부르는 방식으로 쓴다.
    /// </summary>
    /// <remarks>
    /// 리포트는 <c>MapMove.position == 0</c> 이라고 말하는데 지금까지 아무것도 <c>position</c> 이 무엇을 쥐고 있는지 볼 수
    /// 없었으므로, 명세의 모든 줄이 제 전제를 확인할 방법이 없는 규칙이었다. 이것이 그 문장의 반대쪽이다.
    ///
    /// 둘을 일부러 갈라 둔다. static 필드는 값이 하나이고 소유자가 없으며, 인스턴스 필드는 그것을 나르는 객체마다 값이 하나다 —
    /// 만들어진 적 다섯은 <c>hp</c> 에 대한 답 다섯이고, 그것들을 하나로 접는 것은 두 객체에 대한 조건을 한 문장에 쓰는 것과
    /// 같은 실수다. static 은 제 목록으로 쓰고, 인스턴스 값은 그것을 쥔 객체의 경로 아래에 쓰며, 평균 내거나 합하거나 골라
    /// 내지 않는다.
    ///
    /// 여기서는 아무것도 해석하지 않는다. 값은 필드가 준 대로 쓰고 분석이 선언한 대로 타입을 붙이며, <c>flag == 1</c> 이
    /// 무엇을 뜻하는지는 이것을 읽는 쪽의 물음이다.
    /// </remarks>
    internal static class LiveState
    {
        /// <summary>감시 대상 멤버 하나를 몇 개의 객체에서까지 찾고 나머지를 버리는지.</summary>
        /// <remarks>
        /// 풀에서 꺼내 쓰는 발사체는 수백 개가 존재할 수 있고, 풀과 함께 커지는 페이로드는 변화 게이트가 쓸 수 없는 것이다 —
        /// 어떤 조건도 언급하지 않는 이유로 폴링마다 달라지기 때문이다. 무엇이 버려졌는지는 완전해 보이는 개수에서 유추하게 두지
        /// 않고 그 멤버에 적는다.
        /// </remarks>
        private const int MaxHolders = 16;

        /// <summary>
        /// 감시 대상 멤버를 전부 읽고 문서 하나를 쓴다.
        /// </summary>
        /// <remarks>
        /// 타입마다 따로 찾아다니는 대신, 씬을 한 번 걷고 모든 컴포넌트를 모든 감시 대상 타입에 내놓는다. 감시 대상 멤버가 백 개인
        /// 게임은 그러지 않으면 폴링마다 계층을 백 번 걷게 되고, 걷기가 비싼 절반이다.
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

            // 판독을 그것이 서술하는 프레임 옆에 놓을 수 있도록 말해 둔다. 명세는 화면과 이것을 동시에 대고 확인되는데, 시간 위의
            // 자리가 없으면 그 둘은 어느 순간에 속하는지 가릴 방법이 없는 두 진술이다. 프레임은 게임 자신이 세는 것이므로 같은
            // 프레임을 읽는 다른 무엇도 일치한다.
            text.Append(",\"reading\":").Append(reading);
            text.Append(",\"frame\":").Append(Time.frameCount);

            text.Append(",\"scene\":");
            Json.String(text, active.IsValid() ? active.name : null);

            // 첫 판독은 차이를 잴 대상이 없고, 화면이 바뀌면 어차피 그 위의 모든 것이 갈아치워진다 — 그래서 전량 상태를 보내는 값이
            // 거기서는 거의 들지 않으면서 독자에게 확신할 수 있는 지점을 준다. 그 밖의 모든 판독은 움직인 것만 나른다.
            //
            // 그리고 전달되지 못한 판독 뒤에도 그렇다. 차이를 놓친 독자는 무언가 다시 그 값을 움직이기 전까지 그것에 대해 틀린 채로
            // 있고, 그것은 전량 판독이 고치지 또 다른 차이가 고치지 못한다. 잃어버린 문서를 다시 보내는 것은 sink 가 이미 언짢아하는
            // 홍수가 되지만, 전체 상태를 한 번 보내는 것은 그렇지 않다.
            since.TryGetValue("scene", out var was);

            var everything = repair || since.Count == 0 ||
                             was != (active.IsValid() ? active.name : null);

            var ledger = new Ledger
            {
                Restless = restless, Pixels = pixels, Since = since, Now = now, Moved = moved,
                Everything = everything
            };

            // 테스터가 있는 화면은 판독이 주장하는 것의 일부이므로, 화면이 바뀌는 것은 그 위의 모든 값이 마침 같게 읽히더라도
            // 소식이다.
            ledger.Say("scene", active.IsValid() ? active.name : null);

            text.Append(",\"statics\":[");
            Statics(text, statics, ledger);
            text.Append(']');

            var showing = new Bin();
            var hidden = new Bin();

            // Camera.main 은 태그로 하는 씬 전체 조회다. 객체마다 한 번이면 순회를 잡아먹으므로 판독 전체에 대해 한 번 푼다.
            ScreenArea.Begin();

            var truncated = Objects(
                persistent, active.IsValid() ? active.name : null, byOwner, ledger, showing, hidden);

            ScreenArea.Forget();

            showing.WriteTo(text, "active");
            hidden.WriteTo(text, "deactive");

            // 독자가 어떤 종류의 판독을 쥐고 있는지 알도록 말해 둔다. 그것이 없으면 델타와 전량 판독이 똑같아 보이고, 하나를 다른
            // 하나로 오인한 독자는 아직 필요한 상태를 버리거나 사라진 상태를 계속 쥐게 된다.
            text.Append(",\"whole\":").Append(everything ? "true" : "false");

            text.Append(",\"watching\":").Append(watched.Count);
            text.Append(",\"unresolved\":").Append(WatchList.Unresolved);
            text.Append(",\"unwatchable\":").Append(WatchList.Unwatchable);

            if (truncated > 0)
            {
                text.Append(",\"gaps\":[\"holder-limit:").Append(truncated).Append("\"]");
            }

            // 직전 판독과 무엇이 다른가. 비어 있을 때도 쓴다. 빈 목록과 없는 필드는 다른 주장이기 때문이다 — 앞의 것은 값들이
            // 비교됐고 아무것도 움직이지 않았다는 말인데, 그것은 한 실행의 첫 판독이 할 수 없는 말이다.
            //
            // 들썩이는 값을 찾을 수 있게 만드는 절반이 이것이다. 판독이 거의 다 나가는 실행은 어떤 조건도 언급하지 않는 무언가가
            // 움직이고 있는 실행이고, 이것이 없으면 독자는 그런 일이 일어난다는 것만 볼 뿐 어느 멤버가 그러는지는 영영 못 본다.
            // 끝에 세지 않고 판독마다 말하므로 첫 판독에서 눈에 띈다.
            // 사라진 것도 변화다. 직전 판독에 있었고 이번에 없는 키는 파괴된 객체이거나 떠나온 화면이고, 지금 여기 있는 것만 비교하면
            // 게임에서 가장 분주한 순간 — 모든 것이 헐리는 순간 — 을 아무 일도 없었다고 보고하게 된다.
            foreach (var pair in since)
            {
                if (!now.ContainsKey(pair.Key))
                {
                    moved.Add(pair.Key);
                }
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

        /// <summary>이번 판독이 한 말, 직전 판독이 한 말, 그리고 그 차이.</summary>
        /// <summary>
        /// 판독의 객체들이 나뉘어 들어가는 두 목록 중 하나.
        /// </summary>
        /// <remarks>
        /// 켜짐과 꺼짐은 객체 위의 필드가 아니라 그것이 어느 목록으로 도착하는지로 가린다. 샘플 게임 명세의 한 줄이 <em>계속 버튼이
        /// 비활성으로 보인다</em> 이므로 꺼진 것들도 날라야 하는데, 그것들을 나머지와 섞어 나르면 독자가 채널에 물은 바로 그 물음에
        /// 답하려고 목록을 필터링하게 된다. 정렬은 같은 일을 여기서 한 번 하는 것이다.
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

        private sealed class Ledger
        {
            internal Restless Restless;

            /// <summary>화면 좌표에 대한 같은 데드밴드. 그 경계가 픽셀이다.</summary>
            /// <remarks>
            /// 월드 쪽과 갈라 둔다. 둘은 같은 측정이 아니기 때문이다. 월드 단위에서 안전할 만큼 작은 경계는 — 여기의 무엇도 그 축척을
            /// 알 수 없는 게임에서 천분의 일 — 천 픽셀 너비 화면을 가로질러서는 필터가 전혀 아니고, 사각형은 감시 대상 몇 개가 아니라
            /// 모든 객체에 붙는다. 하나를 공유하면 둘 중 어느 쪽에 대해 틀릴지를 골라야 한다.
            /// </remarks>
            internal Restless Pixels;

            internal Dictionary<string, string> Since;
            internal Dictionary<string, string> Now;
            internal List<string> Moved;

            /// <summary>
            /// 값 하나를 제 이름 아래에 기록하고 그것이 새것인지 적어 둔다.
            /// </summary>
            /// <remarks>
            /// 값이 무엇이라 불리는지가 아니라 어디에 사는지로 키를 잡으므로, 두 객체 위의 같은 필드는 두 항목이다. 각자 독립적으로
            /// 움직이는 다섯 적에게 나타나는 멤버는 각각 움직였음을 볼 수 있는 다섯 가지이고, 그 사실이 쓸모 있는 형태는 그것뿐이다.
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
            /// 이 판독이 움직인 것만이 아니라 전부를 나르는지.
            /// </summary>
            /// <remarks>
            /// 차이만 나르는 판독이 요점 전부다 — watch list 는 이제 근거가 청한 전부가 아니라 읽을 수 있는 전부를 쥐고 있고, 그중
            /// 아무것도 움직이지 않았다고 말하려고 게임의 상태 전체를 초당 열 번 보내는 것이 그렇게 넓힌 값이 될 뻔했다.
            ///
            /// 하지만 차이만 본 독자는 값이 무엇인지 한 번도 들은 적이 없고, 판독 하나를 놓친 독자는 무언가 각각을 움직이기 전까지 그
            /// 값들에 대해 틀린 채로 있다. 그래서 독자가 반드시 가지고 있으리라 셈할 수 있는 지점에서 전체 상태가 나간다: 첫 판독과,
            /// 화면이 바뀔 때마다. 씬 전환이 자연스러운 지점이다 — 화면 위의 모든 것이 어차피 갈아치워지므로 거기서 전량 판독은 거의
            /// 값이 들지 않고, 명세가 대고 쓰이는 경계이기도 하다.
            /// </remarks>
            internal bool Everything;

            /// <summary>값을 말하고, 그것이 이번 판독에 들어가는지 답한다.</summary>
            internal bool Keep(string key, string value)
            {
                return Say(key, value) | Everything;
            }
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
        /// 테스트가 작용할 수 있는 모든 객체를, 그것이 앉은 경로 아래에.
        /// </summary>
        /// <remarks>
        /// 리포트가 가진 것과 같은 객체들이고 같은 방식으로 정한다. 그것은 편의가 아니다: 명세는 이 게임에 대한 리포트 자신의
        /// 순회에서 쓰였으므로, 순회가 적어 둔 객체는 어떤 줄이 이름 댈 수 있는 객체다 — 그리고 그 순회보다 좁은 판독은 패키지가
        /// 내내 가지고 있던 줄의 전제를 없는 것으로 보고한다.
        ///
        /// 실제로 좁았다. 이것은 예전에 근거가 이름 댄 멤버를 나르는 객체만 방문했고, 그래서 <c>Canvas/ExitButton</c> 과 버튼
        /// 셋이 모든 판독에서 빠졌다. 정작 리포트는 그것들을 경로와 켜짐 상태와 함께 나열하고 있었다. 오직 그 이유로 여섯 줄이
        /// 답할 수 없는 것이었다.
        ///
        /// 그래서 watch list 는 무엇을 *읽을지* 를 정하지 무엇을 *방문할지* 를 결코 정하지 않는다. 그것의 일 전부는 순회가 볼 수
        /// 없는 값이고 — private 필드, 아무 객체에도 매달리지 않은 static — 그것으로 순회를 정하는 일은 한 가지에 두 가지 일을
        /// 맡긴 것이었다.
        ///
        /// 로드된 모든 씬과 영속 씬. 인터페이스를 매니저 씬 위에 얹는 게임은 둘 다 플레이하고 있는 것이고, 게임이 씬 로드를 건너
        /// 쥐고 있는 것은 Unity 가 로드된 씬으로 아예 세지 않는 자리에 정리돼 있다.
        ///
        /// 로드되지 않은 씬은 읽지 않는다: 읽을 것이 없고 테스터도 거기 없다. 로드된 씬 안의 비활성 객체는 읽는다. 계속 버튼이
        /// 켜져 있는지가 한 줄이 확인하는 것의 전부이기 때문이다 — 그리고 없는 버튼과 꺼진 버튼이 똑같아 보이므로, 그 화면의
        /// 녹화가 답할 수 없는 유일한 것이 그것이다.
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

            // 게임이 씬 로드를 건너 쥐고 있던 것. Unity 는 그것을 로드된 씬으로 세지 않으므로 그것들만 걷는 순회는 이것을 놓치고 —
            // 게임이 화면보다 오래 사는 것들을 두는 자리가 여기다. 샘플 게임은 스테이지 번호를 거기 두는데, 명세 스물여섯 줄이 그것을
            // 검사한다.
            //
            // 다른 화면의 데이터가 아니다. 이 객체들은 이 플레이 세션 안에 지금 살아 있고, 두 번 말해야 하는 유일한 이유는 Unity 가
            // 그것들을 따로 정리해 두기 때문이다.
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

            for (var index = 0; index < roots.Length; index++)
            {
                if (roots[index] == null || roots[index].hideFlags != HideFlags.None)
                {
                    // pulse 자신의 carrier 도 다른 것과 마찬가지로 씬 안에 산다. 그것을 보고하는 것은 게임이 아니라 계기를 보고하는 일이다.
                    continue;
                }

                foreach (var transform in roots[index].GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    if (!Worth.Writing(transform.gameObject, byOwner))
                    {
                        continue;
                    }

                    var kind = transform.gameObject.GetType();
                    seen.TryGetValue(kind, out var already);
                    seen[kind] = already + 1;

                    if (already >= MaxHolders * MaxHolders)
                    {
                        dropped++;
                        continue;
                    }

                    var said = new StringBuilder(256);

                    if (!Object(said, transform, scene, top, index, byOwner, ledger))
                    {
                        continue;
                    }

                    // 어느 통에 들어가는가가 곧 그 진술이므로, 객체가 같은 말을 하는 플래그를 따로 나르지 않는다. 차이를 쥔 독자는 그 객체가
                    // 어디로 도착했는지로 꺼져 있음을 알고, 이번 판독에 아무 말도 하지 않는 객체는 마지막으로 놓인 자리에 그대로 있다 —
                    // 그것이 옳다. 그것이 바뀌는 일 자체가 차이이고 그러면 그 객체를 여기로 데려왔을 것이기 때문이다.
                    (transform.gameObject.activeInHierarchy ? showing : hidden).Add(said);
                }
            }

            return dropped;
        }

        /// <summary>객체 하나를 쓴다: 어디 있는지, 보이고 있는지, 무엇을 쥐고 있는지.</summary>
        /// <remarks>
        /// 컴포넌트마다가 아니라 객체마다 기록 하나이고, 그것이 리포트가 이미 쓰는 모양이다. 줄이 이름 대는 것과 테스터가 작용하는
        /// 것이 객체다. 그 컴포넌트 둘이 각각 감시 대상 필드를 쥐고 있다는 것은 그 안의 배치다.
        /// </remarks>
        /// <returns>이 객체에 대해 무엇이든 판독에 들어갈 것이 있으면 참.</returns>
        private static bool Object(
            StringBuilder into,
            Transform transform,
            Scene scene,
            string top,
            int rootIndex,
            Dictionary<Type, List<Watched>> byOwner,
            Ledger ledger)
        {
            var selector = ScenePath.SelectorOf(transform, rootIndex);

            // 경로가 아니라 selector 로 키를 잡는다. 만들어진 적 다섯은 경로 하나를 공유하므로 —
            // `TurnBattleScene/RangedCat(Clone)` 이 다섯 번 — 경로로 키를 잡은 장부는 그것들이 서로를 덮어쓰게 하고, 한 번도 움직이지
            // 않은 객체에 대해 판독마다 변화를 보고한다. 실측: 그것 하나가 한 실행에서 게이트를 연 것의 대부분이었다.
            var identity = scene.name + "/" + selector;

            // 객체를 아예 쓸지가 그 멤버들을 읽고 나서야 알려지므로 옆에 만들어 둔다. 어떤 값도 움직이지 않은 객체는 판독이 그것에
            // 대해 할 말이 없는 객체이고, 그렇다고 말하기 위해 그것이 쥔 전부를 치르는 것은 틀린 값이다.
            var text = new StringBuilder(256);

            // 어디 있는지는 언제나 쓴다. selector 의 경로를 한 번도 듣지 못한 독자는 그것에 대한 델타로 아무것도 할 수 없고, 이것들은
            // 멤버들 옆에서 아무 값도 들지 않는다.
            //
            // id 가 여기 있는 것은 독자가 자기가 읽은 것에 대해 행동할 수 있게 하기 위해서다. 모든 액션이 대상을 instance id 로
            // 지목하는데 지금까지 그 숫자는 씬 보고에만 실려 왔다 — 그래서 판독을 쥔 독자는 무엇이 바뀌었는지 알면서 그것을 건드릴
            // 방법이 없었다. 바뀔 때만이 아니라 매 기록에 쓰는 것은 경로와 같은 이유다: 그것은 결코 바뀌지 않고, id 를 한 번도 받은
            // 적 없는 객체에 대한 델타는 아무도 행동할 수 없는 차이다.
            //
            // instance id 는 프로세스를 넘어 살아남지 못하고, selector 도 여기 있는 이유가 그것이다. 새로 생긴 약점은 아니지만 —
            // 씬 보고도 같은 숫자로 주소지정한다 — 결국 selector 가 액션이 지목할 것이 되어야 하는 이유다.
            text.Append('{');

            // 최상위가 이미 씬을 말한다. 다른 씬의 객체만 제 이름을 댄다 — persistent 씬이 그렇다.
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

            // 객체에 써넣지 않고 장부에 말해 둔다. 어느 목록에 들어가는지가 이미 그것을 말하고, 두 번 말하는 것은 한 사실이 어긋날
            // 자리를 둘 두는 일이다. 장부는 여전히 그것이 필요하다. 꺼지는 일이 차이가 되어 그 객체를 독자에게 데려오도록.
            var live = transform.gameObject.activeInHierarchy;

            var flipped = ledger.Keep(identity + "|active", live ? "true" : "false");
            var moved = flipped;

            // 꺼져 있는 동안은 값을 싣지 않는다. 독자가 그것을 그리지 않기 때문이다 — 화면에 없고
            // 누를 수도 없어 조준 후보가 아니다. 풀에서 대기하는 적 열여덟이 전량 판독의 41% 를
            // 차지하고 있었고, 그 32 KB 는 실려 가서 버려졌다.
            //
            // **버리는 것이 아니라 미룬다.** 켜지는 순간 그 객체의 값을 전부 싣는다. 그러지 않으면
            // 적이 웨이브에 나올 때 독자가 그 HP 를 영영 모른다 — 값이 안 변했으면 델타에 안 실리고,
            // held 에 들어간 적도 없기 때문이다. 실제로 값만 빼 봤다가 렌더 대조에서 그것이 걸렸다.
            var silent = !live && !flipped;
            var everything = live && flipped;

            moved |= Where(text, transform, ledger, identity);

            moved |= Offered(text, transform, ledger, identity);

            // 컴포넌트별로 묶어 낸다. `on` 을 멤버마다 되풀이하지 않는다(ARTEL-540).
            text.Append(",\"by\":[");

            var written = 0;

            // 이 객체에서 각 타입이 몇 개나 지나갔는지. GameObject 가 한 behaviour 를 둘 나르는 것을 막는 것은 없고, 샘플 게임이
            // 그렇게 한다 — `CombineZone/Zone1` 에 `DropZone` 이 둘 있다. 이것이 없으면 그 둘이 장부에서 항목 하나를 나눠 갖는다:
            // 둘째가 첫째를 덮어쓰므로 첫째의 값은 다음 판독이 대고 비교할 것이 되지 못하고, 보고되지 않은 채 움직이거나 움직이지
            // 않았는데 움직였다고 보고된다.
            //
            // 한 단계 위에서 selector 가 이미 고친 것과 같은 결함이다. 거기서는 만들어진 적 다섯이 경로 하나를 나눠 갖고 판독마다
            // 그것들이 전부 바뀌었다고 했다. 객체는 셀 수 있게 만들어졌는데 그 위의 컴포넌트는 아니었다.
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

                // 둘째 이후만 표시하므로, 한 타입을 하나만 나르는 객체는 — 거의 전부가 그렇다 — 전과 정확히 같은 방식으로 키를 잡고
                // 읽힌다.
                var among = ordinal == 0 ? string.Empty : ordinal.ToString(Invariant) + "#";

                byOwner.TryGetValue(type, out var named);

                // 근거가 청한 것과, 같은 컴포넌트에서 읽을 수 있는 그 밖의 것. 아무도 청하지 않은 멤버도, 분석이 놓친 줄을 누군가 쓰는
                // 순간 누군가 청할 멤버다.
                var members = Readable.On(type, named);

                if (members == null)
                {
                    continue;
                }

                // 이 컴포넌트가 내놓은 것들. `on` 을 멤버마다 되풀이하지 않고 한 번만 쓰기 위해 모은다 —
                // 한 문서에서 `on` 316개 중 295개가 같은 값이었다.
                var mine = new StringBuilder(128);
                var count = 0;

                foreach (var member in members)
                {
                    var said = new StringBuilder(96);

                    said.Append('{');
                    Json.Property(said, "member", Named(member));

                    // `type` 은 싣지 않는다. 어셈블리 정규화된 이름이 멤버 하나에 200~350 B 인데
                    // 아무 독자도 읽지 않는다 — 에이전트의 `PulseMember` 에는 필드조차 없고, SDK 의
                    // 뷰어도 orchestration 도 만지지 않는다. 값의 모양으로 해석하지 이름으로 하지 않는다.

                    // 장부에 두는 것뿐 아니라 문서에도 말한다. 같은 타입과 같은 멤버의 이름을 댄 두 항목을 받은 독자는 각각이 그 객체의 어느
                    // 컴포넌트에서 왔는지 가릴 방법이 없다.
                    if (ordinal > 0)
                    {
                        said.Append(",\"among\":").Append(ordinal.ToString(Invariant));
                    }

                    if (!member.Asked)
                    {
                        said.Append(",\"asked\":false");
                    }

                    said.Append(',');

                    // 장부는 언제나 말한다. 안 보내는 것과 안 아는 것은 다르다 — 여기서 빼먹으면
                    // 다음 판독이 그 값을 처음 보는 것으로 알고 전부 변화라고 보고한다.
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
        /// 값, 또는 값이 없는 이유.
        /// </summary>
        /// <remarks>
        /// 읽을 때 던지는 필드는 0 이라는 값이 아니다. 파괴된 객체의 프로퍼티형 필드에 리플렉션을 걸면 실제로 던지고, 그 예외를
        /// 숫자로 보고하면 명세에 거짓 전제를 넣게 되는데 — 이 패키지 전체가 피하려고 짜인 유일한 실패가 그것이다.
        ///
        /// 참조는 그것이 무엇인지가 아니라 거기 있는지로 쓴다. <c>SaveLoadController</c> 가 쥔 것은 게임 자신의 데이터다. 어떤
        /// 조건이 그것을 <c>null</c> 과 비교한다는 것은 있음/없음으로 온전히 답해지고, 그 이상 가면 상태 채널이 세이브 파일의
        /// 덤프가 된다.
        /// </remarks>
        /// <returns>값이 이번 판독에 들어갈 때 참 — 움직였거나, 전부가 나가는 중이거나.</returns>
        private static bool Value(
            StringBuilder text, Watched member, Component on, Ledger ledger, string key,
            bool always = false)
        {
            // 장부가 실제로 나간 것을 정확히 쥘 수 있도록 먼저 옆에 써 둔다. 값이 아니라 조각을 비교한다는 것은 그 둘이 결코 어긋날 수
            // 없다는 뜻이다 — 데드밴드가 붙잡아 둔 좌표는 두 번째 규칙이 그래야 한다고 말해서가 아니라 그것이 *같은 텍스트이기
            // 때문에* 바뀌지 않은 것으로 읽힌다.
            var said = new StringBuilder(64);

            Read(said, member, on, ledger, key);

            // 쓰이든 쓰이지 않든 장부에는 말한다. 판독이 나르는 것과 판독이 아는 것은 다른 것이다: 가만히 있어서 빠진 값도 여전히
            // 기록돼야 하고, 그러지 않으면 다음 판독이 그것이 없다고 보고 그것을 변화라고 부른다.
            var kept = ledger.Keep(key, said.ToString());

            // `always` 는 이 객체가 방금 켜졌다는 뜻이다. 꺼져 있는 동안 값을 안 보냈으므로 독자는
            // 아무것도 모르고, 장부는 "안 바뀌었다" 고 말한다 — 그 둘이 겹치면 값이 영영 안 간다.
            if (!kept && !always)
            {
                return false;
            }

            text.Append(said);
            return true;
        }

        /// <summary>
        /// 판독이 멤버를 부르는 이름: 그것을 통해 찾아낸 필드가 아니라 청해진 그것.
        /// </summary>
        /// <remarks>
        /// 근거는 <c>IsStreaming</c> 을 청하고 찾아볼 자리는 <c>chatWindowController</c> 다. 판독을 필드의 이름으로 부르면 독자는
        /// <c>chatWindowController = true</c> 를 쥐게 되는데, 그것은 아무도 그것에 대고 줄을 쓴 적 없는 문장이다 — 게다가 크기를
        /// 보려고 읽은 목록과 그 자체로 읽은 목록이 둘 다 목록의 이름으로 불리면서 그 아래 값이 다르게 된다.
        ///
        /// 그래서 이름은 걸어간 경로다. 필드는 그 앞에 남는다. 두 객체가 같은 프로퍼티를 내놓을 수 있고 줄은 자기가 뜻하는 쪽의
        /// 이름을 대기 때문이다.
        /// </remarks>
        private static string Named(Watched member)
        {
            var found = member.Property ?? member.Member;

            return member.Via == null ? found : found + "." + member.Via;
        }

        /// <summary>
        /// 필드의 값에서 출발해 근거가 실제로 이름 댄 것까지 걷는다.
        /// </summary>
        /// <remarks>
        /// 이름마다 한 걸음이고 각각은 필드이거나 인자 없는 프로퍼티다. 분석이 적어 둔 그대로다. 여기서 고르는 것은 없다: 경로는
        /// 코드가 읽힐 때 결정됐고, 그것을 따라가는 일은 산수다.
        ///
        /// 거기 없는 걸음은 없는 값이 아니라 이름으로 보고한다. 난독화는 멤버의 이름을 바꾸고, null 로 답하는 판독은 게임의 입에
        /// 말을 넣는 일이 된다 — 읽을 수 없는 필드가 0 대신 <c>unread</c> 라고 말하는 것과 같은 이유다.
        /// </remarks>
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
                held = member.Field.GetValue(on);

                // 근거는 그 필드를 청한 것이 아니라 거기서 닿는 무언가를 청했다 — 목록의 개수이거나, 필드만 걷는 메서드였다면 돌려줬을
                // 것. 메서드를 부르는 대신 여기서 경로를 따라가는데, 그것이 게임을 감시하는 것과 게임을 하는 것의 차이 전부다.
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
                // Unity 는 동등성을 오버로드해 파괴된 객체가 없는 객체와 같지 않게 하고, null 과 비교하는 조건은 그 오버로드된 답을 뜻한다.
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
                // 개수이고 그 밖에는 없다. 컬렉션에 손을 뻗는 샘플 게임의 모든 조건이 그 안에 몇 개가 있는지를 묻고, 내용물은 게임 자신의
                // 데이터다.
                text.Append("\"count\":").Append(collection.Count.ToString(Invariant));
                return;
            }

            // 그것이 있다는 것이 아니라 그것이 무엇인지. 평범한 객체를 쥔 필드는 예전에 "있음" 으로 읽혔는데, 그것은 참조가 null 이
            // 아니라는 말밖에 하지 않는다 — 그리고 결코 null 이 아닌 참조는 모든 판독에서 같은 말을 하므로, 채널은 그 필드를 나르면서
            // 아무에게도 아무 말도 하지 않았다.
            //
            // 게임이 그런 필드에 무언가를 두는 목적이 그 구체 타입이다. 튜토리얼의 현재 단계, 상태 기계의 현재 상태, 전략, 핸들러:
            // 거기 서 있는 클래스가 *곧* 상태다. 샘플 게임은 튜토리얼 위치를 그런 필드 하나에 두는데 — 인터페이스 하나 뒤에 클래스
            // 열다섯 — 그중 무엇이 거기 있는지를 묻는 것이 근거가 부를 수 없었던 <c>IsMeetCondition()</c> 보다 많이 답한다. 이름은
            // 술어 하나가 마침 참이었는지가 아니라 어느 단계인지를 말하기 때문이다.
            //
            // 실패할 수 없고 틀릴 수 없는 리플렉션 호출 하나가 든다. 선언된 타입은 이미 멤버에 있고, 이것은 거기에 들어 있던 것이다.
            text.Append("\"value\":{");
            Json.Property(text, "is", held.GetType().FullName);
            text.Append('}');
        }

        /// <summary>
        /// 참조가 무엇을 가리키는가: 어느 객체이고, 어디 있는지.
        /// </summary>
        /// <remarks>
        /// 이것은 예전에 <c>"present"</c> 라고 말했는데, 그것은 채널이 존재하는 이유를 버리는 일이었다. 근거는 맵 커서가
        /// <c>MapMove.battle2.transform.position</c> 으로 옮겨 간다고 말하고 <c>character</c> 와 <c>battle2</c> 둘 다 필드다 —
        /// 그러니 실제로 묻고 있는 것은 이름 붙은 두 객체가 어디 있는지, 그리고 그중 하나가 다른 하나에 도착했는지다.
        ///
        /// 화면 녹화가 줄 수 없는 절반이 이것이다. 영상은 스프라이트가 어딘가에서 멈추는 것을 보여 준다. 그 스프라이트가
        /// <c>wordHead</c> 라 불리는 것도, 그 자리가 <c>battle2</c> 라 불리는 것도 모르므로, 방금 본 것이 명세가 이름 댄 그것임을
        /// 가릴 수 없다. 경로가 그 이름이고, 위치가 두 진술을 겹쳐 놓게 해 주는 것이다.
        ///
        /// 경로와 위치 둘 다이지 하나가 아니다. 경로만으로는 그것이 움직였다고 말할 수 없고 위치만으로는 무엇이 움직였는지 말할 수
        /// 없다.
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
                // 애셋이다 — 스프라이트, 클립, ScriptableObject. 화면 어딘가가 아니라 프로젝트 어딘가에 있으므로 그 이름이 할 수 있는
                // 말의 전부다.
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
        /// 게임 자신의 월드에서 객체가 어디 있는가.
        /// </summary>
        /// <remarks>
        /// 명세는 한 객체가 다른 객체가 있는 자리에 도착했는지를 묻고 그 둘 다 watch list 가 이미 읽는 이름 붙은 필드라는 이유로
        /// 지금까지 미뤄 왔다 — 그러니 아무의 근거도 언급하지 않는 객체의 위치는 어떤 줄에도 답하지 않았다.
        ///
        /// 그럼에도 판독을 화면 그림 위에 겹쳐 놓아야 하는 쪽은 그것을 청한다. 게임을 볼 수 있는 독자는 공통된 자리 없이는 자기가
        /// 보는 것과 자기가 들은 것을 이을 수 없고, 이것이 그중 가장 싼 것이다.
        ///
        /// 다른 좌표와 마찬가지로 데드밴드를 거쳐 정착시킨다. 그대로 읽은 transform 은 있던 자리에 정확히 앉아 있는 객체에 대해서도
        /// 마지막 소수 자리가 달라지고, 감시 대상 몇 개가 아니라 모든 객체에 위치가 붙으면 그것이 곧 판독 전체가 매 박자마다
        /// 게이트를 여는 일이 된다.
        ///
        /// 화면 위의 자리도 함께 싣는데, 이것은 예전에 그것을 거절했다. 반대 논거는 그려진 사각형을 원하는 독자가 이미 화면 캡처를
        /// 보고 있다는 것이었다 — 독자에 대해서는 참이고, 그것을 겨눠야 하는 쪽에 대해서는 거짓이다. 월드 단위는 게임 자신의
        /// 것이고 포인터는 픽셀로 가므로, 이것이 없으면 모든 액션이 엔진 밖의 누구도 할 수 없는 변환을 지고 간다.
        ///
        /// 한 번 말하고 둘 수 없다. 사각형은 무엇이든 움직이는 순간 낡고, 전량 상태는 화면이 바뀔 때만 나간다 — 그래서 두 화면
        /// 사이에서 독자는 화면이 나타났을 때 사물들이 있던 자리를 겨누게 된다.
        ///
        /// 제 데드밴드를 거치고, 그것은 픽셀로 잰다. 월드 쪽은 어느 패키지도 축척을 알 수 없는 단위에 대한 추측이지만, 픽셀은
        /// 어디서나 픽셀이고 그 하나 아래로는 아무것도 다르게 그려지지 않는다.
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
        /// 테스터가 지금 이 객체에 무엇을 할 수 있는가.
        /// </summary>
        /// <remarks>
        /// 게임이 무엇을 쥐고 있는지만 말하는 판독은 에이전트에게 화면의 상태 전체를 주면서 그 위의 무엇이 무엇에 답할지는 모르게
        /// 둔다. 명세는 계속 버튼을 누르라고 말하고, 판독은 그 버튼이 있고 켜져 있고 무언가에 연결돼 있음이 확인되는 자리여야 한다.
        ///
        /// 세 종류와 세 출처. 클릭은 인스펙터 배선이고, 한 타입의 두 객체가 서로 다르게 연결될 수 있으므로 지금 여기서 읽는다. 키와
        /// 포인터 핸들러는 컴파일된 코드 안에 있어 구울 때 타입에 대고 모아 두었고, 그 타입이 씬 안의 무엇에 붙어 있는 자리에서만
        /// 내놓는다 — 그것이 "<c>RightArrow</c> 가 무언가를 한다" 를 "<c>RightArrow</c> 가 *여기서* 무언가를 한다" 로 만든다.
        ///
        /// 판독마다 한 번이 아니라 객체에 쓴다. 테스터에게 필요한 것은 게임이 어디선가 읽는 키의 집합이 아니라, 그들이 누를 수 있는
        /// 것과 그것이 무엇에 붙어 있는지다.
        ///
        /// 버튼이 나타나거나 사라지거나 다시 연결되는 일이 소식이 되도록 장부에 말해 둔다. 모든 값이 가만히 있었지만 유일한 버튼이
        /// 방금 연결이 끊긴 화면은 바뀐 것이고, 그것을 건너뛴 판독은 아무 일도 없었다고 보고하는 셈이다.
        /// </remarks>
        /// <summary>
        /// 각 객체가 무엇을 내놓는 것으로 발견됐는지. 리플렉션 값을 한 번만 치르도록.
        /// </summary>
        /// <remarks>
        /// persistent call 을 읽는 일은 리플렉션이고, 스캔은 그것이 객체마다 한 번 답하고 기억할 종류라고 이미 정했다. 여기 들어가는
        /// 것 중 게임이 도는 동안 바뀌는 것은 없다: 인스펙터 배선은 직렬화된 데이터이고, 어떤 타입이 객체 위에 있는지는 그것이
        /// 만들어질 때 정해진다. 객체가 <em>보이고 있는지</em> 는 바뀌는데, 그것은 따로 말한다.
        ///
        /// 타입이 아니라 인스턴스에 대고 쥐고 있는다. 한 타입의 버튼 둘이 서로 다른 메서드에 연결돼 있는 일이 흔하고 그중 하나는
        /// 아무것에도 연결돼 있지 않을 수 있기 때문이다.
        ///
        /// 경계에서 통째로 버린다. <see cref="Worth"/> 가 하는 것과 같은 거래다: 한 시간 동안 만들어내는 게임은 그러지 않으면 여태
        /// 만든 객체마다 줄 하나씩을 늘린다.
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, string> Offers = new Dictionary<int, string>();

        /// <returns>이 객체가 내놓는 것이 판독에 들어갈 때 참.</returns>
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
                    // 컴포넌트 하나의 배선이지, 나머지가 내놓는 것을 잃을 이유가 아니다.
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
                // 아무것도 내놓지 않는 것으로 기억한다. 배선도 없고 감시 대상 타입도 없는 객체가 흔한 경우이고, 판독마다 그것을 다시 묻는
                // 것이 이 캐시가 피하려고 존재하는 값이다.
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
        /// 키와 그것이 하는 일을 함께 쓴다.
        /// </summary>
        /// <remarks>
        /// <c>clicks</c> 와 같은 모양(객체 배열)으로 맞춘다. 이름만 나르던 시절에는 씬의 키 다섯이
        /// 대등하게 실려 읽는 쪽이 어느 것을 눌러야 할지 알 수 없었다(ARTEL-539).
        ///
        /// 하는 일을 모르는 키는 <c>does</c> 를 아예 쓰지 않는다. 빈 배열은 "아무 일도 안 한다" 로 읽히는데
        /// 실제로는 "분석이 못 읽었다" 이고, 그 둘은 다음 수가 다르다.
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

            // 순서를 고정한다. 흔들리면 판독마다 그 자체가 차이로 보고된다.
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
        /// 참조가 라벨이나 그림일 때, 그것이 무엇을 보여 주고 있는가.
        /// </summary>
        /// <remarks>
        /// <c>TMP_Text</c> 타입의 필드는 이미 감시되고 있었고 이미 답해지고 있었다 — 그 라벨이 매달린 객체의 경로와 월드 위치로,
        /// 그것이 <see cref="Held"/> 가 주려고 만들어진 답이다. <c>MapMove.battle2</c> 에는 옳은 답이고 여기서는 틀린 답이다:
        /// 캡션이 어디 있는지 묻는 사람은 없고, 그것이 무엇이라 말하는지를 묻는다.
        ///
        /// 그래서 대신 참조에 그 내용을 청한다. 경로와 보이고 있는지는 남는다. 옳은 말을 쥐고 있으면서 꺼져 있는 캡션은 화면 위에
        /// 있는 캡션과 같은 주장이 아니기 때문이다. 월드 위치는 뺀다: 캡션의 좌표는 어떤 명세 줄에도 답하지 않고 마지막 소수 자리에서
        /// 떠도는 값 하나를 더할 뿐인데, 그것은 아무것도 아닌 것을 위해 열어 둔 게이트다.
        ///
        /// 거기 적힌 이유로, 컴파일 대상으로 삼는 대신 <see cref="SceneEvidenceScan"/> 을 거쳐 타입 이름으로 맞춘다 — uGUI 와
        /// TextMeshPro 는 프로젝트에 없을 수 있는 패키지이고 이 어셈블리는 둘 다 참조하지 않는다.
        /// </remarks>
        /// <returns>참조가 라벨이나 그림이었고 그것이 쓰였을 때 참.</returns>
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
        /// animator 가 무엇을 하고 있는가: 그것이 있는 상태를, 가능할 때 이름과 함께.
        /// </summary>
        /// <remarks>
        /// 명세는 트리거가 발동하고 화면이 무언가 움직이는 것을 보인다고 말한다. 어느 쪽도 홀로 그 움직이는 것이 그 줄이 말하는
        /// 상태로 들어갔다고 말하지 않는다. 그 둘을 잇는 것이 이것이다.
        ///
        /// Unity 는 현재 상태에 대해 해시를 돌려주고 그것을 말로 바꿔 주는 것은 없으므로, 이름은 반대쪽 끝에서 도달한다 — 분석이
        /// 코드가 animator 에 건네는 모든 이름을 적어 두었고, <c>IsName</c> 이 그 상태가 그중 하나로 불리는지에 답한다. 해시는
        /// 어느 쪽이든 나간다. 코드가 이름을 한 번도 언급하지 않은 상태도 여전히 바뀐 상태이고 독자는 그 숫자가 움직이는 것을 볼 수
        /// 있기 때문이다.
        ///
        /// 트리거의 이름과 상태의 이름은 같은 것이 아니다. 게임들은 흔히 하나를 다른 것으로 쓰지만 무엇도 그것을 강제하지 않는다.
        /// 이름은 Unity 가 확인해 준 자리에서만 쓰므로, 그것들을 다르게 이름 짓는 게임은 틀린 말 대신 해시를 받는다.
        ///
        /// 매개변수는 읽지 않는다. 트리거는 설정된 뒤 한 프레임 안에 상태 기계가 소비하므로 초당 열 번의 판독은 거의 언제나 그것을
        /// 거짓으로 보고하게 된다 — 대개 틀린 값은 없는 값보다 나쁘다.
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
                // 컨트롤러가 없거나 레이어 0 이 없는 animator. 그것은 존재하면서 아무것도 하지 않고 있고, 그것은 그것이 없는 것과 다른
                // 사실이다.
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
        /// 이 animator 가 답할 이름들.
        /// </summary>
        /// <remarks>
        /// <c>Attack</c> 트리거가 발동한다고 말하는 줄은 코드의 <c>SetTrigger("Attack")</c> 에서 쓰였는데, 지금까지 그 객체 위의
        /// animator 에 그 이름의 매개변수가 있는지는 아무것도 확인하지 않았다. 오타, 다른 것으로 바꾼 컨트롤러, 이름을 바꾼
        /// 트리거 — 코드는 여전히 컴파일되고 애니메이션은 조용히 영영 재생되지 않는데, 그것이 정확히 명세가 잡으려고 존재하는
        /// 종류의 결함이다.
        ///
        /// 코드가 언급한 것만이 아니라 전부를 본다. 일치하는 것만 보고하면 빈 답이 서로 다른 두 가지를 뜻하게 되고 — 이 animator 에
        /// 그 이름들이 하나도 없거나, 컨트롤러가 아예 묶이지 않아 매개변수가 하나도 없거나 — 그 둘을 가리지 못하는 판독은 이
        /// 패키지가 다른 모든 자리에서 거절하는 모양이다.
        ///
        /// 값이 아니라 이름이다. 트리거는 설정된 뒤 한 프레임 안에 상태 기계가 소비하므로 초당 열 번 읽으면 거의 언제나 거짓으로
        /// 보고되고, 대개 틀린 값은 없는 값보다 나쁘다. 움직임이 구동하는 float 매개변수도 어떤 조건도 언급하지 않는 이유로 매
        /// 박자마다 변화 게이트를 열 것이다. 매개변수가 무엇을 쥐고 있는지는 화면이 보여 줄 몫이고, 그것이 무엇이라 불리는지는
        /// 여기서만 알 수 있다.
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
        /// float 이 소수점 아래 몇 자리를 지키는지.
        /// </summary>
        /// <remarks>
        /// 변화 게이트가 이 문서를 해싱하므로, 날것의 float 은 숨 쉬는 idle 애니메이션을 상태 변화로 만들고 페이로드가 매 틱 나간다.
        /// 반올림이 게이트를 쓸 수 있게 만드는 것이고, 네 자리는 근거가 하는 어떤 비교보다도 곱다.
        /// </remarks>
        private const int Decimals = 4;

        private static void Number(StringBuilder text, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // 숫자가 아니고 JSON 으로 쓸 수도 없다. 0 으로 바꾸지 않고 그렇다고 말한다.
                Json.Property(text, "unread", "not-a-number");
                return;
            }

            text.Append("\"value\":").Append(Math.Round(value, Decimals).ToString("0.####", Invariant));
        }
    }
}
