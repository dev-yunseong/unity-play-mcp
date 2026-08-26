using System;
using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 어셈블리의 근거가 누군가에게 보라고 청하는 것의 전부. 한 번에 쓴다.
    /// </summary>
    /// <remarks>
    /// 타입 단위가 아니라 어셈블리 단위인 것은 물음의 모양이 그렇기 때문이다. 감시자는 컴포넌트에서
    /// 출발해 거기서 무엇을 읽을지 묻지 않는다. 아무것도 없는 데서 출발해 이 게임에서 도대체 무엇이 읽을
    /// 값이 있는지를 묻고, 그 답에는 어떤 GameObject 도 나르지 않는 타입의 멤버가 들어간다 — 맵
    /// 컨트롤러의 static 필드 하나가 화면 다섯을 결정하면서 아무것에도 매달려 있지 않다.
    ///
    /// 이것은 합집합이고 작다. 샘플 게임의 모든 조건이 서로 다른 값 일흔 남짓 중 하나를 부르고 모든 효과가
    /// 백쉰 남짓 중 하나를 부르는데, 양쪽 대부분은 같은 몇 개를 다른 분기에서 다시 말한 것이다. 나오는
    /// 것은 폴링마다 읽을 만큼 짧고 사람이 눈으로 확인할 만큼 짧으며, 중요한 성질이 그것이다: 아무도
    /// 감사할 수 없는 목록은 아무도 믿을 수 없는 목록이다.
    ///
    /// 정렬한다. 같은 어셈블리를 두 번 분석하면 같은 바이트가 나오도록. 근거 리소스를 타임스탬프 없이
    /// deflate 하는 것도 같은 이유이고, 해시 순서로 순회한 집합은 그것을 조용히 무위로 돌렸을 것이다.
    /// </remarks>
    internal static class WatchListJson
    {
        /// <summary>
        /// 하나: 조건과 효과가 부르는 멤버들을, 선언 타입과 멤버로 적는다.
        /// </summary>
        /// <remarks>
        /// 근거 문서의 번호가 아니라 제 번호를 쓴다. 둘은 같은 패스가 쓰지만 서로 다른 독자가 서로 다른 이유로
        /// 읽고, 이 목록을 이해하는 감시자는 기록이 어떻게 생겼는지에 대해 아무 의견도 없다.
        /// </remarks>
        internal const int SchemaVersion = 1;

        /// <summary>나머지를 빼고 뺐다고 말하기 전까지 몇 개의 멤버를 쓰는지.</summary>
        /// <remarks>
        /// 폴링마다 읽으므로 한계를 둔다. 이백이면 샘플 게임 전체다. 서로 다른 값 천 개를 한꺼번에 감시하라는
        /// 게임은 덤프를 청하는 것이고, 덤프는 이것이 피하려고 존재하는 바로 그것이다. 무엇을 버렸는지는
        /// 말하므로, 짧은 목록이 완전한 목록으로 오해되는 일은 없다.
        /// </remarks>
        private const int MaxMembers = 1024;

        /// <summary>
        /// 분석이 감시할 것으로 찾아낸 것, 그리고 거절한 것.
        /// </summary>
        /// <remarks>
        /// 거절은 세되 이름을 대지 않는다. <c>spellCards.Count</c> 나 <c>CompareTag("Spell")</c> 에 대한
        /// 조건은 호출이 만들어내는 것이고 그 뒤에 읽을 멤버가 없다 — 알아보려고 그것을 부르는 일은 게임을
        /// 감시하는 게 아니라 게임을 하는 것이다. 개수는 감시자가 리포트의 어디까지 제 전제를 확인할 수
        /// 있는지를 말해 준다. 개별 항목은 이미 근거 안에 낱낱이 적혀 있고, 있어야 할 자리가 거기다.
        /// </remarks>
        internal sealed class Result
        {
            internal string Document;
            internal int Watched;
            internal int Unwatchable;
        }

        internal static Result Write(IEnumerable<Variant> variants)
        {
            var found = new Dictionary<string, WatchTarget>(StringComparer.Ordinal);
            var names = new List<string>();
            var unwatchable = 0;

            var offers = new Dictionary<string, Offer>(StringComparer.Ordinal);

            foreach (var variant in variants)
            {
                Taking(variant, offers);

                Gather(variant.When, found, ref unwatchable);

                foreach (var outcome in variant.Outcomes)
                {
                    Take(outcome.Watch, found, ref unwatchable);

                    // 없다고 해서 거절로 세지 않는다. 대부분의 효과는 값을 어디선가 옮겨 오는 게 아니라 어딘가에 넣는
                    // 것이고, 출처가 없는 것은 읽지 못한 무언가가 아니라 평범한 경우다.
                    if (outcome.WatchSource != null)
                    {
                        Take(outcome.WatchSource, found, ref unwatchable);
                    }

                    if (outcome.AnimatorName != null && !names.Contains(outcome.AnimatorName))
                    {
                        names.Add(outcome.AnimatorName);
                    }
                }
            }

            var keys = new List<string>(found.Keys);
            keys.Sort(StringComparer.Ordinal);

            var text = new StringBuilder(1024);
            text.Append("{\"schema\":").Append(SchemaVersion).Append(",\"watch\":[");

            var written = 0;

            foreach (var key in keys)
            {
                if (written >= MaxMembers)
                {
                    break;
                }

                if (written > 0)
                {
                    text.Append(',');
                }

                var target = found[key];
                text.Append('{');
                Property(text, "declaring", target.Declaring);
                text.Append(',');
                Property(text, "member", target.Member);
                text.Append(',');

                if (target.Property != null)
                {
                    Property(text, "property", target.Property);
                    text.Append(',');
                }

                if (target.Via != null)
                {
                    Property(text, "via", target.Via);
                    text.Append(',');
                }

                Property(text, "type", target.Type);
                text.Append(",\"static\":").Append(target.Static ? "true" : "false");
                text.Append('}');
                written++;
            }

            text.Append(']');

            // 코드가 animator 에 건네는 모든 이름. 그래야 판독이 그 상태가 그중 하나로 불리는지 물을 수 있다.
            // Unity 는 `IsName` 에는 답하지만 해시를 말로 되돌려 주는 것은 없으므로, 이 목록이 없으면 판독은
            // animator 가 바뀌었다고만 말하고 무엇으로 바뀌었는지는 영영 말하지 못한다.
            names.Sort(StringComparer.Ordinal);
            text.Append(",\"animatorNames\":[");

            for (var at = 0; at < names.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                EvidenceJson.String(text, names[at]);
            }

            text.Append(']');

            Offers(text, offers);

            text.Append(",\"unwatchable\":").Append(unwatchable);

            if (keys.Count > written)
            {
                text.Append(",\"dropped\":").Append(keys.Count - written);
            }

            text.Append('}');

            return new Result { Document = text.ToString(), Watched = written, Unwatchable = unwatchable };
        }

        /// <summary>
        /// 플레이어가 어떤 타입을 움직이게 만들 수 있는 방법들. 경우마다가 아니라 타입마다 모은다.
        /// </summary>
        /// <remarks>
        /// 판독은 게임이 무엇을 쥐고 있는지를 말한다. 테스터가 다음에 무엇을 할 수 있는지는 말하지 않고, 그것
        /// 없이는 채널을 읽는 에이전트가 한 화면의 상태를 통째로 쥐고서도 그 위의 무엇이 무엇에 답할지를
        /// 모른다.
        ///
        /// 버튼은 스캔이 이미 찾는다. 지속 호출은 테스터가 볼 수 있는 배선이기 때문이다. 스캔이 볼 수 없는
        /// 둘이 여기 있다: 이 화면에서 어떤 키가 뜻을 가지는가, 그리고 어떤 객체가 포인터에 답하는가. 둘 다
        /// 컴파일된 코드에서만 알 수 있고 — 키는 메서드 안의 리터럴이고, 드래그 핸들러는 엔진이 부르는 메서드
        /// 이름이다 — 둘 다 추상적으로는 쓸모가 없다. 게임이 어딘가에서 <c>RightArrow</c> 를 읽는다는 것을
        /// 아는 일은, 지금 그것을 눌러서 무슨 일이 일어나는지를 아는 일이 아니다.
        ///
        /// 그래서 그것들을 나르는 타입에 대고 모은다. 판독은 실제로 화면에 있는 객체들을 걷고, 그중 어느
        /// 것에도 없는 타입은 아무것도 내놓지 않는다.
        /// </remarks>
        private sealed class Offer
        {
            /// <summary>키 이름 → 그 키가 하는 일. 값이 비면 무엇을 하는지 모른다는 뜻이다.</summary>
            /// <remarks>
            /// 이름만 담던 자리다. 그때는 테스터가 다섯 키를 받아도 어느 것이 무엇을 하는지 알 길이 없었고,
            /// 실제로 Map 씬의 QA 가 그래서 전투에 진입하지 못했다 — 화살표를 눌러 보고 화면이 안 바뀌자
            /// 진입할 수 없다고 판단했다. 근거는 `Return` 이 씬을 바꾼다는 것을 알고 있었다.
            ///
            /// 정렬된 사전인 이유: 키 순서가 판독마다 흔들리면 그 자체가 차이로 보고된다.
            /// </remarks>
            internal readonly SortedDictionary<string, SortedSet<string>> Keys =
                new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            internal readonly List<string> Pointers = new List<string>();
        }

        /// <summary>
        /// 테스터가 포인터로 일으킬 수 있는 엔진 메시지들.
        /// </summary>
        /// <remarks>
        /// 분석이 따라가는 메시지의 부분집합이고, 일부러 전부는 아니다. <c>OnTriggerEnter2D</c> 는 진입점이며
        /// 누가 무엇을 해서가 아니라 발사체가 도착해서 닿는다 — 그것을 입력으로 내놓으면 어떤 테스터도 수행할
        /// 수 없는 단계를 테스트에 넣게 된다.
        /// </remarks>
        private static readonly HashSet<string> Pointered = new HashSet<string>(StringComparer.Ordinal)
        {
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag",
            "OnMouseEnter", "OnMouseExit", "OnMouseOver",
            "OnPointerClick", "OnPointerDown", "OnPointerUp", "OnPointerEnter", "OnPointerExit",
            "OnBeginDrag", "OnDrag", "OnEndDrag", "OnDrop", "OnScroll"
        };

        private static void Taking(Variant variant, Dictionary<string, Offer> offers)
        {
            var owner = variant.Owner == null ? null : variant.Owner.FullName;

            if (owner == null)
            {
                // 어떤 GameObject 도 나르지 않는 타입 위의 경우는 판독이 걷는 무엇에 대고도 내놓을 수 없다. 아무 데도
                // 세지 않는다. 그것이 내놓았을 것은 애초에 테스터에게 주어질 입력이 아니었기 때문이다.
                return;
            }

            if (!offers.TryGetValue(owner, out var offer))
            {
                offer = new Offer();
                offers[owner] = offer;
            }

            var gestures = new List<InputRead>();
            variant.When.CollectGestures(gestures, new HashSet<Condition>());

            // 이 갈래가 무엇을 하는지. 같은 키가 여러 갈래에서 읽히면 그 전부가 모인다 —
            // 어느 갈래를 타는지는 런타임 조건이 정하고 분석은 그것을 모른다.
            var does = Does(variant);

            foreach (var gesture in gestures)
            {
                var said = gesture.ToString();

                if (!offer.Keys.TryGetValue(said, out var effects))
                {
                    effects = new SortedSet<string>(StringComparer.Ordinal);
                    offer.Keys[said] = effects;
                }

                foreach (var effect in does)
                {
                    effects.Add(effect);
                }
            }

            var entry = Method(variant.EntryId);

            if (entry != null && Pointered.Contains(entry) && !offer.Pointers.Contains(entry))
            {
                offer.Pointers.Add(entry);
            }
        }

        /// <summary>
        /// 이 갈래를 타면 무엇이 일어나는가. 읽는 사람이 고를 수 있을 만큼만 짧게.
        /// </summary>
        /// <remarks>
        /// 효과 전부를 옮기지 않는다. 판독은 초당 열 번 나가고 이 목록은 객체마다 붙으므로, 근거 문서를
        /// 그대로 실으면 크기가 그쪽에 매인다. 여기서 고르는 것은 <b>테스터가 화면에서 확인할 수 있는
        /// 것</b>이다 — 씬이 바뀐다, 어떤 상태가 쓰인다. 나머지는 <c>inspect</c> 로 물을 수 있고,
        /// 물어야 알 만한 것이기도 하다.
        /// </remarks>
        private static List<string> Does(Variant variant)
        {
            var said = new List<string>();

            foreach (var outcome in variant.Outcomes)
            {
                if (outcome == null || outcome.Kind == null)
                {
                    continue;
                }

                // 씬 이동이 가장 크게 읽히는 결과다. 그것 하나로 "이 키가 나를 어디로 데려가는가" 에
                // 답할 수 있고, QA 시나리오가 대개 그것을 묻는다.
                if (outcome.Kind == "scene" && outcome.Target != null)
                {
                    Remember(said, "→ " + outcome.Target);
                    continue;
                }

                if (outcome.Kind == "write" && outcome.Target != null)
                {
                    Remember(said, "sets " + outcome.Target);
                }
            }

            return said;
        }

        private static void Remember(List<string> said, string what)
        {
            // 같은 갈래가 같은 말을 두 번 하는 일이 흔하다. 호출 경로가 갈렸다가 다시 만나면 그렇다.
            if (said.Count < MaxEffectsPerKey && !said.Contains(what))
            {
                said.Add(what);
            }
        }

        /// <summary>키 하나에 적어 두는 효과의 수. 넘는 것은 적지 않는다 — 고르는 데 필요한 만큼이다.</summary>
        private const int MaxEffectsPerKey = 3;

        /// <summary>writer 가 assembly|type|name|signature 로 만든 id 에서 꺼낸 메서드 자신의 이름.</summary>
        private static string Method(string entryId)
        {
            if (entryId == null)
            {
                return null;
            }

            var parts = entryId.Split('|');

            return parts.Length < 3 ? null : parts[2];
        }

        private static void Offers(StringBuilder text, Dictionary<string, Offer> offers)
        {
            var owners = new List<string>(offers.Keys);
            owners.Sort(StringComparer.Ordinal);

            text.Append(",\"inputs\":[");

            var written = 0;

            foreach (var owner in owners)
            {
                var offer = offers[owner];

                if (offer.Keys.Count == 0 && offer.Pointers.Count == 0)
                {
                    continue;
                }


                if (written > 0)
                {
                    text.Append(',');
                }

                text.Append('{');
                Property(text, "declaring", owner);
                text.Append(",\"keys\":[");
                Keyed(text, offer.Keys);
                text.Append("],\"pointers\":[");
                Flat(text, offer.Pointers);
                text.Append("]}");
                written++;
            }

            text.Append(']');
        }

        /// <summary>
        /// 키마다 무엇을 하는지 함께 쓴다. 배열은 평평하게 두고 효과를 문자열 안에 싣는다.
        /// </summary>
        /// <remarks>
        /// 객체 배열로 쓰고 싶은 모양이지만 못 쓴다. 이 문서를 읽는 <c>WatchList.Entries</c> 는 항목의 끝을
        /// 첫 <c>}</c> 로 찾는데 — 필드가 제 대괄호를 나르는 제네릭 타입 이름을 쥐어서 괄호를 셀 수 없기
        /// 때문이다 — 키를 객체로 만들면 항목이 첫 키에서 잘린다. 실제로 그렇게 만들었다가 키가 통째로
        /// 사라졌다.
        ///
        /// 그래서 구분자를 쓴다. <c>\u0001</c> 은 식별자에도 씬 이름에도 나타날 수 없고 JSON 에서
        /// 이스케이프된다. 판독으로 나갈 때 <see cref="Artel.Affordances.Live.WatchList"/> 가 다시 가른다.
        /// </remarks>
        private static void Keyed(
            StringBuilder text, SortedDictionary<string, SortedSet<string>> keys)
        {
            var written = 0;

            foreach (var pair in keys)
            {
                if (written > 0)
                {
                    text.Append(',');
                }

                var said = new StringBuilder(pair.Key);

                foreach (var effect in pair.Value)
                {
                    said.Append('\u0001').Append(effect);
                }

                EvidenceJson.String(text, said.ToString());
                written++;
            }
        }

        private static void Flat(StringBuilder text, List<string> said)
        {
            said.Sort(StringComparer.Ordinal);

            for (var at = 0; at < said.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                EvidenceJson.String(text, said[at]);
            }
        }

        private static void Gather(
            Condition condition, Dictionary<string, WatchTarget> found, ref int unwatchable)
        {
            if (condition == null)
            {
                return;
            }

            if (condition.Kind == ConditionKind.Test)
            {
                Take(condition.Test?.Watch, found, ref unwatchable);
                return;
            }

            if (condition.Parts == null)
            {
                return;
            }

            foreach (var part in condition.Parts)
            {
                Gather(part, found, ref unwatchable);
            }
        }

        private static void Take(
            WatchTarget target, Dictionary<string, WatchTarget> found, ref int unwatchable)
        {
            if (target == null)
            {
                unwatchable++;
                return;
            }

            // 두 갈래로 닿은 같은 멤버는 읽을 것 하나다. 분기 여덟 곳에서 검사된 필드는 조건 여덟에 볼 자리
            // 하나이고, 그 두 숫자의 차이가 이 목록이 짧은 이유 전부다.
            //
            // 읽힌 것이 무엇인지를 말하는 항목 쪽을 남긴다. 자기 자신으로도 감시되고 개수로도 감시되는 목록은
            // 어느 쪽이든 읽을 멤버 하나이며, 개수는 그러지 않으면 독자가 스스로 계산해야 했을 부분이다.
            if (!found.TryGetValue(target.Key, out var already) || already.Via == null)
            {
                found[target.Key] = target;
            }
        }

        private static void Property(StringBuilder text, string name, string value)
        {
            EvidenceJson.String(text, name);
            text.Append(':');
            EvidenceJson.String(text, value);
        }
    }
}
