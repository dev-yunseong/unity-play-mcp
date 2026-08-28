using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    public sealed class ArtelBootstrapTests
    {
        [UnityTest]
        public IEnumerator EmptyScene_StillGetsAManager()
        {
            // The test scene carries nothing, so whatever is here was spawned by
            // the AfterSceneLoad hook — which is the whole point of the hook.
            yield return null;

            var spawned = GameObject.Find("Artel");
            Assert.IsNotNull(spawned, "Development builds should spawn the manager themselves.");
            Assert.IsNotNull(spawned.GetComponent<ArtelManager>());
        }
    }
}
