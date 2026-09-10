using System.Reflection;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 가 <see cref="UnityPlayMcpHost"/> 에 남기는 상태를 test 안에서 재현한다.
    /// </summary>
    /// <remarks>
    /// test 는 진짜 reload 를 일으킬 수 없다. 그래서 reload 가 host 에게 하는 일만 그대로 따라 한다. Unity 의
    /// 순서는 <c>OnDisable</c> → serialize → domain 교체 → deserialize → <c>OnEnable</c> 이고, <c>Awake</c> 는
    /// 다시 부르지 않는다. 되돌아오는 것은 <c>[SerializeField]</c> 가 붙은 field 뿐이고, static 은 전부
    /// 초기값이 되며, 나머지 field 는 갓 만들어진 객체의 값 — 즉 field initializer 가 놓은 값 — 이 된다.
    ///
    /// 그 "갓 만들어진 객체의 값" 을 손으로 적지 않고 실제로 하나 만들어서 읽는다. 비활성 GameObject 에
    /// component 를 붙이면 <c>Awake</c> 가 돌지 않으므로, 그 host 의 field 는 initializer 가 놓은 값 그대로다.
    /// 그것을 그대로 베끼면 <c>nextMessageId</c> 처럼 0 이 아닌 값으로 시작하는 field 도 reload 뒤의 값과
    /// 어긋나지 않고, 나중에 누가 <c>Awake</c> 에서만 채우는 field 를 하나 더 늘려도 같이 지워진다.
    ///
    /// <c>AffordanceBootstrap</c> 과 <c>Pulse</c> 의 static 은 따로 건드리지 않는다. 아래 <c>enabled = false</c>
    /// 가 부르는 <c>OnDisable</c> → <c>StopTransport</c> → <c>EndDiscovery</c> 가 이미 그것들을 멈춘 상태로
    /// 돌려놓기 때문이고, 그것이 reload 직전에 실제로 일어나는 일이다.
    /// </remarks>
    internal static class AssemblyReloadSimulation
    {
        private const BindingFlags DeclaredInstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>host 를 reload 건너편에 다시 세운다.</summary>
        public static void Rehearse(UnityPlayMcpHost host)
        {
            host.enabled = false;
            ForgetUnserializedState(host);
            UnityPlayMcpHostSlot.Clear();
            host.enabled = true;
        }

        private static void ForgetUnserializedState(UnityPlayMcpHost host)
        {
            var untouched = NewlyConstructedHost();
            try
            {
                foreach (var field in typeof(UnityPlayMcpHost).GetFields(DeclaredInstanceFields))
                {
                    // Unity 가 되돌려 주는 값이다. 지우면 reload 가 아니라 새 host 를 흉내 내게 된다.
                    if (Serialized(field))
                    {
                        continue;
                    }

                    // readonly field 는 건드리지 않는다. runtime 에 따라 reflection 으로 쓰는 것 자체가
                    // 거절되고, 지금 여기 걸리는 것은 actionRequests 하나뿐이라 비어 있는 그것과 새로 만든
                    // 빈 것을 이 test 가 갈라 보지 않는다.
                    if (field.IsInitOnly)
                    {
                        continue;
                    }

                    field.SetValue(host, field.GetValue(untouched));
                }
            }
            finally
            {
                Object.DestroyImmediate(untouched.gameObject);
            }
        }

        /// <summary>Unity 가 이 field 를 저장했다가 reload 건너편에서 되돌려 주는지.</summary>
        /// <remarks>
        /// <c>[SerializeField]</c> 만 보면 반만 맞다. Unity 는 attribute 없는 public field 도 저장하고,
        /// <c>[NonSerialized]</c> 가 붙은 것은 public 이어도 저장하지 않는다. 지금 host 에 public field 가
        /// 없어 어느 쪽이든 결과는 같지만, 누가 하나 늘리는 순간 simulation 이 진짜 reload 보다 가혹해져
        /// 있지도 않은 결함을 test 가 붙잡게 된다.
        /// </remarks>
        private static bool Serialized(FieldInfo field)
        {
            if (field.IsDefined(typeof(System.NonSerializedAttribute), false))
            {
                return false;
            }

            return field.IsPublic || field.IsDefined(typeof(SerializeField), false);
        }

        /// <summary>
        /// <c>Awake</c> 가 한 번도 돌지 않은 host. reload 직후의 field 값을 읽을 자리다.
        /// </summary>
        /// <remarks>
        /// 비활성 GameObject 에 붙인다. Unity 는 오브젝트가 활성화될 때 <c>Awake</c> 를 부르므로, 이 host 는
        /// 아무것도 만들지 않고 slot 도 차지하지 않는다.
        /// </remarks>
        private static UnityPlayMcpHost NewlyConstructedHost()
        {
            var carrier = new GameObject("assembly reload rehearsal") { hideFlags = HideFlags.HideAndDontSave };
            carrier.SetActive(false);
            return carrier.AddComponent<UnityPlayMcpHost>();
        }
    }
}
