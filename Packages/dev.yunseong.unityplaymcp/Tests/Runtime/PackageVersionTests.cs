using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityPlayMcp.Affordances.Scan;

namespace UnityPlayMcp.Tests
{
    /// <remarks>
    /// runtime 은 자기 version 을 손으로 적은 상수로만 안다. player build 에 <c>package.json</c> 이 들어가지
    /// 않고, <c>UnityEditor.PackageManager</c> 로 읽으면 runtime assembly 가 editor 전용 API 에 걸려
    /// Standalone build 가 깨지기 때문이다. 그 상수가 조용히 낡는 것이 유일한 위험이라 여기서 막는다.
    ///
    /// report 의 <c>build.sdk</c> 와 device context 의 <c>sdkVersion</c> 이 둘 다 이 값을 싣는다. 틀린
    /// version 을 단정하는 것은 version 이 없는 것보다 나쁘다 — 어느 build 가 낸 report 인지 모른 채
    /// 안다고 믿게 된다.
    /// </remarks>
    public sealed class PackageVersionTests
    {
        [Test]
        public void MatchesTheVersionInPackageJson()
        {
            var package = PackageInfo.FindForAssembly(typeof(PackageVersion).Assembly);

            if (package == null)
            {
                Assert.Ignore("The scan assembly does not resolve to a package in this project.");
            }

            Assert.AreEqual(package.version, PackageVersion.Value);
        }
    }
}
