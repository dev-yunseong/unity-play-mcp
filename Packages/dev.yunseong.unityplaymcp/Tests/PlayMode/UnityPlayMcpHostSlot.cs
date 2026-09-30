using System.Reflection;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 중복 판정에 쓰는 <see cref="UnityPlayMcpHost"/> 의 static slot 을 test 가 잠시 비울 수 있게 한다.
    /// </summary>
    /// <remarks>
    /// 살아 있는 host 가 있으면 새 host 는 Awake 에서 중복으로 파괴된다. development build 의 AfterSceneLoad
    /// hook 이 play mode 진입 때 host 를 띄우므로, 자기 host 를 세우는 fixture 는 먼저 slot 을 비워야 한다.
    /// hook 이 띄운 오브젝트는 play mode 당 한 번만 생기므로 파괴하지 않고 slot 만 비운다.
    /// </remarks>
    internal static class UnityPlayMcpHostSlot
    {
        private static readonly FieldInfo InstanceField = typeof(UnityPlayMcpHost)
            .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>slot 을 비우고 원래 있던 host 를 돌려준다.</summary>
        public static UnityPlayMcpHost Clear()
        {
            var displaced = InstanceField.GetValue(null) as UnityPlayMcpHost;
            InstanceField.SetValue(null, null);
            return displaced;
        }

        public static void Restore(UnityPlayMcpHost manager)
        {
            InstanceField.SetValue(null, manager);
        }
    }
}
