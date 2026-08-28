using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Tests.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests.Input
{
    public sealed class VirtualKeyboardStateTests
    {
        private GameObject cursorObject;
        private CursorController cursorController;

        [SetUp]
        public void SetUp()
        {
            cursorObject = new GameObject("keyboard action cursor");
            cursorController = cursorObject.AddComponent<CursorController>();
        }

        [TearDown]
        public void TearDown()
        {
            ArtelInput.ResetVirtualKeyboard();
            Object.DestroyImmediate(cursorObject);
        }

        [Test]
        public void Click_ExposesDownHeldAndUpAsFrameSnapshots()
        {
            var keyboard = new VirtualKeyboardState();
            keyboard.Click(KeyCode.Space, 0.5f, 10);

            Assert.That(keyboard.GetKeyDown(KeyCode.Space, 10, 1f), Is.False);
            Assert.That(keyboard.GetKeyDown(KeyCode.Space, 11, 1f), Is.True);
            Assert.That(keyboard.GetKeyDown(KeyCode.Space, 11, 1f), Is.True);
            Assert.That(keyboard.GetKey(KeyCode.Space, 11, 1f), Is.True);
            Assert.That(keyboard.GetKey(KeyCode.Space, 12, 1.49f), Is.True);
            Assert.That(keyboard.GetKeyUp(KeyCode.Space, 13, 1.5f), Is.True);
            Assert.That(keyboard.GetKeyUp(KeyCode.Space, 13, 1.5f), Is.True);
            Assert.That(keyboard.GetKey(KeyCode.Space, 13, 1.5f), Is.False);
            Assert.That(keyboard.GetKeyUp(KeyCode.Space, 14, 1.6f), Is.False);
        }

        [Test]
        public void AnyKey_UsesSameVirtualFrameState()
        {
            var keyboard = new VirtualKeyboardState();
            keyboard.Click(KeyCode.A, 1f, 3);

            Assert.That(keyboard.AnyKeyDown(3, 0f), Is.False);
            Assert.That(keyboard.AnyKeyDown(4, 0.1f), Is.True);
            Assert.That(keyboard.AnyKey(4, 0.1f), Is.True);
            Assert.That(keyboard.AnyKeyDown(5, 0.2f), Is.False);
            Assert.That(keyboard.AnyKey(5, 0.2f), Is.True);
        }

        [Test]
        public void Press_HoldsUntilReleaseInsteadOfExpiring()
        {
            var keyboard = new VirtualKeyboardState();
            keyboard.Press(KeyCode.LeftShift, 10);

            Assert.That(keyboard.GetKeyDown(KeyCode.LeftShift, 11, 1f), Is.True);
            Assert.That(keyboard.GetKey(KeyCode.LeftShift, 11, 1f), Is.True);

            // No duration to run out: the key is still down an hour of game time later.
            Assert.That(keyboard.GetKey(KeyCode.LeftShift, 500, 3600f), Is.True);
            Assert.That(keyboard.GetKeyUp(KeyCode.LeftShift, 500, 3600f), Is.False);

            keyboard.Release(KeyCode.LeftShift, 500);

            Assert.That(keyboard.GetKey(KeyCode.LeftShift, 500, 3600f), Is.True);
            Assert.That(keyboard.GetKeyUp(KeyCode.LeftShift, 501, 3600.1f), Is.True);
            Assert.That(keyboard.GetKey(KeyCode.LeftShift, 501, 3600.1f), Is.False);
            Assert.That(keyboard.GetKeyUp(KeyCode.LeftShift, 502, 3600.2f), Is.False);
        }

        [Test]
        public void Release_IgnoresAKeyThatWasNeverPressed()
        {
            var keyboard = new VirtualKeyboardState();
            keyboard.Release(KeyCode.LeftShift, 10);

            Assert.That(keyboard.GetKeyUp(KeyCode.LeftShift, 11, 1f), Is.False);
            Assert.That(keyboard.GetKey(KeyCode.LeftShift, 11, 1f), Is.False);
        }

        [Test]
        public void ReleaseAll_LetsGoOfEveryHeldKey()
        {
            var keyboard = new VirtualKeyboardState();
            keyboard.Press(KeyCode.LeftShift, 1);
            keyboard.Press(KeyCode.A, 1);

            keyboard.ReleaseAll(4);

            Assert.That(keyboard.GetKeyUp(KeyCode.LeftShift, 5, 1f), Is.True);
            Assert.That(keyboard.GetKeyUp(KeyCode.A, 5, 1f), Is.True);
            Assert.That(keyboard.AnyKey(5, 1f), Is.False);
        }

        [TestCase("key_down")]
        [TestCase("key_up")]
        public void ActionExecutor_RejectsAKeyHoldWithoutAKeyCode(string method)
        {
            var result = ExecuteAction(8, method, new List<object>());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain(method + " requires params [keyCode]."));
        }

        [Test]
        public void ActionExecutor_AcceptsKeyCodeNameAndDuration()
        {
            var result = ExecuteAction(
                2,
                "key_click",
                new List<object> { "Space", 0.5d });

            Assert.That(result.Id, Is.EqualTo(2));
            Assert.That(result.IsSuccess, Is.True);
        }

        [UnityTest]
        public IEnumerator IlPostProcessor_ReroutesUnityInputCallsToArtelInput()
        {
            var host = new GameObject("input fixture");
            var fixture = host.AddComponent<InputFixtureBehaviour>();
            try
            {
                var result = ExecuteAction(
                    2,
                    "key_click",
                    new List<object> { "Space", 0.5d });

                Assert.That(result.IsSuccess, Is.True);
                yield return null;

                Assert.That(fixture.ReadSpaceKeyDown(), Is.True);
                Assert.That(fixture.ReadSpaceKeyDown(), Is.True);
                Assert.That(fixture.ReadSpaceKey(), Is.True);
                Assert.That(fixture.ReadAnyKeyDown(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(null, 0.5d)]
        [TestCase("UnknownKey", 0.5d)]
        [TestCase("Space", 0d)]
        [TestCase("Space", -1d)]
        public void ActionExecutor_RejectsInvalidKeyClickParameters(object key, object duration)
        {
            var result = ExecuteAction(
                7,
                "key_click",
                new List<object> { key, duration });

            Assert.That(result.Id, Is.EqualTo(7));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("key_click requires"));
        }

        private ActionResultDto ExecuteAction(int actionId, string method, List<object> parameters)
        {
            var executor = new ActionExecutor(
                new TargetLookup(), cursorController, new PointerEventDispatcher());
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
