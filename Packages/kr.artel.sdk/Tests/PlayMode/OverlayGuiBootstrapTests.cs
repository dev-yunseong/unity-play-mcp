using System;
using System.Collections;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// 매니저를 붙이기만 하면 오버레이가 따라오는지 본다. 플레이 모드에서만 돌 수 있다:
    /// 매니저의 <c>Awake</c>가 <c>DontDestroyOnLoad</c>를 부르고 — 에디터 스크립트에서는
    /// 부를 수 없다 — GUI는 컨트롤러의 <c>Start</c>가 만든다.
    /// </summary>
    public sealed class OverlayGuiBootstrapTests
    {
        private const string DarkThemePlayerPrefsKey = "Artel.DarkTheme";

        // 세션은 여러 키에 흩어져 있다. 하나라도 흘리면 로그인된 화면으로 시작한다.
        private static readonly string[] SessionPlayerPrefsKeys =
        {
            "Artel.SdkToken",
            "Artel.SdkTokenExpiresAt",
            "Artel.SdkDisplayName",
            "Artel.ProjectId",
            "Artel.InstanceId"
        };

        private ArtelManager displacedInstance;
        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            // 토큰은 보안 저장소로 간다. 갈아끼우지 않으면 테스트가 개발자의 실제 키체인을 읽어
            // 로그인 화면 대신 등록 화면부터 시작한다.
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ClearSession();
            PlayerPrefs.DeleteKey(DarkThemePlayerPrefsKey);
            displacedInstance = ArtelManagerSlot.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            ArtelManagerSlot.Restore(displacedInstance);
            ClearSession();
            PlayerPrefs.DeleteKey(DarkThemePlayerPrefsKey);
            PlayerPrefs.Save();
            ArtelSecretStore.Current = null;
        }

        [UnityTest]
        public IEnumerator ArtelManager_CreatesOverlayGuiAutomatically()
        {
            host = new GameObject("Artel overlay test");
            var manager = host.AddComponent<ArtelManager>();

            var controller = host.GetComponent<ArtelOverlayController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(host.GetComponent<KeyboardStatusController>(), Is.Not.Null);

            // GUI는 컨트롤러의 Start가 만들고, Start는 다음 프레임에 돈다.
            yield return null;

            // 이름으로 찾지 않는다. 개발 빌드용 훅이 띄운 매니저의 오버레이도 같은 이름이라
            // 어느 쪽을 봤는지 알 수 없다. 캔버스는 컨트롤러의 자식이므로 host 아래에서 찾는다.
            var canvas = host.transform.Find("Artel Overlay Canvas");
            Assert.That(canvas, Is.Not.Null);

            var buttons = canvas.GetComponentsInChildren<Button>(true);
            var toggles = canvas.GetComponentsInChildren<Toggle>(true);
            var smoothCursorToggle = Array.Find(toggles, toggle => toggle.name == "부드러운 커서 Toggle");
            var loginButton = Array.Find(buttons, button => button.name == "로그인 Button");
            var connectButton = Array.Find(buttons, button => button.name == "연결 Button");

            Assert.That(manager.SdkId, Is.Not.Empty);
            Assert.That(manager.InstanceName, Is.Not.Empty);

            // Artel 토글, 고급, 연결, 로그아웃, 로그인, 다시 시도, 게이트 로그아웃, 나중에.
            Assert.That(buttons, Has.Length.EqualTo(8));

            // 키 입력창은 사라졌다. 남아 있으면 로그인 흐름과 두 입구가 공존한다.
            Assert.That(canvas.GetComponentInChildren<InputField>(true), Is.Null);
            Assert.That(loginButton, Is.Not.Null);
            Assert.That(loginButton.interactable, Is.True);
            Assert.That(connectButton, Is.Not.Null);
            Assert.That(connectButton.interactable, Is.False);
            Assert.That(smoothCursorToggle, Is.Not.Null);
            Assert.That(smoothCursorToggle.isOn, Is.False);
            Assert.That(Array.Find(toggles, toggle => toggle.name == "다크 모드 Toggle"), Is.Not.Null);
            Assert.That(canvas.GetComponentsInChildren<ArtelLogoGraphic>(true), Has.Length.EqualTo(3));
        }

        private static void ClearSession()
        {
            foreach (var key in SessionPlayerPrefsKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
        }
    }
}
