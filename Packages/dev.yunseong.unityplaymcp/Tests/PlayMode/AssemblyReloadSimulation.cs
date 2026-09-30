using System.Reflection;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// play 중 assembly reload 가 MonoBehaviour 에 남기는 상태를 test 안에서 재현한다.
    /// </summary>
    /// <remarks>
    /// Unity 순서는 <c>OnDisable</c> → serialize → domain 교체 → deserialize → <c>OnEnable</c> 이고
    /// <c>Awake</c> 는 다시 돌지 않는다. serialize 되는 field 만 복원되고, static 은 초기값, 나머지
    /// field 는 field initializer 값이 된다.
    ///
    /// initializer 값은 비활성 GameObject 에 붙인 새 component 에서 읽는다. 그래서 0 이 아닌 초기값이나
    /// 새로 추가된 field 도 따로 적지 않아도 맞는다.
    ///
    /// <c>AffordanceBootstrap</c> 과 <c>Pulse</c> 의 static 은 <c>OnDisable</c> → <c>StopTransport</c> →
    /// <c>EndDiscovery</c> 가 이미 멈추므로 따로 지우지 않는다.
    /// </remarks>
    internal static class AssemblyReloadSimulation
    {
        private const BindingFlags DeclaredInstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>host 에 reload 를 재현한다.</summary>
        /// <remarks>
        /// host 는 살아 있는 instance 를 가리키는 static slot 을 가지므로 그 slot 도 비운다.
        /// </remarks>
        public static void Rehearse(UnityPlayMcpHost host)
        {
            Rehearse(host, () => UnityPlayMcpHostSlot.Clear());
        }

        /// <summary>static 을 갖지 않는 component 에 reload 를 재현한다.</summary>
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
        /// <c>DeclaredOnly</c> 는 대상 type 이 모두 <c>MonoBehaviour</c> 를 바로 상속한 <c>sealed</c> 라서 충분하다.
        /// 중간 base class 가 있는 component 를 넘기려면 flag 를 넓혀야 한다.
        /// </remarks>
        private static void ForgetUnserializedState(MonoBehaviour behaviour)
        {
            var type = behaviour.GetType();
            var untouched = NewlyConstructed(type);
            try
            {
                foreach (var field in type.GetFields(DeclaredInstanceFields))
                {
                    // Unity 가 복원하는 값이다. 지우면 reload 가 아니라 새 component 를 재현하게 된다.
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
        /// readonly field 는 runtime 에 따라 reflection 대입이 거절되므로 내용만 비운다. reload 뒤와 다른 것은
        /// 참조 동일성뿐이고 그것을 보는 코드는 없다.
        /// 비우지 않으면 <c>KeyboardStatusController.keyboardKeys</c> 가 채워진 채 남는다 (#65).
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

        /// <summary>Unity 가 이 field 를 serialize 해 reload 뒤 복원하는지 반환한다.</summary>
        /// <remarks>
        /// attribute 없는 public field 도 serialize 되고, <c>[NonSerialized]</c> 는 public 이어도 제외된다.
        /// 이를 어기면 simulation 이 실제 reload 보다 더 많이 지워 없는 결함을 잡는다.
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
        /// <c>Awake</c> 와 <c>OnEnable</c> 이 돌지 않은 component 를 만든다. reload 직후 field 값을 읽는 데 쓴다.
        /// </summary>
        /// <remarks>
        /// 비활성 GameObject 에 붙이므로 아무것도 만들지 않고, host 라도 slot 을 차지하지 않는다.
        /// </remarks>
        private static MonoBehaviour NewlyConstructed(System.Type type)
        {
            var carrier = new GameObject("assembly reload rehearsal") { hideFlags = HideFlags.HideAndDontSave };
            carrier.SetActive(false);
            return (MonoBehaviour)carrier.AddComponent(type);
        }
    }
}
