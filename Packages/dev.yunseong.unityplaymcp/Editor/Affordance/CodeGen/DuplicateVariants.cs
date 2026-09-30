using System.Collections.Generic;
using System.Text;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 어디서 닿았는지만 다른 경우들을 하나로 접는다.
    /// </summary>
    /// <remarks>
    /// 진입점 여러 곳에서 불리는 헬퍼는 같은 내용의 기록을 진입점 수만큼 만든다.
    ///
    /// 조건, 효과, 호출, 구독, gap 이 모두 같을 때만 접는다. 첫 경우는 원래 자리에 남고 나머지는
    /// <c>AlsoReachedBy</c> 로 간다. 그 목록을 무시하는 읽는 쪽은 경로를 전보다 적게 보므로 스키마 버전을 올린다.
    /// </remarks>
    internal static class DuplicateVariants
    {
        /// <summary>필드 구분자. 인접한 두 값이 붙어 다른 값처럼 읽히지 않게 한다.</summary>
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
        /// 도달 경로를 뺀 한 경우의 전체 내용.
        /// </summary>
        /// <remarks>
        /// 서로 다른 경로에서 찾은 두 경우는 내용이 같아도 다른 객체이므로 객체 동일성이 아니라 써 나가는 값으로 만든다.
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
