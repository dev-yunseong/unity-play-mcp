using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityPlayMcp.Affordances.Scan;

namespace UnityPlayMcp.Tests
{
    /// <remarks>
    /// player build 에는 <c>package.json</c> 이 없고 <c>UnityEditor.PackageManager</c> 는 Standalone build 를 깨뜨리므로
    /// runtime 은 version 을 상수로 둔다. 이 상수가 낡지 않게 여기서 확인한다.
    /// report 의 <c>build.sdk</c> 와 device context 의 <c>sdkVersion</c> 이 이 값을 싣는다.
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
