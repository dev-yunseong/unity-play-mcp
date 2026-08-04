using System;
using UnityEngine;

namespace Artel.Auth
{
    /// <summary>
    /// 플랫폼에 맞는 <see cref="IArtelSecretStore"/> 하나를 골라 두고 그리로 넘긴다.
    /// </summary>
    /// <remarks>
    /// 읽기와 지우기의 실패는 접는다 — 키체인이 잠겼든 파일이 깨졌든 사용자가 할 수 있는 일은
    /// 다시 로그인하는 것뿐이라, 오류를 위로 올려 봐야 로그인 화면 대신 실패 화면만 보여 준다.
    ///
    /// 쓰기 실패는 접지 않는다. 접으면 로그인한 줄 알고 있다가 다음 실행에서 조용히 로그아웃된
    /// 상태로 돌아온다. 무엇보다 여기서 PlayerPrefs로 물러서는 폴백을 두면 안 된다 — 이 클래스가
    /// 있는 이유가 토큰을 평문으로 남기지 않는 것인데, 그 폴백은 그것을 조용히 되돌린다.
    /// </remarks>
    internal static class ArtelSecretStore
    {
        private static IArtelSecretStore current;

        /// <summary>테스트가 실제 키체인이나 사용자 프로필을 건드리지 않도록 갈아끼울 수 있다.</summary>
        internal static IArtelSecretStore Current
        {
            get { return current ?? (current = CreatePlatformStore()); }
            set { current = value; }
        }

        public static bool TryLoad(string key, out string value)
        {
            try
            {
                return Current.TryLoad(key, out value);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Artel] 보안 저장소를 읽지 못했습니다: " + exception.Message);
                value = string.Empty;
                return false;
            }
        }

        public static void Save(string key, string value)
        {
            Current.Save(key, value);
        }

        public static void Delete(string key)
        {
            try
            {
                Current.Delete(key);
            }
            catch (Exception exception)
            {
                // 로그아웃은 계속 진행한다. 다만 토큰이 남았다는 사실은 조용히 넘기지 않는다.
                Debug.LogError("[Artel] 보안 저장소에서 값을 지우지 못했습니다: " + exception.Message);
            }
        }

        internal static IArtelSecretStore CreatePlatformStore()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return new KeychainSecretStore();
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new DpapiSecretStore();
#else
            return new PlayerPrefsSecretStore();
#endif
        }
    }
}
