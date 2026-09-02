using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPlayMcp.Tests
{
    /// <remarks>
    /// 이름이 <c>H</c> 로 시작해야 한다. AfterSceneLoad 훅은 play mode 당 한 번만 돌아
    /// 오브젝트를 다시 띄워 주지 않는데, <c>MouseMessageActionTests</c> 와
    /// <c>PointerActionTests</c> 는 SetUp 에서 살아 있는 <see cref="UnityPlayMcpHost"/> 를 전부
    /// 파괴한다 — 포트 17311 을 자기 매니저에게 넘겨야 하기 때문이다. 실행 순서는 fixture 의
    /// 전체 이름을 알파벳 순으로 매기므로, 그 두 이름보다 뒤로 가는 이름을 쓰면 관찰 대상이
    /// 이미 없다. NUnit 3.5 의 <c>Order</c> 는 메서드에만 붙어 이 순서를 대신 고정하지 못한다.
    /// </remarks>
    public sealed class HostBootstrapTests
    {
        [UnityTest]
        public IEnumerator EmptyScene_StillGetsAManager()
        {
            // The test scene carries nothing, so whatever is here was spawned by
            // the AfterSceneLoad hook — which is the whole point of the hook.
            yield return null;

            var spawned = GameObject.Find("Unity Play MCP");
            Assert.IsNotNull(spawned, "Development builds should spawn the manager themselves.");
            Assert.IsNotNull(spawned.GetComponent<UnityPlayMcpHost>());
        }
    }
}
