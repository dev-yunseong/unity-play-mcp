using UnityEngine;

namespace Artel.Auth
{
    /// <summary>
    /// 평문 폴백. OS 보안 저장소를 아직 붙이지 않은 플랫폼에서만 쓴다.
    /// </summary>
    /// <remarks>
    /// ponytail: 지금 이 자리로 오는 플랫폼은 없다 — 브라우저 로그인 자체가 Editor와
    /// Standalone에만 있어서, 다른 플랫폼은 저장할 토큰을 애초에 받지 못한다. 그쪽이
    /// 로그인을 지원하게 되면 그 플랫폼의 보안 저장소를 그때 붙인다.
    /// </remarks>
    internal sealed class PlayerPrefsSecretStore : IArtelSecretStore
    {
        public bool TryLoad(string key, out string value)
        {
            value = PlayerPrefs.GetString(key, string.Empty);
            return value.Length > 0;
        }

        public void Save(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
