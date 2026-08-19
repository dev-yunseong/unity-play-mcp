using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>런타임 스캐너를 위해 유계이고 결정적인 근거 문서 하나를 쓴다.</summary>
    internal static class EvidenceJson
    {
        /// <summary>
        /// 여섯: 기록은 무엇이 자신에게 닿는지를 말한다.
        /// </summary>
        /// <remarks>
        /// 셋은 호출 엣지에 수신자와 인자를 더하고 구독을 써냈다. 넷은 어디서 닿았는지만 다른 기록들을 하나로
        /// 접고 나머지 갈래를 <c>alsoReachedBy</c> 에 넣는다.
        ///
        /// 버전 둘 문서가 가지고 있던 필드는 전부 같은 자리에 같은 뜻으로 남아 있으므로, 이것을 읽기 위해
        /// 다시 써야 할 것은 없다. 번호가 움직이는 이유는 문서가 *빠뜨린 것* 의 뜻이 달라졌기 때문이다:
        /// <c>handles</c> 를 무시하는 독자는 그 타입이 아무것도 구독하지 않는다고 결론짓고,
        /// <c>alsoReachedBy</c> 를 무시하는 독자는 어떤 효과에 이르는 갈래를 실제보다 적게 본다. 자기가
        /// 무엇을 쥐고 있는지 말해 주는 것은 버전뿐이다.
        ///
        /// 여섯은 <c>calledBy</c> 를 더한다: 무엇이 이 기록에 닿는가 — 기록 자신의 호출로는 결코 말할 수 없는
        /// 것이다. 더하기만 하고, 그것을 나르는 문서와 함께 움직이지 한 버전 뒤에 처지지 않는다. 한 파일이
        /// 자신이 어떤 모양인지에 대해 두 개의 답을 쥐고 있어서는 안 된다.
        /// </remarks>
        internal const int SchemaVersion = 6;
        private const int MaxItems = 64;
        private const int MaxConditionNodes = 512;
        private const int MaxConditionDepth = 64;

        internal static string Write(Variant variant, out bool truncated)
        {
            return Write(variant, null, out truncated);
        }

        internal static string Write(
            Variant variant, Dictionary<string, List<string>> callers, out bool truncated)
        {
            truncated = false;
            var text = new StringBuilder(1024);
            text.Append('{');
            Property(text, "schema", SchemaVersion);
            text.Append(',');
            Property(text, "owner", variant.Owner?.FullName);
            text.Append(',');
            Property(text, "entry", variant.Entry);
            text.Append(',');
            Property(text, "entryId", variant.EntryId);
            text.Append(',');
            Property(text, "source", variant.Method);
            text.Append(',');
            Property(text, "methodId", variant.MethodId);
            text.Append(',');
            Property(text, "recordKind", variant.RecordKind);
            text.Append(',');
            Property(text, "triggerKind", variant.TriggerKind);
            text.Append(',');
            Property(text, "confidence", Confidence(variant));
            if (variant.LoopsBackTo >= 0)
            {
                text.Append(',');
                Property(text, "loopsBackTo", variant.LoopsBackTo);
            }

            if (variant.HandedAt >= 0)
            {
                text.Append(',');
                Property(text, "handedOverAt", variant.HandedAt);
                text.Append(',');
                Property(text, "handedOverIn", variant.HandedIn);

                // 무엇이 그것을 가져갔는가. `WaitUntil` 에 넘긴 술어는 그것이 참이 될 때까지 멈춰 선 코루틴이고,
                // 같은 술어를 콜백 목록에 넘긴 것은 그것이 아니다.
                if (variant.HandedTo != null)
                {
                    text.Append(',');
                    Property(text, "handedOverTo", variant.HandedTo);
                }
            }

            // 무엇이 여기에 닿는가. 기록 자신의 호출로는 말할 수 없다: 그것들은 나가는 엣지이고, 제 기록이 없는
            // 호출자는 그중 하나도 남기지 않는다.
            if (callers != null && variant.EntryId != null &&
                callers.TryGetValue(variant.EntryId, out var reachedBy))
            {
                text.Append(",\"calledBy\":");
                Strings(text, reachedBy, ref truncated);
            }

            text.Append(",\"callPath\":");
            Strings(text, variant.CallPath, ref truncated);
            text.Append(",\"condition\":");
            var nodes = MaxConditionNodes;
            Condition(text, variant.When, 0, ref nodes, ref truncated);
            text.Append(",\"inputs\":[");
            var inputCount = System.Math.Min(variant.Inputs.Count, MaxItems);
            truncated |= variant.Inputs.Count > MaxItems;
            for (var index = 0; index < inputCount; index++)
            {
                if (index > 0) text.Append(',');
                var input = variant.Inputs[index];
                text.Append('{');
                Property(text, "kind", input.Gesture);
                text.Append(',');
                Property(text, "control", input.Name);
                text.Append(',');
                Property(text, "phase", input.Phase);
                text.Append(',');
                Property(text, "absent", input.Absent);
                text.Append(',');
                Property(text, "offset", input.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"effects\":[");
            var effectCount = System.Math.Min(variant.Outcomes.Count, MaxItems);
            truncated |= variant.Outcomes.Count > MaxItems;
            for (var index = 0; index < effectCount; index++)
            {
                if (index > 0) text.Append(',');
                var effect = variant.Outcomes[index];
                text.Append('{');
                Property(text, "kind", effect.Kind);
                text.Append(',');
                Property(text, "category", effect.Category);
                text.Append(',');
                Property(text, "target", effect.Target);

                // 대상이 될 수 있었던 값들. 원본이 그중에서 골랐을 때만 나온다. 그러니 이것을 본 독자는 위의 이름
                // 하나가 답이 아니라는 것을 안다.
                if (effect.TargetCandidates != null && effect.TargetCandidates.Count > 0)
                {
                    text.Append(",\"targetCandidates\":");
                    Strings(text, effect.TargetCandidates, ref truncated);
                }

                text.Append(',');
                Property(text, "detail", effect.Detail);
                text.Append(',');
                Property(text, "source", variant.Method);
                text.Append(',');
                Property(text, "offset", effect.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"calls\":[");
            var callCount = System.Math.Min(variant.Calls.Count, MaxItems);
            truncated |= variant.Calls.Count > MaxItems;
            for (var index = 0; index < callCount; index++)
            {
                if (index > 0) text.Append(',');
                var call = variant.Calls[index];
                text.Append('{');
                Property(text, "targetId", call.TargetId);
                text.Append(',');
                Property(text, "target", call.Target);
                text.Append(',');
                Property(text, "receiver", call.Receiver);
                text.Append(',');
                Property(text, "receiverWhere", call.ReceiverWhere);
                text.Append(',');
                Property(text, "args", call.Arguments);
                text.Append(',');
                Property(text, "offset", call.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"handles\":[");
            var handleCount = System.Math.Min(variant.Handles.Count, MaxItems);
            truncated |= variant.Handles.Count > MaxItems;
            for (var index = 0; index < handleCount; index++)
            {
                if (index > 0) text.Append(',');
                var handled = variant.Handles[index];
                text.Append('{');
                Property(text, "channel", handled.Channel);
                text.Append(',');
                Property(text, "channelType", handled.ChannelType);
                text.Append(',');
                Property(text, "member", handled.Member);
                text.Append(',');
                Property(text, "handler", handled.Handler);
                text.Append(',');
                Property(text, "handlerId", handled.HandlerId);
                text.Append(',');
                Property(text, "offset", handled.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"alsoReachedBy\":[");
            var arrivalCount = System.Math.Min(variant.AlsoReachedBy.Count, MaxItems);
            truncated |= variant.AlsoReachedBy.Count > MaxItems;
            for (var index = 0; index < arrivalCount; index++)
            {
                if (index > 0) text.Append(',');
                var arrival = variant.AlsoReachedBy[index];
                text.Append('{');
                Property(text, "entry", arrival.Entry);
                text.Append(',');
                Property(text, "entryId", arrival.EntryId);
                text.Append(',');
                Property(text, "triggerKind", arrival.TriggerKind);
                text.Append(",\"callPath\":");
                Strings(text, arrival.CallPath, ref truncated);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"gaps\":");
            Strings(text, variant.Gaps, ref truncated);
            text.Append('}');
            return text.ToString();
        }

        private static string Confidence(Variant variant)
        {
            if (variant.Gaps.Count > 0)
            {
                return "partial";
            }

            return variant.CallPath.Count > 1 ? "derived" : "verified";
        }

        private static void Condition(
            StringBuilder text,
            Condition condition,
            int depth,
            ref int nodes,
            ref bool truncated)
        {
            if (condition == null || depth >= MaxConditionDepth || nodes-- <= 0)
            {
                truncated = true;
                text.Append("{\"kind\":\"unknown\",\"reason\":\"serialization-limit\"}");
                return;
            }

            text.Append('{');
            Property(text, "kind", condition.Kind.ToString().ToLowerInvariant());

            if (condition.Kind == ConditionKind.Test)
            {
                text.Append(','); Property(text, "left", condition.Test.Left);
                text.Append(','); Property(text, "operator", condition.Test.Operator);
                text.Append(','); Property(text, "right", condition.Test.Right);
                text.Append(','); Property(text, "context", condition.Test.Context);
                if (condition.Test.SubjectLost != null)
                {
                    text.Append(','); Property(text, "subjectLost", condition.Test.SubjectLost);
                }

                text.Append(','); Property(text, "offset", condition.Test.Offset);
            }
            else if (condition.Kind == ConditionKind.Gesture)
            {
                text.Append(','); Property(text, "input", condition.Gesture.ToString());
                text.Append(','); Property(text, "offset", condition.Gesture.Offset);
            }
            else if (condition.Kind == ConditionKind.Unknown)
            {
                text.Append(','); Property(text, "reason", condition.Reason);

                if (condition.Unread != null)
                {
                    text.Append(','); Property(text, "unread", condition.Unread);
                }

                if (condition.LoopsBackTo >= 0)
                {
                    text.Append(','); Property(text, "loopsBackTo", condition.LoopsBackTo);
                }
            }
            else if (condition.Parts != null)
            {
                text.Append(",\"parts\":[");
                var count = System.Math.Min(condition.Parts.Count, MaxItems);
                truncated |= condition.Parts.Count > MaxItems;
                for (var index = 0; index < count; index++)
                {
                    if (index > 0) text.Append(',');
                    Condition(text, condition.Parts[index], depth + 1, ref nodes, ref truncated);
                }
                text.Append(']');
            }

            text.Append('}');
        }

        private static void Strings(StringBuilder text, IList<string> values, ref bool truncated)
        {
            text.Append('[');
            var count = System.Math.Min(values.Count, MaxItems);
            truncated |= values.Count > MaxItems;
            for (var index = 0; index < count; index++)
            {
                if (index > 0) text.Append(',');
                String(text, values[index]);
            }
            text.Append(']');
        }

        private static void Property(StringBuilder text, string name, string value)
        {
            String(text, name);
            text.Append(':');
            String(text, value);
        }

        private static void Property(StringBuilder text, string name, int value)
        {
            String(text, name);
            text.Append(':').Append(value);
        }

        private static void Property(StringBuilder text, string name, bool value)
        {
            String(text, name);
            text.Append(value ? ":true" : ":false");
        }

        /// <summary>두 번째 writer 가 이스케이프에 대해 이쪽과 다른 말을 하지 못하도록 공유한다.</summary>
        internal static void String(StringBuilder text, string value)
        {
            if (value == null)
            {
                text.Append("null");
                return;
            }

            text.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < 32)
                        {
                            text.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            text.Append(character);
                        }
                        break;
                }
            }
            text.Append('"');
        }
    }
}
