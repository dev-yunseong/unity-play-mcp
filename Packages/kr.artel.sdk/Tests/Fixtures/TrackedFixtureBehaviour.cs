using System;
using Artel.Tracking;
using UnityEngine;

namespace Artel.Tests.Tracking
{
    public sealed class TrackedFixtureBehaviour : MonoBehaviour
    {
        [ArtelState("hp")]
        public int Hp = 10;

        [ArtelAction("attack")]
        public int Attack(int damage)
        {
            return damage * 2;
        }

        [ArtelAction("ping")]
        public void Ping()
        {
        }

        [ArtelAction("fail")]
        public void Fail()
        {
            throw new InvalidOperationException("boom");
        }
    }
}
