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
        [Test]
        public void DuplicateCandidatesKeepTheBuiltInWithoutConstructingEither()
        {
            LogAssert.Expect(LogType.Warning, "[Unity Play MCP] Multiple overrides for set_button. Using the default action.");
            Assert.That(ActionRegistry.CreateSetButtonOverride(new[]
            {
                typeof(ThrowingOverride), typeof(MissingConstructorOverride)
            }), Is.Null);
        }

        [Test]
        public void ConstructorFailureKeepsTheBuiltIn()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex(@"Could not create override for set_button: fixture constructor failed"));
            Assert.That(ActionRegistry.CreateSetButtonOverride(new[] { typeof(ThrowingOverride) }), Is.Null);
        }

        // 어트리뷰트가 없는 타입을 전달해 다른 테스트의 자동 등록에는 영향을 주지 않는다.
        private sealed class ThrowingOverride : IUnityAction
        {
            private ThrowingOverride() => throw new InvalidOperationException("fixture constructor failed");

            public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed) =>
                throw new NotSupportedException();
        }

        private sealed class MissingConstructorOverride : IUnityAction
        {
            public MissingConstructorOverride(int ignored) { }

            public IEnumerator Execute(int id, List<object> parameters, Action<ActionResultDto> completed) =>
                throw new NotSupportedException();
        }
    }
}
