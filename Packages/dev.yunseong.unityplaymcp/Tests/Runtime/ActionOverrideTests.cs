using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp.Tests
{
    public sealed class ActionOverrideTests
    {
        [TestCase(BuiltInAction.MoveMouse, "move_mouse")]
        [TestCase(BuiltInAction.MouseDown, "mouse_down")]
        [TestCase(BuiltInAction.MouseUp, "mouse_up")]
        [TestCase(BuiltInAction.EnterText, "enter_text")]
        [TestCase(BuiltInAction.KeyClick, "key_click")]
        [TestCase(BuiltInAction.KeyDown, "key_down")]
        [TestCase(BuiltInAction.KeyUp, "key_up")]
        [TestCase(BuiltInAction.SetAxis, "set_axis")]
        [TestCase(BuiltInAction.SetButton, "set_button")]
        [TestCase(BuiltInAction.PauseTime, "pause_time")]
        [TestCase(BuiltInAction.ResumeTime, "resume_time")]
        [TestCase(BuiltInAction.ResetGame, "reset_game")]
        [TestCase(BuiltInAction.StartReadings, "start_readings")]
        [TestCase(BuiltInAction.StopReadings, "stop_readings")]
        [TestCase(BuiltInAction.CaptureScreen, "capture_screen")]
        [TestCase(BuiltInAction.SetOverlay, "set_overlay")]
        public void OverrideUsesTheExistingWireName(BuiltInAction target, string method)
        {
            var registry = CreateRegistry((target, typeof(ValidOverride)));

            Assert.That(registry.TryGetOverride(method, out var replacement), Is.True);
            Assert.That(replacement, Is.TypeOf<ValidOverride>());
            Assert.That(registry.TryGetBuiltIn(method, out var fallback), Is.True);
            Assert.That(fallback, Is.Not.SameAs(replacement));
        }

        [Test]
        public void DuplicateCandidatesKeepTheirBuiltInAndDoNotBlockOtherTargets()
        {
            LogAssert.Expect(LogType.Warning, "[Unity Play MCP] Multiple overrides for set_button. Using the default action.");
            var registry = CreateRegistry(
                (BuiltInAction.SetButton, typeof(ThrowingOverride)),
                (BuiltInAction.SetButton, typeof(ValidOverride)),
                (BuiltInAction.CaptureScreen, typeof(ValidOverride)));

            Assert.That(registry.TryGetOverride("set_button", out _), Is.False);
            Assert.That(registry.TryGetBuiltIn("set_button", out _), Is.True);
            Assert.That(registry.TryGetOverride("capture_screen", out _), Is.True);
        }

        [Test]
        public void ConstructorFailureKeepsItsBuiltInAndDoesNotBlockOtherTargets()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex(@"Could not create override for capture_screen: fixture constructor failed"));
            var registry = CreateRegistry(
                (BuiltInAction.CaptureScreen, typeof(ThrowingOverride)),
                (BuiltInAction.SetButton, typeof(ValidOverride)));

            Assert.That(registry.TryGetOverride("capture_screen", out _), Is.False);
            Assert.That(registry.TryGetBuiltIn("capture_screen", out _), Is.True);
            Assert.That(registry.TryGetOverride("set_button", out _), Is.True);
        }

        [Test]
        public void UnknownTargetDoesNotBlockKnownOverrides()
        {
            var unknown = (BuiltInAction)999;
            LogAssert.Expect(LogType.Warning, "[Unity Play MCP] Unknown built-in action override target: 999");
            var registry = CreateRegistry(
                (unknown, typeof(ThrowingOverride)),
                (BuiltInAction.PauseTime, typeof(ValidOverride)));

            Assert.That(registry.TryGetOverride("pause_time", out _), Is.True);
        }

        private static ActionRegistry CreateRegistry(params (BuiltInAction Target, Type Implementation)[] candidates)
        {
            var context = new ActionContext(null, null, null, null, null, null);
            return new ActionRegistry(context, candidates);
        }

        // 어트리뷰트가 없는 타입을 전달해 다른 테스트의 자동 등록에는 영향을 주지 않는다.
        private sealed class ValidOverride : IUnityAction
        {
            public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed)
            {
                completed(ActionResultDto.Success(id));
                yield break;
            }
        }

        private sealed class ThrowingOverride : IUnityAction
        {
            private ThrowingOverride() => throw new InvalidOperationException("fixture constructor failed");

            public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed) =>
                throw new NotSupportedException();
        }
    }
}
