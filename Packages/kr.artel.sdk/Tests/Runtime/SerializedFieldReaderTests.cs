using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Artel.Tests.Tracking;
using Artel.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    public sealed class SerializedFieldReaderTests
    {
        private GameObject gameObject;
        private SerializedFixtureBehaviour fixture;
        private SerializedFieldReader reader;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("serialized field reader target");
            fixture = gameObject.AddComponent<SerializedFixtureBehaviour>();
            reader = new SerializedFieldReader();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void GetSerializedFields_FollowsUnitySerializationRules()
        {
            var names = reader.GetSerializedFields(typeof(SerializedFixtureBehaviour))
                .Select(field => field.Name)
                .ToList();

            Assert.That(names, Contains.Item("Level"));
            Assert.That(names, Contains.Item("title"), "[SerializeField] opens a private field.");
            Assert.That(names, Has.No.Member("Scratch"), "[NonSerialized] closes a public field.");
            Assert.That(names, Has.No.Member("SharedCounter"), "static fields are not serialized.");
            Assert.That(names, Has.No.Member("Constant"), "readonly fields are not serialized.");
            Assert.That(names, Has.No.Member("Score"), "properties are not serialized.");
        }

        [Test]
        public void ReadValue_LowersUnityStructsToTheirFields()
        {
            // Handing a Vector3 to Newtonsoft recurses through `normalized`; the reader has to
            // produce a plain map first.
            var spawn = Read("Spawn") as Dictionary<string, object>;

            Assert.That(spawn, Is.Not.Null);
            Assert.That(spawn["x"], Is.EqualTo(1f));
            Assert.That(spawn["y"], Is.EqualTo(2f));
            Assert.That(spawn["z"], Is.EqualTo(3f));
        }

        [Test]
        public void ReadValue_DescribesUnityObjectReferencesWithoutFollowingThem()
        {
            var referenced = new GameObject("referenced prefab");
            try
            {
                fixture.Prefab = referenced;

                var stub = Read("Prefab") as Dictionary<string, object>;

                Assert.That(stub, Is.Not.Null);
                Assert.That(stub["instanceId"], Is.EqualTo(referenced.GetInstanceID()));
                Assert.That(stub["name"], Is.EqualTo("referenced prefab"));
                Assert.That(stub["type"], Is.EqualTo(typeof(GameObject).FullName));
                Assert.That(stub.ContainsKey("children"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(referenced);
            }
        }

        [Test]
        public void ReadValue_ReadsUnassignedReferenceAsNull()
        {
            Assert.That(Read("Prefab"), Is.Null);
        }

        [Test]
        public void ReadValue_LowersNestedSerializableObjects()
        {
            var slot = Read("Slot") as Dictionary<string, object>;

            Assert.That(slot, Is.Not.Null);
            Assert.That(slot["Item"], Is.EqualTo("sword"));
            Assert.That(slot["Count"], Is.EqualTo(2));
        }

        [Test]
        public void ReadValue_StopsAtSelfReferencingObjects()
        {
            var node = new SerializedFixtureBehaviour.LinkedNode { Label = "loop" };
            node.Next = node;
            fixture.Chain = node;

            var chain = Read("Chain") as Dictionary<string, object>;

            Assert.That(chain, Is.Not.Null);
            Assert.That(chain["Label"], Is.EqualTo("loop"));
            Assert.That(chain["Next"], Is.Null, "The cycle is cut where it closes.");
        }

        [Test]
        public void ReadValue_TruncatesLongCollectionsAndStrings()
        {
            fixture.Tags = Enumerable.Range(0, 100).Select(index => "tag" + index).ToArray();
            fixture.SetTitle(new string('a', 2000));

            var tags = Read("Tags") as List<object>;
            var title = Read("title") as string;

            Assert.That(tags, Has.Count.EqualTo(64));
            Assert.That(tags[0], Is.EqualTo("tag0"));
            Assert.That(title, Has.Length.EqualTo(1025));
            Assert.That(title, Does.EndWith("…"));
        }

        private object Read(string fieldName)
        {
            return reader.ReadValue(Field(fieldName), fixture);
        }

        private FieldInfo Field(string fieldName)
        {
            return reader.GetSerializedFields(typeof(SerializedFixtureBehaviour))
                .Single(field => field.Name == fieldName);
        }
    }
}
