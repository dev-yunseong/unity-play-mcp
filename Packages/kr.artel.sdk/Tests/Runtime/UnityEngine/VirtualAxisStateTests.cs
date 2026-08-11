using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Tests.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests.Input
{
    public sealed class VirtualAxisStateTests
    {
        private const string MissingAxis = "__artel_no_such_axis__";

        private GameObject cursorObject;
        private CursorController cursorController;

        [SetUp]
        public void SetUp()
        {
            cursorObject = new GameObject("axis action cursor");
            cursorController = cursorObject.AddComponent<CursorController>();
        }

        [TearDown]
        public void TearDown()
        {
            ArtelInput.ResetVirtualKeyboard();
            Object.DestroyImmediate(cursorObject);
        }

        [Test]
        public void Set_TakesEffectOnTheNextFrameAndHolds()
        {
            var axes = new VirtualAxisState();
            axes.Set("Horizontal", 1f, 10);

            Assert.That(axes.TryGetValue("Horizontal", 10, out _), Is.False, "the hold starts on the next frame");
            Assert.That(axes.TryGetValue("Horizontal", 11, out var value), Is.True);
            Assert.That(value, Is.EqualTo(1f));
            Assert.That(axes.TryGetValue("Horizontal", 11, out _), Is.True, "reading it must not consume it");
            Assert.That(axes.TryGetValue("Horizontal", 40, out _), Is.True, "a hold does not expire on its own");
        }

        [Test]
        public void TryGetValue_IsFalseForAnAxisNobodyIsDriving()
        {
            var axes = new VirtualAxisState();

            Assert.That(axes.TryGetValue("Horizontal", 5, out var value), Is.False);
            Assert.That(value, Is.EqualTo(0f), "the out value stays neutral so a caller cannot misread it");
        }

        [Test]
        public void Set_KeepsTheStartFrameSoTheButtonGoesDownOnlyOnce()
        {
            var axes = new VirtualAxisState();
            axes.Set("Jump", 1f, 1);

            Assert.That(axes.GetButtonDown("Jump", 2), Is.True);

            axes.Set("Jump", 1f, 5);

            Assert.That(axes.GetButtonDown("Jump", 6), Is.False, "repeating the request is not a new press");
            Assert.That(axes.GetButton("Jump", 6), Is.True);
        }

        [Test]
        public void Set_UpdatesTheValueOfAHoldAlreadyInForce()
        {
            var axes = new VirtualAxisState();
            axes.Set("Horizontal", 1f, 1);
            axes.Set("Horizontal", 0.25f, 5);

            Assert.That(axes.TryGetValue("Horizontal", 6, out var value), Is.True);
            Assert.That(value, Is.EqualTo(0.25f));
        }

        [Test]
        public void Release_ReportsTheUpEdgeOnExactlyOneFrame()
        {
            var axes = new VirtualAxisState();
            axes.Set("Jump", 1f, 1);

            Assert.That(axes.GetButton("Jump", 2), Is.True);

            axes.Release("Jump", 2);

            Assert.That(axes.GetButton("Jump", 2), Is.True, "still held on the frame that asked");
            Assert.That(axes.GetButtonUp("Jump", 3), Is.True);
            Assert.That(axes.GetButton("Jump", 3), Is.False);
            Assert.That(axes.GetButtonUp("Jump", 4), Is.False);
        }

        [Test]
        public void Release_HandsTheAxisBackOnTheSameFrameTheButtonGoesUp()
        {
            var axes = new VirtualAxisState();
            axes.Set("Horizontal", 1f, 1);
            axes.Release("Horizontal", 2);

            Assert.That(axes.GetButtonUp("Horizontal", 3), Is.True);
            Assert.That(
                axes.TryGetValue("Horizontal", 3, out _),
                Is.False,
                "the real input is the one to report from the release frame on");
        }

        [Test]
        public void NegativeValue_IsNotAButtonPress()
        {
            var axes = new VirtualAxisState();
            axes.Set("Horizontal", -1f, 1);

            Assert.That(axes.TryGetValue("Horizontal", 2, out var value), Is.True);
            Assert.That(value, Is.EqualTo(-1f));
            Assert.That(axes.GetButton("Horizontal", 2), Is.False, "only the positive side is the button");
            Assert.That(axes.GetButtonDown("Horizontal", 2), Is.False);

            axes.Release("Horizontal", 2);

            Assert.That(axes.GetButtonUp("Horizontal", 3), Is.False);
        }

        /// <summary>
        /// Pins the documented limit: button edges come from the hold starting and being released,
        /// not from the value crossing zero. Changing that has to break this test first.
        /// </summary>
        [Test]
        public void FlippingTheValueSignReportsNoButtonEdge()
        {
            var axes = new VirtualAxisState();
            axes.Set("Horizontal", 1f, 1);

            Assert.That(axes.GetButton("Horizontal", 2), Is.True);

            axes.Set("Horizontal", -1f, 2);

            Assert.That(axes.GetButton("Horizontal", 3), Is.False, "the button reads as released");
            Assert.That(axes.GetButtonUp("Horizontal", 3), Is.False, "but the crossing is not an edge");
            Assert.That(axes.GetButtonUp("Horizontal", 4), Is.False);
        }

        [Test]
        public void ReleaseAll_LetsGoOfEveryHeldAxis()
        {
            var axes = new VirtualAxisState();
            axes.Set("Horizontal", 1f, 1);
            axes.Set("Jump", 1f, 1);

            axes.ReleaseAll(5);

            Assert.That(axes.GetButtonUp("Horizontal", 6), Is.True);
            Assert.That(axes.GetButtonUp("Jump", 6), Is.True);
            Assert.That(axes.TryGetValue("Horizontal", 6, out _), Is.False);
            Assert.That(axes.TryGetValue("Jump", 6, out _), Is.False);
        }

        [Test]
        public void Refresh_DropsAReleasedHoldOnceItsUpEdgeHasPassed()
        {
            var axes = new VirtualAxisState();
            axes.Set("Jump", 1f, 1);
            axes.Release("Jump", 2);

            axes.Refresh(3);

            Assert.That(axes.GetButtonUp("Jump", 3), Is.True, "the up frame itself must survive the refresh");

            axes.Refresh(4);

            Assert.That(axes.GetButtonUp("Jump", 3), Is.False, "and is gone once the frame has passed");
        }

        [Test]
        public void Clear_ForgetsEverythingWithoutAnUpEdge()
        {
            var axes = new VirtualAxisState();
            axes.Set("Horizontal", 1f, 1);

            axes.Clear();

            Assert.That(axes.TryGetValue("Horizontal", 2, out _), Is.False);
            Assert.That(axes.GetButtonUp("Horizontal", 2), Is.False);
        }

        [Test]
        public void ActionExecutor_RejectsSetAxisWithoutBothParameters()
        {
            var result = ExecuteAction(3, "set_axis", new List<object> { "Horizontal" });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("set_axis requires params"));
        }

        [TestCase(5d)]
        [TestCase(-1.5d)]
        [TestCase("sideways")]
        public void ActionExecutor_RejectsAnAxisValueOutsideTheRange(object value)
        {
            var result = ExecuteAction(4, "set_axis", new List<object> { "Horizontal", value });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("set_axis requires params"));
        }

        [Test]
        public void ActionExecutor_RejectsSetButtonWithoutAFlag()
        {
            var result = ExecuteAction(5, "set_button", new List<object> { "Jump", "maybe" });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("set_button requires params"));
        }

        [TestCase("set_axis")]
        [TestCase("set_button")]
        public void ActionExecutor_RejectsAnAxisTheInputManagerDoesNotHave(string method)
        {
            var parameter = method == "set_axis" ? (object)1d : true;
            var result = ExecuteAction(6, method, new List<object> { MissingAxis, parameter });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain(MissingAxis));
        }

        [UnityTest]
        public IEnumerator IlPostProcessor_ReroutesUnityAxisCallsToArtelInput()
        {
            var host = new GameObject("axis fixture");
            var fixture = host.AddComponent<TrackedFixtureBehaviour>();
            try
            {
                Assert.That(
                    fixture.ReadHorizontalAxis(),
                    Is.EqualTo(0f),
                    "an axis nobody is driving reports the real input, not a virtual zero");
                Assert.That(fixture.ReadJumpButton(), Is.False);

                Assert.That(
                    ExecuteAction(7, "set_axis", new List<object> { "Horizontal", 1d }).IsSuccess,
                    Is.True);
                Assert.That(
                    ExecuteAction(8, "set_button", new List<object> { "Jump", true }).IsSuccess,
                    Is.True);
                yield return null;

                Assert.That(fixture.ReadHorizontalAxis(), Is.EqualTo(1f));
                Assert.That(fixture.ReadHorizontalAxisRaw(), Is.EqualTo(1f));
                Assert.That(fixture.ReadJumpButton(), Is.True);
                Assert.That(fixture.ReadJumpButtonDown(), Is.True);
                Assert.That(fixture.ReadJumpButtonDown(), Is.True, "reading it must not consume it");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private ActionResultDto ExecuteAction(int actionId, string method, List<object> parameters)
        {
            var executor = new ActionExecutor(
                new SceneScanner(), cursorController, new PointerEventDispatcher());
            ActionResultDto result = null;
            Drain(executor.Execute(actionId, method, parameters, value => result = value));
            return result;
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }
    }
}
