using System;
using System.Reflection;
using UnityPlayMcp.Diagnostics;
using UnityPlayMcp.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace UnityPlayMcp.Tests.Diagnostics
{
    /// <summary>
    /// 하드웨어 값은 기기마다 다르므로 어느 환경에서나 성립하는 불변식만 확인한다.
    /// </summary>
    public sealed class RuntimeEnvironmentTests
    {
        /// <summary>기기 식별자로 읽힐 수 있는 이름 조각이다. 어떤 필드도 이것을 담으면 안 된다.</summary>
        private static readonly string[] IdentifierNameFragments = { "unique", "identifier", "udid" };

        [Test]
        public void ReadDeviceContext_FillsEveryDescriptiveField()
        {
            var context = RuntimeEnvironment.ReadDeviceContext();

            // 빈 문자열이나 null 이면 소비자가 "값 없음" 과 "unknown" 을 구분하지 못한다.
            Assert.IsNotEmpty(context.DeviceModel);
            Assert.IsNotEmpty(context.ProcessorType);
            Assert.IsNotEmpty(context.OperatingSystem);
            Assert.IsNotEmpty(context.GraphicsDeviceName);
            Assert.IsNotEmpty(context.GraphicsDeviceType);
            Assert.IsNotEmpty(context.FullScreenMode);
            Assert.IsNotEmpty(context.ScriptingBackend);
            Assert.IsNotEmpty(context.SdkVersion);
        }

        [Test]
        public void ReadDeviceContext_ReportsAtLeastOneProcessor()
        {
            var context = RuntimeEnvironment.ReadDeviceContext();

            Assert.Greater(context.ProcessorCount, 0);
        }

        [Test]
        public void ReadDeviceContext_MarksTheSessionAsEditor()
        {
            // 이 assembly 는 editor 에서만 돌므로 항상 참이다. 거짓이면 scene view 비용이 섞인 표본이 Standalone 통계에 들어간다.
            var context = RuntimeEnvironment.ReadDeviceContext();

            Assert.IsTrue(context.IsEditor);
        }

        [Test]
        public void ReadDeviceContext_KeepsDisplayNumbersReportable()
        {
            var context = RuntimeEnvironment.ReadDeviceContext();

            // 주사율과 DPI 는 못 읽으면 0 을 보낸다. 음수나 NaN 은 JSON 을 깨거나 예산 계산을 뒤집는다.
            Assert.GreaterOrEqual(context.RefreshRateHz, 0d);
            Assert.IsFalse(double.IsNaN(context.RefreshRateHz));
            Assert.IsFalse(double.IsInfinity(context.RefreshRateHz));

            Assert.GreaterOrEqual(context.Dpi, 0f);
            Assert.IsFalse(float.IsNaN(context.Dpi));
            Assert.IsFalse(float.IsInfinity(context.Dpi));

            Assert.GreaterOrEqual(context.ResolutionWidth, 0);
            Assert.GreaterOrEqual(context.ResolutionHeight, 0);
        }

        [Test]
        public void ReadDeviceContext_ReportsTheVersionFromPackageJson()
        {
            // runtime 에서 package.json 을 읽을 수 없어 버전을 상수로 둔다. 상수가 낡지 않게 여기서 동기화를 확인한다.
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(RuntimeEnvironment).Assembly);
            if (package == null)
            {
                Assert.Ignore("Runtime assembly is not resolving to a package in this project.");
            }

            Assert.AreEqual(package.version, RuntimeEnvironment.ReadDeviceContext().SdkVersion);
        }

        [Test]
        public void ReadDeviceContext_DeclaresTheCollectedMetricGroups()
        {
            // 목록이 비면 서버는 값이 없는 군을 "SDK 가 모르는 군" 으로 읽어 "못 쟀다" 와 구분하지 못한다.
            // 목록과 보고 필드의 대응은 MetricGroupContractTests 가 확인한다.
            var context = RuntimeEnvironment.ReadDeviceContext();

            CollectionAssert.IsNotEmpty(context.CollectedGroups);
            CollectionAssert.AllItemsAreNotNull(context.CollectedGroups);
        }

        [Test]
        public void ReadStatus_ReportsAKnownBatteryStatus()
        {
            var status = RuntimeEnvironment.ReadStatus();

            Assert.IsTrue(
                Enum.IsDefined(typeof(BatteryStatus), status.BatteryStatus),
                "batteryStatus must stay parseable as UnityEngine.BatteryStatus.");
        }

        [Test]
        public void ReadStatus_MatchesApplicationFocus()
        {
            Assert.AreEqual(Application.isFocused, RuntimeEnvironment.ReadStatus().IsFocused);
        }

        [TestCase(typeof(DeviceContextDto))]
        [TestCase(typeof(RuntimeStatusDto))]
        public void Payload_HasNoFieldThatCouldCarryADeviceIdentifier(Type dtoType)
        {
            // SystemInfo.deviceUniqueIdentifier 가 실리면 보고 전체가 개인정보가 된다. 필드가 늘어도 여기서 잡는다.
            foreach (var property in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var jsonName = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
                foreach (var fragment in IdentifierNameFragments)
                {
                    AssertDoesNotContain(property.Name, fragment, dtoType.Name);
                    AssertDoesNotContain(jsonName, fragment, dtoType.Name);
                }
            }
        }

        [Test]
        public void Payload_DoesNotSerializeTheDeviceUniqueIdentifier()
        {
            var identifier = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrEmpty(identifier) || identifier == SystemInfo.unsupportedIdentifier)
            {
                // "n/a" 같은 placeholder 는 우연히 겹칠 수 있어 비교가 무의미하다.
                Assert.Ignore("This platform does not expose a device identifier to compare against.");
            }

            var json = JsonConvert.SerializeObject(RuntimeEnvironment.ReadDeviceContext())
                       + JsonConvert.SerializeObject(RuntimeEnvironment.ReadStatus());

            Assert.IsFalse(json.Contains(identifier), "The device identifier leaked into the payload.");
        }

        private static void AssertDoesNotContain(string name, string fragment, string dtoName)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            Assert.IsFalse(
                name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0,
                $"{dtoName}.{name} looks like a device identifier.");
        }
    }
}
