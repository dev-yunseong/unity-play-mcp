using UnityEngine;

namespace UnityPlayMcp.Tests.Fixtures
{
    /// <summary>
    /// A game component that reads <see cref="Input"/> the way a game does.
    /// </summary>
    /// <remarks>
    /// It sits in an assembly of its own because that is what the weaver acts on: the assembly
    /// under test is a consumer of the package, not the package's own test assembly. A test that
    /// wove itself would prove less than the one thing this fixture exists to prove — that a
    /// game's own `Input` calls come out reading the virtual mouse and keyboard.
    ///
    /// It names no UnityPlayMcp type at all, which is what makes it a real game assembly. It once
    /// carried a `UnityPlayMcpHost` field for the sole purpose of putting `UnityPlayMcp.Runtime`
    /// into the fixture's IL metadata, because the weaver would not touch an assembly without that
    /// reference. That field hid the defect in issue #47: the tests passed while every real game
    /// went unwoven. Do not add one back — the weaver now adds the reference itself when it has a
    /// call to rewrite.
    /// </remarks>
    public sealed class InputFixtureBehaviour : MonoBehaviour
    {
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
