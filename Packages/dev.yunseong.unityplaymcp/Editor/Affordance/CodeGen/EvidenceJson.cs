using System.Collections.Generic;
using System.Text;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>런타임 스캐너가 읽을 유계이고 결정적인 `evidence` 문서 하나를 쓴다.</summary>
    internal static class EvidenceJson
    {
        /// <summary>
        /// 6: <c>calledBy</c> 를 더한다.
        /// </summary>
        /// <remarks>
        /// 3 은 호출 엣지에 수신 객체와 인자, 구독을 더했고, 4 는 중복 경우를 접어 <c>alsoReachedBy</c> 에 넣었다.
        ///
        /// 기존 필드는 같은 자리와 뜻으로 남는다. 그래도 버전을 올리는 것은 빠뜨린 필드의 뜻이 달라지기 때문이다.
        /// <c>handles</c> 나 <c>alsoReachedBy</c> 를 무시하는 읽는 쪽은 구독이나 경로를 실제보다 적게 본다.
        ///
        /// 이 값은 attribute 에도 쓰이므로 문서와 함께 올린다. 한 파일이 두 버전을 말하면 안 된다.
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

                // 가져간 대상. `WaitUntil` 에 넘긴 술어는 coroutine 을 멈춰 세우지만 콜백 목록에 넘긴 것은 그렇지 않다.
                if (variant.HandedTo != null)
                {
                    text.Append(',');
                    Property(text, "handedOverTo", variant.HandedTo);
                }
            }

            // 이 기록에 닿는 호출자. 기록의 호출 목록은 나가는 엣지뿐이고, 자기 기록이 없는 호출자는 거기 남지 않는다.
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

                // 원본이 여러 값 중에서 골랐을 때만 나온다. 위의 target 하나가 확정된 답이 아님을 알린다.
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

        /// <summary>다른 writer 와 같은 이스케이프 규칙을 쓰도록 공유한다.</summary>
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
