using System;
using System.Collections.Generic;
using System.Text;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 어셈블리의 모든 variant 가 참조하는 감시 대상을 watch list 문서 하나로 쓴다.
    /// </summary>
    /// <remarks>
    /// 어셈블리 단위로 모은다. GameObject 에 붙지 않는 타입의 static 필드도 감시 대상이 되어야 한다.
    ///
    /// 같은 어셈블리에서 같은 바이트가 나오도록 정렬한다.
    /// </remarks>
    internal static class WatchListJson
    {
        /// <summary>
        /// watch list 문서의 schema 버전.
        /// </summary>
        /// <remarks>
        /// `evidence` 문서와 읽는 쪽이 달라 버전을 따로 둔다.
        /// </remarks>
        internal const int SchemaVersion = 1;

        /// <summary>쓰는 멤버의 최대 수. 넘친 수는 <c>dropped</c> 로 적는다.</summary>
        /// <remarks>
        /// 폴링마다 읽으므로 상한을 둔다. <c>dropped</c> 가 있어 잘린 목록을 완전한 목록으로 오해하지 않는다.
        /// </remarks>
        private const int MaxMembers = 1024;

        /// <summary>
        /// 쓴 문서와 감시한 수, 감시할 수 없던 수.
        /// </summary>
        /// <remarks>
        /// 감시할 수 없는 항목은 개수만 센다. 개별 항목은 `evidence` 에 이미 있다.
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

                    // 대부분의 효과는 출처가 없으므로 없을 때 unwatchable 로 세지 않는다.
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

            // Unity 는 상태 해시를 이름으로 돌려주지 않으므로 `pulse` 가 `IsName` 으로 시험할 후보 이름을 싣는다.
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
        /// 한 타입에서 플레이어가 쓸 수 있는 입력. 경우마다가 아니라 타입마다 모은다.
        /// </summary>
        /// <remarks>
        /// 버튼은 스캔이 persistent call 로 이미 찾는다. 여기서는 코드에서만 알 수 있는 키 입력과 포인터
        /// 핸들러를 모은다. `pulse` 는 화면에 있는 객체의 타입에 대해서만 이것을 내보낸다.
        /// </remarks>
        private sealed class Offer
        {
            /// <summary>키 이름 → 그 키가 하는 일. 값이 비면 하는 일을 모른다는 뜻이다.</summary>
            /// <remarks>
            /// 키 순서가 `pulse` 마다 달라지면 변화로 보고되므로 정렬된 사전을 쓴다.
            /// </remarks>
            internal readonly SortedDictionary<string, SortedSet<string>> Keys =
                new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            internal readonly List<string> Pointers = new List<string>();
        }

        /// <summary>
        /// 테스터가 포인터로 일으킬 수 있는 엔진 메시지들.
        /// </summary>
        /// <remarks>
        /// <c>OnTriggerEnter2D</c> 같은 물리 메시지는 테스터가 직접 일으킬 수 없으므로 뺀다.
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
                // GameObject 에 붙지 않는 타입은 `pulse` 가 걷지 않으므로 입력으로 내보낼 수 없다.
                return;
            }

            if (!offers.TryGetValue(owner, out var offer))
            {
                offer = new Offer();
                offers[owner] = offer;
            }

            var gestures = new List<InputRead>();
            variant.When.CollectGestures(gestures, new HashSet<Condition>());

            // 같은 키가 여러 경우에서 읽히면 효과를 모두 모은다. 어느 경우를 탈지는 런타임 조건이 정한다.
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
        /// 이 경우의 효과 요약.
        /// </summary>
        /// <remarks>
        /// `pulse` 마다 객체별로 실리므로 씬 이동과 필드 쓰기만 짧게 싣는다. 나머지는 <c>inspect</c> 로 묻는다.
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

                // 씬 이동은 "이 키가 어디로 데려가는가" 에 답하므로 우선한다.
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
            // 호출 경로가 갈렸다 다시 만나면 같은 효과가 두 번 나온다.
            if (said.Count < MaxEffectsPerKey && !said.Contains(what))
            {
                said.Add(what);
            }
        }

        /// <summary>키 하나에 적는 최대 효과 수.</summary>
        private const int MaxEffectsPerKey = 3;

        /// <summary>assembly|type|name|signature 형식의 id 에서 메서드 이름을 꺼낸다.</summary>
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
        /// 키와 그 효과를 문자열 하나로 쓴다. 배열은 평평하게 둔다.
        /// </summary>
        /// <remarks>
        /// <c>WatchList.Entries</c> 는 항목 끝을 첫 <c>}</c> 로 찾으므로(제네릭 타입 이름 때문에 괄호를 셀 수
        /// 없다) 키를 객체로 쓰면 항목이 잘린다.
        ///
        /// 구분자 <c>\u0001</c> 은 식별자와 씬 이름에 나타나지 않는다.
        /// <see cref="UnityPlayMcp.Affordances.Live.WatchList"/> 가 `pulse` 로 보낼 때 다시 나눈다.
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

            // 같은 멤버는 하나로 합친다. Via 가 있는 항목을 남긴다.
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
