using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// An edit-mode test on purpose: outside play mode Unity never runs <c>OnEnable</c>, so no
    /// <see cref="EventSystem"/> ever registers itself as the current one. That is exactly the
    /// scene the dispatcher has to survive — a game that never used uGUI.
    /// </summary>
    public sealed class PointerEventFallbackTests
    {
        [Test]
        public void WithoutAnEventSystem_TheDispatcherStaysQuiet()
        {
            Assume.That(EventSystem.current, Is.Null);

            // move_mouse and mouse_down still reach the virtual mouse state in such a game; this
            // must not throw on the way there.
            var dispatcher = new PointerEventDispatcher();

            Assert.DoesNotThrow(() =>
            {
                dispatcher.MoveTo(new Vector2(100f, 100f));
                dispatcher.Press(0);
                dispatcher.MoveTo(new Vector2(300f, 200f));
                dispatcher.Release(0);
                dispatcher.ReleaseAll();
            });
        }
    }
}
