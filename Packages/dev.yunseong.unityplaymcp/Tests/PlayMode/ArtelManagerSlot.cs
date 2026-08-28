using System.Reflection;

namespace Artel.Tests
{
    /// <summary>
    /// 중복 판정에 쓰는 <see cref="ArtelManager"/>의 정적 슬롯을 테스트가 잠시 비켜 둘 수 있게 한다.
    /// </summary>
    /// <remarks>
    /// 살아 있는 매니저가 하나라도 있으면 새로 붙인 매니저는 Awake가 중복으로 보고 그 자리에서
    /// 파괴한다. 개발 빌드용 AfterSceneLoad 훅이 플레이 모드 진입 때 하나를 띄우므로, 자기
    /// 매니저를 세우려는 픽스처는 먼저 슬롯을 비워야 한다.
    ///
    /// 훅이 띄운 오브젝트를 파괴하는 대신 슬롯만 비우는 이유는 <c>ArtelBootstrapTests</c>가 바로
    /// 그 오브젝트를 관찰하기 때문이다. 훅은 플레이 모드당 한 번만 돌아 다시 띄워 주지 않으므로,
    /// 파괴하면 먼저 도는 픽스처가 그 테스트의 관찰 대상을 없앤다.
    /// </remarks>
    internal static class ArtelManagerSlot
    {
        private static readonly FieldInfo InstanceField = typeof(ArtelManager)
            .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>슬롯을 비우고, 비켜 둔 매니저를 돌려준다.</summary>
        public static ArtelManager Clear()
        {
            var displaced = InstanceField.GetValue(null) as ArtelManager;
            InstanceField.SetValue(null, null);
            return displaced;
        }

        public static void Restore(ArtelManager manager)
        {
            InstanceField.SetValue(null, manager);
        }
    }
}
