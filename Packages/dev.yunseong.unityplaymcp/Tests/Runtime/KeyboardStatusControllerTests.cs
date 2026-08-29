using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

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

        [Test]
        public void KeyboardOverlay_UsesDarkBrandPaletteByDefault()
        {
            var hadTheme = PlayerPrefs.HasKey("Artel.DarkTheme");
            var previousTheme = PlayerPrefs.GetInt("Artel.DarkTheme");
            var host = new GameObject("keyboard status");
            try
            {
                PlayerPrefs.SetInt("Artel.DarkTheme", 1);
                var controller = host.AddComponent<KeyboardStatusController>();
                typeof(KeyboardStatusController)
                    .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);
                var panel = host.transform
                    .Find("Artel Keyboard Status Canvas/Keyboard Status Panel");

                Assert.That(panel.GetComponent<Image>().color, Is.EqualTo((Color)KeyboardStatusController.DarkPanelColor));
                // 다크에서는 밝힌 coral을 써야 한다. 원본 #F04B3A는 다크 패널 위에서
                // 대비 4.5:1을 넘지 못한다.
                Assert.That(
                    panel.Find("Brand Accent").GetComponent<Image>().color,
                    Is.EqualTo((Color)KeyboardStatusController.DarkAccentColor));
                Assert.That(panel.Find("Separator"), Is.Not.Null);

                PlayerPrefs.SetInt("Artel.DarkTheme", 0);
                typeof(KeyboardStatusController)
                    .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);

                Assert.That(panel.GetComponent<Image>().color, Is.EqualTo((Color)KeyboardStatusController.LightPanelColor));
                Assert.That(
                    panel.Find("Brand Accent").GetComponent<Image>().color,
                    Is.EqualTo((Color)KeyboardStatusController.LightAccentColor));
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (hadTheme)
                {
                    PlayerPrefs.SetInt("Artel.DarkTheme", previousTheme);
                }
                else
                {
                    PlayerPrefs.DeleteKey("Artel.DarkTheme");
                }
            }
        }
    }
}
