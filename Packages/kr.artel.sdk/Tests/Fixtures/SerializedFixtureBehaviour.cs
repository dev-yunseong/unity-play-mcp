using System;
using UnityEngine;

namespace Artel.Tests.Tracking
{
    /// <summary>
    /// Covers the field shapes a full scan has to deal with: what Unity serializes, what it does
    /// not, and the values that need lowering before they can be turned into JSON.
    /// </summary>
    public sealed class SerializedFixtureBehaviour : MonoBehaviour
    {
        public static int SharedCounter = 7;

        public int Level = 3;

        [SerializeField]
        private string title = "hidden but serialized";

        [NonSerialized]
        public int Scratch = 99;

        public readonly int Constant = 1;

        public Vector3 Spawn = new Vector3(1f, 2f, 3f);

        public GameObject Prefab;

        public string[] Tags = { "alpha", "beta" };

        public InventorySlot Slot = new InventorySlot { Item = "sword", Count = 2 };

        public LinkedNode Chain;

        public int Score
        {
            get { return Level * 10; }
        }

        public void SetTitle(string value)
        {
            title = value;
        }

        [Serializable]
        public sealed class InventorySlot
        {
            public string Item;
            public int Count;
        }

        [Serializable]
        public sealed class LinkedNode
        {
            public string Label;
            public LinkedNode Next;
        }
    }
}
