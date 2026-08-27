using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Input
{
    public sealed class MouseButtonKeyCodeTests
    {
        [Test]
        public void TryGetButton_MapsTheThreeMouseKeyCodesToTheirButtons()
        {
            Assert.That(MouseButtonKeyCode.TryGetButton(KeyCode.Mouse0, out var left), Is.True);
            Assert.That(left, Is.EqualTo(0));

            Assert.That(MouseButtonKeyCode.TryGetButton(KeyCode.Mouse1, out var right), Is.True);
            Assert.That(right, Is.EqualTo(1));

            Assert.That(MouseButtonKeyCode.TryGetButton(KeyCode.Mouse2, out var middle), Is.True);
            Assert.That(middle, Is.EqualTo(2));
        }

        [Test]
        public void TryGetButton_RefusesAKeyThatIsNotAMouseButton()
        {
            Assert.That(MouseButtonKeyCode.TryGetButton(KeyCode.Space, out var button), Is.False);
            Assert.That(button, Is.EqualTo(-1), "a refused mapping must not look like button 0");

            Assert.That(MouseButtonKeyCode.TryGetButton(KeyCode.None, out _), Is.False);
        }

        /// <summary>
        /// <c>KeyCode.Mouse3</c> 이후는 <see cref="VirtualMouseState.ButtonCount"/> 에 자리가 없다.
        /// 매핑해 버리면 존재하지 않는 버튼을 누르라는 요청이 조용히 성공한다.
        /// </summary>
        [Test]
        public void TryGetButton_StopsWhereTheVirtualMouseStops()
        {
            Assert.That(MouseButtonKeyCode.TryGetButton(KeyCode.Mouse3, out _), Is.False);
            Assert.That(MouseButtonKeyCode.TryGetButton(KeyCode.Mouse6, out _), Is.False);
        }

        [Test]
        public void TryGetButton_OnlyEverNamesAButtonTheVirtualMouseHas()
        {
            for (var key = KeyCode.Mouse0; key <= KeyCode.Mouse6; key++)
            {
                if (!MouseButtonKeyCode.TryGetButton(key, out var button))
                {
                    continue;
                }

                Assert.That(
                    VirtualMouseState.IsButton(button),
                    Is.True,
                    key + " mapped to a button the virtual mouse does not have");
            }
        }
    }
}
