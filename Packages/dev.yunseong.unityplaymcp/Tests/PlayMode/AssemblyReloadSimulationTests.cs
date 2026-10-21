using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UnityPlayMcp.Tests
{
    public sealed class AssemblyReloadSimulationTests
    {
        [Test]
        public void PreservesPrivateReloadableStateButDropsRuntimeState()
        {
            var carrier = new GameObject("reload probe");
            try
            {
                var probe = carrier.AddComponent<ReloadStateProbe>();
                probe.ChangeState(carrier);
                AssemblyReloadSimulation.Rehearse(probe);
                probe.AssertReloadedState(carrier);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(carrier);
            }
        }
    }

    public sealed class ReloadStateProbe : MonoBehaviour
    {
        private bool initialized;
        private GameObject ownedObject;
        private Vector2 position;
        [NonSerialized] private bool runtimeInitialized;
        private object service;
        private readonly List<int> cache = new List<int>();

        public void ChangeState(GameObject owner)
        {
            initialized = runtimeInitialized = true;
            ownedObject = owner;
            position = new Vector2(5, 6);
            service = new object();
            cache.Add(42);
        }

        public void AssertReloadedState(GameObject owner)
        {
            Assert.That(initialized, Is.True);
            Assert.That(ownedObject, Is.SameAs(owner));
            Assert.That(position, Is.EqualTo(new Vector2(5, 6)));
            Assert.That(runtimeInitialized, Is.False);
            Assert.That(service, Is.Null);
            Assert.That(cache, Is.Empty);
        }
    }
}
