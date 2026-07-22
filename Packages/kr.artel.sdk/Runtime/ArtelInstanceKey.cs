using System;
using UnityEngine;

namespace Artel
{
    internal static class ArtelInstanceKey
    {
        private const string PlayerPrefsKey = "Artel.InstanceKey";

        public static bool TryLoad(out string instanceKey)
        {
            var storedKey = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(storedKey))
            {
                instanceKey = string.Empty;
                return false;
            }

            instanceKey = storedKey.Trim();
            return true;
        }

        public static void Save(string instanceKey)
        {
            if (string.IsNullOrWhiteSpace(instanceKey))
            {
                throw new ArgumentException("Instance key is required.", nameof(instanceKey));
            }

            PlayerPrefs.SetString(PlayerPrefsKey, instanceKey.Trim());
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            PlayerPrefs.Save();
        }
    }
}
