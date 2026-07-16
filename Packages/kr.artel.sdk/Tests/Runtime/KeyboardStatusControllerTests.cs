using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Input
{
    public sealed class KeyboardStatusControllerTests
    {
        [Test]
        public void FormatPressedKeys_UsesReadableKeyLabels()
        {
            var result = KeyboardStatusController.FormatPressedKeys(
                new List<KeyCode> { KeyCode.W, KeyCode.LeftShift, KeyCode.Space });

            Assert.That(result, Is.EqualTo("W  +  LEFT SHIFT  +  SPACE"));
        }

        [Test]
        public void FormatPressedKeys_UsesPlaceholderWhenNoKeyIsPressed()
        {
            Assert.That(
                KeyboardStatusController.FormatPressedKeys(new List<KeyCode>()),
                Is.EqualTo("—"));
        }
    }
}
