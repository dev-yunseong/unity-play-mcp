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

        public bool ReadSpaceKeyDown()
        {
            return Input.GetKeyDown(KeyCode.Space);
        }

        public bool ReadSpaceKey()
        {
            return Input.GetKey(KeyCode.Space);
        }

        public bool ReadAnyKeyDown()
        {
            return Input.anyKeyDown;
        }

        public float ReadHorizontalAxis()
        {
            return Input.GetAxis("Horizontal");
        }

        public float ReadHorizontalAxisRaw()
        {
            return Input.GetAxisRaw("Horizontal");
        }

        public bool ReadJumpButton()
        {
            return Input.GetButton("Jump");
        }

        public bool ReadJumpButtonDown()
        {
            return Input.GetButtonDown("Jump");
        }
    }
}
