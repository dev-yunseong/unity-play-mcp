using UnityEngine;

namespace Artel.Tests.Fixtures
{
    /// <summary>
    /// A game component that reads <see cref="Input"/> the way a game does.
    /// </summary>
    /// <remarks>
    /// It sits in an assembly of its own because that is what the weaver acts on: the assembly
    /// under test is a consumer of the package, not the package's own test assembly. A test that
    /// wove itself would prove less than the one thing this fixture exists to prove — that a
    /// game's own `Input` calls come out reading the virtual mouse and keyboard.
    /// </remarks>
    public sealed class InputFixtureBehaviour : MonoBehaviour
    {
        /// <summary>
        /// The one runtime type this fixture names on purpose.
        /// </summary>
        /// <remarks>
        /// InputMethodWeaver only takes an assembly whose IL actually references `Artel.Runtime`,
        /// and the IL carries that reference only where a type is used. Drop this field and the
        /// weaver skips the fixture: every `Input` call below keeps reading the real device, and
        /// the tests fail while the weaver itself is working.
        /// </remarks>
        public ArtelManager Manager;

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
