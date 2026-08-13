using System;
using UnityEngine;

namespace Artel
{
    internal static class ArtelSdkIdentity
    {
        private const string PlayerPrefsKey = "Artel.SdkId";

        public static string LoadOrCreate()
        {
            var storedId = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (Guid.TryParse(storedId, out _))
            {
                return storedId;
            }

            var sdkId = Guid.NewGuid().ToString("D");
            PlayerPrefs.SetString(PlayerPrefsKey, sdkId);
            PlayerPrefs.Save();
            return sdkId;
        }

        public static string ResetAndCreate()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            return LoadOrCreate();
        }
    }
}
