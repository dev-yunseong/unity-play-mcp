using System.Reflection;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 가 MonoBehaviour 에 남기는 상태를 test 안에서 재현한다.
    /// </summary>
    /// <remarks>
    /// test 는 진짜 reload 를 일으킬 수 없다. 그래서 reload 가 component 에게 하는 일만 그대로 따라 한다.
    /// Unity 의 순서는 <c>OnDisable</c> → serialize → domain 교체 → deserialize → <c>OnEnable</c> 이고,
    /// <c>Awake</c> 는 다시 부르지 않는다. 되돌아오는 것은 <c>[SerializeField]</c> 가 붙은 field 뿐이고,
    /// static 은 전부 초기값이 되며, 나머지 field 는 갓 만들어진 객체의 값 — 즉 field initializer 가 놓은
    /// 값 — 이 된다.
    ///
    /// 그 "갓 만들어진 객체의 값" 을 손으로 적지 않고 실제로 하나 만들어서 읽는다. 비활성 GameObject 에
    /// component 를 붙이면 <c>Awake</c> 도 <c>OnEnable</c> 도 돌지 않으므로, 그 component 의 field 는
    /// initializer 가 놓은 값 그대로다. 그것을 그대로 베끼면 <c>nextMessageId</c> 처럼 0 이 아닌 값으로
    /// 시작하는 field 도 reload 뒤의 값과 어긋나지 않고, 나중에 누가 <c>OnEnable</c> 에서만 채우는 field 를
    /// 하나 더 늘려도 같이 지워진다.
    ///
    /// <c>AffordanceBootstrap</c> 과 <c>Pulse</c> 의 static 은 따로 건드리지 않는다. 아래
    /// <c>enabled = false</c> 가 부르는 <c>OnDisable</c> → <c>StopTransport</c> → <c>EndDiscovery</c> 가
    /// 이미 그것들을 멈춘 상태로 돌려놓기 때문이고, 그것이 reload 직전에 실제로 일어나는 일이다.
    /// </remarks>
    internal static class AssemblyReloadSimulation
    {
        private const BindingFlags DeclaredInstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>host 를 reload 건너편에 다시 세운다.</summary>
        /// <remarks>
        /// host 만 static 을 하나 쥔다 — 살아 있는 하나를 가리키는 slot 이고, reload 는 static 을 전부
        /// 초기값으로 되돌린다. 그 slot 을 비우는 자리가 여기라서 host 에게만 이 overload 가 있다.
        /// </remarks>
        public static void Rehearse(UnityPlayMcpHost host)
        {
            Rehearse(host, () => UnityPlayMcpHostSlot.Clear());
        }

        /// <summary>static 을 쥐지 않는 component 를 reload 건너편에 다시 세운다.</summary>
        public static void Rehearse(MonoBehaviour behaviour)
        {
            Rehearse(behaviour, null);
        }

        private static void Rehearse(MonoBehaviour behaviour, System.Action forgetStatics)
        {
            behaviour.enabled = false;
            ForgetUnserializedState(behaviour);
            if (forgetStatics != null)
            {
                forgetStatics();
            }

            behaviour.enabled = true;
        }

        /// <remarks>
        /// <c>DeclaredOnly</c> 로 두는 것은 지금 rehearse 하는 세 type 이 전부 <c>sealed</c> 이고
        /// <c>MonoBehaviour</c> 를 바로 상속해, 자기가 선언한 field 가 곧 자기 field 전부이기 때문이다.
        /// 중간 base class 를 낀 component 를 여기 넘기게 되면 이 flag 부터 넓혀야 한다.
        /// </remarks>
        private static void ForgetUnserializedState(MonoBehaviour behaviour)
        {
            var type = behaviour.GetType();
            var untouched = NewlyConstructed(type);
            try
            {
                foreach (var field in type.GetFields(DeclaredInstanceFields))
                {
                    // Unity 가 되돌려 주는 값이다. 지우면 reload 가 아니라 새 component 를 흉내 내게 된다.
                    if (Serialized(field))
                    {
                        continue;
                    }

                    if (field.IsInitOnly)
                    {
                        ForgetCollectionContents(field.GetValue(behaviour));
                        continue;
                    }

                    field.SetValue(behaviour, field.GetValue(untouched));
                }
            }
            finally
            {
                Object.DestroyImmediate(untouched.gameObject);
            }
        }

        /// <summary>readonly collection 을 비운다.</summary>
        /// <remarks>
        /// readonly field 는 다시 대입하지 않는다 — runtime 에 따라 reflection 으로 쓰는 것 자체가
        /// 거절된다. 대신 안을 비운다. reload 가 돌려주는 것은 initializer 가 놓은 갓 만들어진 빈
        /// collection 이므로, 두 결과가 갈리는 자리는 참조 동일성뿐이고 reload 뒤의 코드가 그것을 보는
        /// 자리는 없다.
        ///
        /// 비우지 않으면 <c>KeyboardStatusController.keyboardKeys</c> 가 채워진 채 남아, reload 가 남기는
        /// 빈 list 를 이 rehearsal 이 재현하지 못한다 (issue #65). <c>UnityPlayMcpHost.actionRequests</c> 도
        /// 여기 걸리지만 그 test 들에서 이미 비어 있어 뜻이 달라지지 않는다.
        /// </remarks>
        private static void ForgetCollectionContents(object value)
        {
            if (!(value is System.Collections.ICollection))
            {
                return;
            }

            var clear = value.GetType().GetMethod("Clear", System.Type.EmptyTypes);
            if (clear != null)
            {
                clear.Invoke(value, null);
            }
        }

        /// <summary>Unity 가 이 field 를 저장했다가 reload 건너편에서 되돌려 주는지.</summary>
        /// <remarks>
        /// <c>[SerializeField]</c> 만 보면 반만 맞다. Unity 는 attribute 없는 public field 도 저장하고,
        /// <c>[NonSerialized]</c> 가 붙은 것은 public 이어도 저장하지 않는다. 지금 여기 오는 type 에 public
        /// field 가 없어 어느 쪽이든 결과는 같지만, 누가 하나 늘리는 순간 simulation 이 진짜 reload 보다
        /// 가혹해져 있지도 않은 결함을 test 가 붙잡게 된다.
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
        /// <c>Awake</c> 도 <c>OnEnable</c> 도 한 번도 돌지 않은 component. reload 직후의 field 값을 읽을
        /// 자리다.
        /// </summary>
        /// <remarks>
        /// 비활성 GameObject 에 붙인다. Unity 는 오브젝트가 활성화될 때 그 둘을 부르므로, 이 component 는
        /// 아무것도 만들지 않고 host 라면 slot 도 차지하지 않는다.
        /// </remarks>
        private static MonoBehaviour NewlyConstructed(System.Type type)
        {
            var carrier = new GameObject("assembly reload rehearsal") { hideFlags = HideFlags.HideAndDontSave };
            carrier.SetActive(false);
            return (MonoBehaviour)carrier.AddComponent(type);
        }
    }
}
