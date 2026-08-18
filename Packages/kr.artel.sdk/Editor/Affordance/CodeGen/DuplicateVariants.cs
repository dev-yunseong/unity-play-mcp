using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 어디서 닿았는지만 다른 경우들을 하나로 접는다.
    /// </summary>
    /// <remarks>
    /// 진입점 여섯 곳에서 불리는 헬퍼 하나는 같은 메서드에 대해 같은 말을 하는 기록 여섯 개를 만든다.
    /// 실측한 모든 리포트의 5분의 1에서 3분의 1이 그것이었고 — WordVenture 19%, Trash Dash 24%,
    /// Chop Chop 30% — 독자는 그것들에 대해 무슨 말을 하기 전에 먼저 같은 것임을 알아내야 한다.
    ///
    /// 나머지가 전부 같을 때만 접는다: 조건, 효과, 호출, 구독, 그리고 빈자리까지. 그 전부에 대해 일치하는
    /// 두 경우는 하나의 사실이고, 거기 이르는 여러 갈래가 다른 부분이다.
    ///
    /// 첫 번째 갈래는 있던 자리에 늘 쓰던 이름으로 남으므로, 이것을 읽는 쪽은 계속 돌아가기 위해
    /// 아무것도 바꾸지 않아도 된다. 나머지는 그 옆의 목록으로 간다. 다만 그 목록을 무시하는 독자는 같은
    /// 효과에 이르는 갈래를 전보다 적게 보게 되고, 스키마 번호가 움직이는 이유가 그것이다.
    /// </remarks>
    internal static class DuplicateVariants
    {
        /// <summary>필드 사이에 둔다. 두 값이 흘러 붙어 세 번째가 되지 않도록.</summary>
        private const char Separator = '\u001f';

        internal static int Fold(List<Variant> variants)
        {
            var byIdentity = new Dictionary<string, Variant>(variants.Count);
            var kept = new List<Variant>(variants.Count);
            var folded = 0;

            foreach (var variant in variants)
            {
                var identity = Identity(variant);

                if (!byIdentity.TryGetValue(identity, out var already))
                {
                    byIdentity[identity] = variant;
                    kept.Add(variant);
                    continue;
                }

                already.AlsoReachedBy.Add(new Arrival
                {
                    Entry = variant.Entry,
                    EntryId = variant.EntryId,
                    TriggerKind = variant.TriggerKind,
                    CallPath = variant.CallPath
                });

                folded++;
            }

            variants.Clear();
            variants.AddRange(kept);
            return folded;
        }

        /// <summary>
        /// 어떻게 닿았는지를 뺀, 한 경우의 전부.
        /// </summary>
        /// <remarks>
        /// 객체 동일성이 아니라 실제로 써 나가는 값들로부터 만든다. 서로 다른 두 경로에서 찾아낸 두 경우는
        /// 정확히 같은 말을 하면서도 여전히 두 객체이기 때문이다.
        /// </remarks>
        private static string Identity(Variant variant)
        {
            var key = new StringBuilder(256);

            key.Append(variant.Owner?.FullName).Append(Separator)
                .Append(variant.MethodId).Append(Separator)
                .Append(variant.RecordKind).Append(Separator)
                .Append(variant.When.Key).Append(Separator);

            foreach (var outcome in variant.Outcomes)
            {
                key.Append(outcome.Kind).Append(':').Append(outcome.Category).Append(':')
                    .Append(outcome.Target).Append(':').Append(outcome.Detail).Append(':')
                    .Append(outcome.Offset).Append(Separator);
            }

            key.Append(Separator);

            foreach (var call in variant.Calls)
            {
                key.Append(call.TargetId).Append(':').Append(call.Receiver).Append(':')
                    .Append(call.Arguments).Append(':').Append(call.Offset).Append(Separator);
            }

            key.Append(Separator);

            foreach (var handled in variant.Handles)
            {
                key.Append(handled.HandlerId).Append(':').Append(handled.Channel).Append(':')
                    .Append(handled.Offset).Append(Separator);
            }

            key.Append(Separator);

            foreach (var gap in variant.Gaps)
            {
                key.Append(gap).Append(Separator);
            }

            return key.ToString();
        }
    }
}
