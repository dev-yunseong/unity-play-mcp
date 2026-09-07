using System;
using System.Linq;
using NUnit.Framework;
using UnityPlayMcp.Tests.Fixtures;
using UnityPlayMcp.Tests.Fixtures.NoInput;

namespace UnityPlayMcp.Tests.Input
{
    /// <summary>
    /// weaver 가 `UnityPlayMcp.Runtime` assembly reference 를 언제 붙이는지 IL metadata 로 직접 본다.
    /// </summary>
    /// <remarks>
    /// 다른 weaving test 들은 woven 된 call 의 동작을 본다. 이 test 는 그 앞 단계 — reference 자체 — 를
    /// 본다. issue #47 의 결함이 살아 있으면 `InputFixtureBehaviour` 쪽 test 가 여기서 먼저 깨진다.
    /// `Assembly.GetReferencedAssemblies` 는 asmdef 의 compiler reference 가 아니라 IL metadata 에 실제로
    /// 남은 것만 돌려주므로, 이 둘을 가르는 데 쓸 수 있다.
    /// </remarks>
    public sealed class InputWeavingReferenceTests
    {
        private const string RuntimeAssemblyName = "UnityPlayMcp.Runtime";

        [Test]
        public void Weaver_AddsRuntimeReference_ToAnAssemblyThatNamesNoUnityPlayMcpType()
        {
            // InputFixtureBehaviour 의 source 는 UnityPlayMcp type 을 하나도 쓰지 않는다. 그런데도 IL 에
            // reference 가 있다면 weaver 가 Input 호출을 바꾸면서 붙였다는 뜻이다.
            Assert.That(ReferencesRuntime(typeof(InputFixtureBehaviour)), Is.True);
        }

        [Test]
        public void Weaver_LeavesAnAssemblyWithNoInputCallsAlone()
        {
            // asmdef 가 UnityPlayMcp.Runtime 을 참조하므로 WillProcess 는 이 assembly 를 통과시킨다.
            // 바꿀 Input 호출이 없으니 reference 는 붙지 않아야 한다.
            Assert.That(ReferencesRuntime(typeof(NoInputFixture)), Is.False);
        }

        private static bool ReferencesRuntime(Type type)
        {
            return type.Assembly
                .GetReferencedAssemblies()
                .Any(name => string.Equals(name.Name, RuntimeAssemblyName, StringComparison.Ordinal));
        }
    }
}
