using System;
using System.Linq;
using NUnit.Framework;
using UnityPlayMcp.Tests.Fixtures;
using UnityPlayMcp.Tests.Fixtures.NoInput;

namespace UnityPlayMcp.Tests.Input
{
    /// <summary>
    /// weaver 가 `UnityPlayMcp.Runtime` assembly reference 를 언제 붙이는지 IL metadata 로 확인한다.
    /// </summary>
    /// <remarks>
    /// `Assembly.GetReferencedAssemblies` 는 asmdef 의 compiler reference 가 아니라 IL metadata 에 남은 것만
    /// 돌려주므로 둘을 구분할 수 있다 (#47).
    /// </remarks>
    public sealed class InputWeavingReferenceTests
    {
        private const string RuntimeAssemblyName = "UnityPlayMcp.Runtime";

        [Test]
        public void Weaver_AddsRuntimeReference_ToAnAssemblyThatNamesNoUnityPlayMcpType()
        {
            // InputFixtureBehaviour 는 UnityPlayMcp type 을 쓰지 않으므로, reference 가 있다면 weaver 가 붙인 것이다.
            Assert.That(ReferencesRuntime(typeof(InputFixtureBehaviour)), Is.True);
        }

        [Test]
        public void Weaver_LeavesAnAssemblyWithNoInputCallsAlone()
        {
            // asmdef 가 UnityPlayMcp.Runtime 을 참조해 WillProcess 는 통과하지만, 바꿀 Input 호출이 없으니 reference 는 없어야 한다.
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
