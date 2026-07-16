using System.Collections;
using System.Collections.Generic;
using Artel.Tests.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests.Input
{
    public sealed class VirtualKeyboardStateTests
    {
        [TearDown]
        public void TearDown()
        {
            ArtelInput.ResetVirtualKeyboard();
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
        public void ActionExecutor_AcceptsKeyCodeNameAndDuration()
        {
            var executor = new ActionExecutor(new SceneScanner());

            var result = executor.Execute(
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
            var fixture = host.AddComponent<TrackedFixtureBehaviour>();
            var executor = new ActionExecutor(new SceneScanner());

            try
            {
                var result = executor.Execute(
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
            var executor = new ActionExecutor(new SceneScanner());

            var result = executor.Execute(
                7,
                "key_click",
                new List<object> { key, duration });

            Assert.That(result.Id, Is.EqualTo(7));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("key_click requires"));
        }
    }
}
