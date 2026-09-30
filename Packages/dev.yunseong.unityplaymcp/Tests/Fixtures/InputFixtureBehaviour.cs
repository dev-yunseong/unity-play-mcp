using UnityEngine;

namespace UnityPlayMcp.Tests.Fixtures
{
    /// <summary>
    /// A game component that reads <see cref="Input"/> the way a game does.
    /// </summary>
    /// <remarks>
    /// It lives in its own assembly because the weaver acts on consumer assemblies, not the
    /// package's own test assembly.
    ///
    /// It must not name any UnityPlayMcp type. A `UnityPlayMcpHost` field once added the runtime
    /// reference and hid #47: tests passed while real games went unwoven. The weaver now adds the
    /// reference itself when it rewrites a call.
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
