using System.Collections.Generic;
using UnityEngine;

namespace UnityPlayMcp.Play
{
    internal enum EntityLifecycle
    {
        /// <summary>살아 있고 active 다.</summary>
        Present,

        /// <summary>살아 있지만 비활성이다.</summary>
        Inactive,

        /// <summary>파괴됐거나, 그 id 가 다른 오브젝트로 재사용됐다.</summary>
        Destroyed,

        /// <summary>이 session 에서 이 handle 을 준 적이 없다.</summary>
        Unknown,

        /// <summary>다른 Play session 의 handle 이다.</summary>
        WrongSession
    }

    /// <summary>
    /// 엔터티 handle 의 수명. instance id 가 재사용되거나 scene 이 다시 로드돼도 다른 대상에 입력하지 않게 한다.
    /// </summary>
    /// <remarks>
    /// handle 은 <c>(sessionId, instanceId, generation)</c> 이다. 같은 id 의 GameObject 가 파괴된 뒤 다른 것이 그 id 를
    /// 쓰면 generation 이 오른다. 이전 generation 의 handle 은 <see cref="EntityLifecycle.Destroyed"/> 로 해석한다.
    /// <para>
    /// 슬롯은 GameObject 를 잡고 있다. 파괴된 오브젝트는 Unity 비교에서 null 로 읽히므로 그 자체가 파괴 표지다.
    /// 슬롯 수는 <see cref="MaxSlots"/> 로 제한하고 파괴된 슬롯부터 비운다.
    /// </para>
    /// </remarks>
    internal sealed class EntityRegistry
    {
        public const int MaxSlots = 20000;

        private sealed class Slot
        {
            public GameObject Object;
            public int Generation;
        }

        private readonly Dictionary<int, Slot> slots = new Dictionary<int, Slot>();

        public EntityRegistry(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        /// <summary>오브젝트에 handle 을 준다. 같은 오브젝트에는 같은 handle 을 준다.</summary>
        public EntityRefDto Track(GameObject gameObject)
        {
            var id = gameObject.GetInstanceID();
            Slot slot;
            if (!slots.TryGetValue(id, out slot))
            {
                if (slots.Count >= MaxSlots)
                {
                    Compact();
                }

                slot = new Slot { Object = gameObject, Generation = 1 };
                slots[id] = slot;
            }
            else if (slot.Object != gameObject)
            {
                // 옛 오브젝트가 파괴된 뒤 같은 id 를 다른 오브젝트가 쓴다.
                slot.Object = gameObject;
                slot.Generation++;
            }

            return new EntityRefDto { SessionId = SessionId, Id = id, Generation = slot.Generation };
        }

        /// <summary>handle 이 지금 가리키는 오브젝트와 수명을 돌려준다.</summary>
        public EntityLifecycle Resolve(EntityRefDto handle, out GameObject gameObject)
        {
            gameObject = null;
            if (handle == null || handle.SessionId != SessionId)
            {
                return EntityLifecycle.WrongSession;
            }

            Slot slot;
            if (!slots.TryGetValue(handle.Id, out slot))
            {
                return EntityLifecycle.Unknown;
            }

            // 파괴된 오브젝트, 또는 더 새로운 generation 이 있다.
            if (slot.Generation != handle.Generation || slot.Object == null)
            {
                return EntityLifecycle.Destroyed;
            }

            gameObject = slot.Object;
            return gameObject.activeInHierarchy ? EntityLifecycle.Present : EntityLifecycle.Inactive;
        }

        public static string Key(EntityRefDto handle)
        {
            return handle.SessionId + ":" + handle.Id + ":" + handle.Generation;
        }

        private void Compact()
        {
            var dead = new List<int>();
            foreach (var pair in slots)
            {
                if (pair.Value.Object == null)
                {
                    dead.Add(pair.Key);
                }
            }

            foreach (var id in dead)
            {
                slots.Remove(id);
            }
        }
    }
}
