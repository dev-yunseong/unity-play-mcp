using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Artel.Protocol;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Artel.Tests.Protocol
{
    /// <summary>
    /// 성능 보고가 싣는 지표군과 <c>DEVICE_CONTEXT</c>가 선언하는 목록이 어긋나지 않게 잡는다.
    /// </summary>
    /// <remarks>
    /// 어긋나도 아무것도 터지지 않는 것이 문제다. 서버는 선언만 있고 값이 없는 군을 "이
    /// 플랫폼에 카운터가 없었다"로 읽고, 선언 없이 값만 오는 군은 조용히 받는다. 어느 쪽도
    /// 오류가 아니라서, 잘못된 선언은 조회 화면에 그럴듯한 오답으로만 나타난다.
    /// </remarks>
    public sealed class MetricGroupContractTests
    {
        /// <summary>
        /// 서버가 이름을 붙여 읽는 최상위 필드.
        ///
        /// 출처는 orchestration-server의 <c>SdkPerformanceMessage</c>다. 서버는 이 목록에
        /// 없는 최상위 <em>객체</em> 필드를 전부 지표군으로 받으므로, SDK 쪽에서 무엇이 군인지
        /// 판정하려면 같은 목록이 필요하다. 서버가 필드에 이름을 하나 더 붙이면 이 테스트가
        /// 그 필드를 군으로 세어 깨진다 — 조용히 지나가는 것보다 낫다.
        /// </summary>
        private static readonly HashSet<string> ServerNamedFields =
            new HashSet<string> { "type", "id", "frameTimes", "status", "process" };

        [Test]
        public void Collected_ListsExactlyTheGroupsTheReportCarries()
        {
            var carried = GroupNamesOnTheReport();

            // 집합이 같아야 한다. 군을 늘리고 목록을 안 고치면 왼쪽이 커지고, 수집하지 않는
            // 군을 목록에 적으면 오른쪽이 커진다. 둘 다 서버의 가용성 판정을 틀리게 만든다.
            CollectionAssert.AreEquivalent(carried, MetricGroupNames.Collected());
        }

        /// <remarks>
        /// 목록이 플랫폼에 따라 달라지지 않는다는 것은 여기서 검증할 수 없다. 이 어셈블리는
        /// <c>includePlatforms: [Editor]</c>라 에디터에서만 도는데, 누가 목록을
        /// <c>#if UNITY_EDITOR</c>로 감싸도 에디터에서는 그대로 통과한다. 통과할 근거가 없는
        /// 단정을 두면 없는 커버리지를 있다고 광고하게 되므로 두지 않는다. Standalone 확인은
        /// 빌드가 필요하고, ARTEL-486 PR에 미검증으로 남겼다.
        /// </remarks>
        [Test]
        public void Collected_DoesNotHandOutTheBackingArray()
        {
            // 같은 배열을 넘기면 보고에 실린 뒤 호출자가 내용을 고칠 수 있고, 그러면 이후
            // 세션이 조용히 다른 목록을 보낸다. 내용은 같고 배열만 달라야 한다.
            var first = MetricGroupNames.Collected();
            var second = MetricGroupNames.Collected();

            Assert.AreNotSame(first, second);
            CollectionAssert.AreEqual(first, second);
        }

        /// <summary>
        /// 보고에서 지표군으로 읽힐 필드의 와이어 이름. 서버의 판정 규칙을 그대로 옮긴 것이라,
        /// 스칼라는 제외하고 이름 붙은 고정 필드도 제외한다.
        /// </summary>
        private static IEnumerable<string> GroupNamesOnTheReport()
        {
            return typeof(PerformanceMessageDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => new
                {
                    Name = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName,
                    property.PropertyType
                })
                .Where(field => field.Name != null)
                .Where(field => !ServerNamedFields.Contains(field.Name))
                .Where(field => IsCarriedAsAnObject(field.PropertyType))
                .Select(field => field.Name);
        }

        /// <summary>군은 언제나 한 단계 아래로 묶인 객체다. 스칼라는 군이 아니다.</summary>
        private static bool IsCarriedAsAnObject(Type type)
        {
            return type.IsClass && type != typeof(string);
        }
    }
}
