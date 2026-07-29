using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Input
{
    public sealed class VirtualMouseStateTests
    {
        [Test]
        public void Press_ExposesDownHeldAndUpAsFrameSnapshots()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 10);

            Assert.That(mouse.GetButtonDown(0, 10), Is.False, "the press starts on the next frame");
            Assert.That(mouse.GetButtonDown(0, 11), Is.True);
            Assert.That(mouse.GetButtonDown(0, 11), Is.True, "reading it must not consume it");
            Assert.That(mouse.GetButton(0, 11), Is.True);
            Assert.That(mouse.GetButtonDown(0, 12), Is.False);
            Assert.That(mouse.GetButton(0, 12), Is.True);

            mouse.Release(0, 12);

            Assert.That(mouse.GetButton(0, 12), Is.True, "still down on the frame that asked");
            Assert.That(mouse.GetButtonUp(0, 13), Is.True);
            Assert.That(mouse.GetButton(0, 13), Is.False);
            Assert.That(mouse.GetButtonUp(0, 14), Is.False);
        }

        [Test]
        public void Press_LeavesTheOtherButtonsAlone()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(1, 3);

            Assert.That(mouse.GetButton(1, 4), Is.True);
            Assert.That(mouse.GetButton(0, 4), Is.False);
            Assert.That(mouse.GetButton(2, 4), Is.False);
            Assert.That(mouse.IsAnyButtonHeld(4), Is.True);
        }

        [Test]
        public void Press_IgnoresAButtonThatDoesNotExist()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(7, 3);
            mouse.Release(-1, 3);

            Assert.That(mouse.GetButton(7, 4), Is.False);
            Assert.That(mouse.IsAnyButtonHeld(4), Is.False);
        }

        [Test]
        public void ReleaseAll_LetsGoOfEveryHeldButton()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 1);
            mouse.Press(2, 1);

            mouse.ReleaseAll(5);

            Assert.That(mouse.GetButtonUp(0, 6), Is.True);
            Assert.That(mouse.GetButtonUp(2, 6), Is.True);
            Assert.That(mouse.IsAnyButtonHeld(6), Is.False);
        }

        [Test]
        public void Position_IsUnclaimedUntilTheAgentMovesIt()
        {
            var mouse = new VirtualMouseState();
            var restingHand = new Vector2(10f, 10f);

            Assert.That(mouse.OwnsPointer(restingHand), Is.False);

            mouse.MoveTo(new Vector2(120f, 240f), restingHand);

            Assert.That(mouse.OwnsPointer(restingHand), Is.True);
            Assert.That(mouse.Position, Is.EqualTo(new Vector2(120f, 240f)));

            mouse.Clear();

            Assert.That(mouse.OwnsPointer(restingHand), Is.False);
        }

        [Test]
        public void OwnsPointer_GivesTheMouseBackAsSoonAsAPersonMovesIt()
        {
            var mouse = new VirtualMouseState();
            mouse.MoveTo(new Vector2(120f, 240f), new Vector2(10f, 10f));

            // Jitter from a mouse sitting still is not somebody reaching for it.
            Assert.That(mouse.OwnsPointer(new Vector2(12f, 11f)), Is.True);

            Assert.That(mouse.OwnsPointer(new Vector2(400f, 300f)), Is.False);

            // And it stays given back: the claim is not re-taken by the hand coming to rest again.
            Assert.That(mouse.OwnsPointer(new Vector2(10f, 10f)), Is.False);
        }

        [Test]
        public void ReleasePointer_LeavesAHeldButtonAlone()
        {
            // The connection ending hands the pointer back, but a button still has to report its
            // release on the right frame rather than vanishing mid-press.
            var mouse = new VirtualMouseState();
            mouse.MoveTo(new Vector2(120f, 240f), Vector2.zero);
            mouse.Press(0, 1);

            mouse.ReleasePointer();

            Assert.That(mouse.OwnsPointer(Vector2.zero), Is.False);
            Assert.That(mouse.GetButton(0, 2), Is.True);
        }

        [Test]
        public void Refresh_ForgetsAButtonOnceItsReleaseFrameHasPassed()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 1);
            mouse.Release(0, 1);

            // The release frame itself still has to report the up, so only later frames may drop it.
            mouse.Refresh(2);
            Assert.That(mouse.GetButtonUp(0, 2), Is.True);

            mouse.Refresh(3);
            Assert.That(mouse.GetButtonUp(0, 3), Is.False);
            Assert.That(mouse.GetButton(0, 3), Is.False);
        }
    }
}
