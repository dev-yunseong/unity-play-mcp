using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityPlayMcp.Protocol;
using UnityPlayMcp.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;

namespace UnityPlayMcp.Tests.Protocol
{
    /// <summary>
    /// 성능 보고에 실리는 지표군과 <c>DEVICE_CONTEXT</c> 가 선언하는 목록이 같은지 확인한다.
    /// </summary>
    /// <remarks>
    /// 어긋나도 오류가 나지 않는다. 서버는 선언만 있는 군을 "카운터 없음" 으로 읽고, 선언 없는 값은
    /// 그냥 받아 조회 화면에 그럴듯한 오답이 나온다.
    /// </remarks>
    public sealed class MetricGroupContractTests
    {
        /// <summary>
        /// 서버가 이름으로 읽는 최상위 필드다 (orchestration-server <c>SdkPerformanceMessage</c>).
        /// 서버는 이 목록에 없는 최상위 객체 필드를 모두 지표군으로 받는다. 서버가 필드를 추가하면
        /// 이 test 가 그것을 군으로 세어 실패한다.
        /// </summary>
        private static readonly HashSet<string> ServerNamedFields =
            new HashSet<string> { "type", "id", "frameTimes", "status", "process" };

        [Test]
        public void Collected_ListsExactlyTheGroupsTheReportCarries()
        {
            var carried = GroupNamesOnTheReport();

            // 군을 추가하고 목록을 안 고치거나, 수집하지 않는 군을 목록에 적으면 서버의 가용성 판정이 틀린다.
            CollectionAssert.AreEquivalent(carried, MetricGroupNames.Collected());
        }

        /// <remarks>
        /// 목록이 플랫폼마다 같은지는 여기서 검증하지 않는다. 이 assembly 는 editor 에서만 돌아
        /// <c>#if UNITY_EDITOR</c> 로 감싸도 통과하기 때문이다. Standalone 확인은 빌드가 필요하다.
        /// </remarks>
        [Test]
        public void Collected_DoesNotHandOutTheBackingArray()
        {
            // 같은 배열을 돌려주면 호출자가 내용을 바꿔 이후 세션이 다른 목록을 보낼 수 있다. 내용은 같고 배열은 달라야 한다.
            var first = MetricGroupNames.Collected();
            var second = MetricGroupNames.Collected();

            Assert.AreNotSame(first, second);
            CollectionAssert.AreEqual(first, second);
        }

        /// <summary>
        /// 보고에서 지표군으로 읽힐 필드의 wire 이름이다. 서버 규칙대로 스칼라와 이름 붙은 고정 필드는 제외한다.
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

        /// <summary>군은 한 단계 아래 객체다. 스칼라는 군이 아니다.</summary>
        private static bool IsCarriedAsAnObject(Type type)
        {
            return type.IsClass && type != typeof(string);
        }
    }
}
