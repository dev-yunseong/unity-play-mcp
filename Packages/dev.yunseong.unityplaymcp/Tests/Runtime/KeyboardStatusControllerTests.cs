using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp.Tests.Input
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
            var hadTheme = PlayerPrefs.HasKey("UnityPlayMcp.DarkTheme");
            var previousTheme = PlayerPrefs.GetInt("UnityPlayMcp.DarkTheme");
            var host = new GameObject("keyboard status");
            try
            {
                PlayerPrefs.SetInt("UnityPlayMcp.DarkTheme", 1);
                var controller = host.AddComponent<KeyboardStatusController>();

                // EditMode 에서는 AddComponent 가 OnEnable 을 부르지 않는다. GUI 가 거기서 만들어지므로
                // 직접 부르지 않으면 찾으려는 panel 이 끝까지 존재하지 않는다.
                typeof(KeyboardStatusController)
                    .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);
                var panel = host.transform
                    .Find("Unity Play MCP Keyboard Status Canvas/Keyboard Status Panel");

                Assert.That(panel.GetComponent<Image>().color, Is.EqualTo((Color)KeyboardStatusController.DarkPanelColor));
                // 다크에서는 밝힌 coral을 써야 한다. 원본 #F04B3A는 다크 패널 위에서
                // 대비 4.5:1을 넘지 못한다.
                Assert.That(
                    panel.Find("Brand Accent").GetComponent<Image>().color,
                    Is.EqualTo((Color)KeyboardStatusController.DarkAccentColor));
                Assert.That(panel.Find("Separator"), Is.Not.Null);

                PlayerPrefs.SetInt("UnityPlayMcp.DarkTheme", 0);
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
                    PlayerPrefs.SetInt("UnityPlayMcp.DarkTheme", previousTheme);
                }
                else
                {
                    PlayerPrefs.DeleteKey("UnityPlayMcp.DarkTheme");
                }
            }
        }
    }
}
