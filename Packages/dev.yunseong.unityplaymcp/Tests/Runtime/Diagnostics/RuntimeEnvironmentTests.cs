using System;
using System.Reflection;
using Artel.Diagnostics;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Diagnostics
{
    /// <summary>
    /// 실제 하드웨어 값을 단정하면 실행 기기마다 결과가 갈린다. 그래서 어느 환경에서나
    /// 성립해야 하는 불변식만 확인한다.
    /// </summary>
    public sealed class RuntimeEnvironmentTests
    {
        /// <summary>기기 식별자로 읽힐 수 있는 이름 조각. 어떤 필드도 이걸 담으면 안 된다.</summary>
        private static readonly string[] IdentifierNameFragments = { "unique", "identifier", "udid" };

        [Test]
        public void ReadDeviceContext_FillsEveryDescriptiveField()
        {
            var context = RuntimeEnvironment.ReadDeviceContext();

            // 빈 문자열이나 null이 오면 소비자가 "값이 없음"과 "값이 unknown"을 구분하지 못한다.
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
            // 이 어셈블리는 에디터에서만 돌기 때문에 항상 참이어야 한다. 거짓이면 소비자가
            // 씬 뷰 비용이 얹힌 표본을 Standalone 통계에 섞게 된다.
            var context = RuntimeEnvironment.ReadDeviceContext();

            Assert.IsTrue(context.IsEditor);
        }

        [Test]
        public void ReadDeviceContext_KeepsDisplayNumbersReportable()
        {
            var context = RuntimeEnvironment.ReadDeviceContext();

            // 주사율과 DPI는 못 읽으면 0으로 눌러 보낸다. 음수나 NaN이 새어 나가면
            // JSON이 깨지거나 소비자의 예산 계산이 뒤집힌다.
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
            // 버전은 런타임에서 package.json을 읽을 수 없어 상수로 박아 둔다. 그 상수가
            // 조용히 낡는 것이 유일한 위험이라, 동기화를 여기서 강제한다.
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
            // 이 목록이 비면 서버는 값이 없는 군을 전부 "이 SDK가 모르는 군"으로 읽는다.
            // 못 쟀다는 것과 아예 모른다는 것이 한 덩어리가 되어 회귀 판단이 성립하지 않는다.
            // 목록과 실제 보고 필드의 대응은 MetricGroupContractTests가 지킨다.
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
            // SystemInfo.deviceUniqueIdentifier는 성능 해석에 쓰이지 않는데 한 번 실리면
            // 보고 전체가 개인정보가 된다. 나중에 누가 필드를 늘려도 여기서 걸린다.
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
                // 플랫폼이 "n/a" 같은 자리표시자를 주면 우연히 겹칠 수 있어 판정이 무의미해진다.
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
