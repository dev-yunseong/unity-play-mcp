using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// Edit mode on purpose: outside play mode <c>OnEnable</c> never runs, so no
    /// <see cref="EventSystem"/> becomes current, like a game that never used uGUI.
    /// </summary>
    public sealed class PointerEventFallbackTests
    {
        [Test]
        public void WithoutAnEventSystem_TheDispatcherStaysQuiet()
        {
            Assume.That(EventSystem.current, Is.Null);

            // move_mouse and mouse_down still reach the virtual mouse in such a game, so this must not throw.
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
