namespace Artel.Auth
{
    /// <summary>
    /// 토큰처럼 평문으로 디스크에 남으면 안 되는 값을 두는 곳.
    /// </summary>
    /// <remarks>
    /// 구현은 플랫폼마다 다르다 — macOS는 키체인, Windows는 DPAPI, 나머지는 PlayerPrefs.
    /// 어느 구현이든 보호 범위는 사용자 계정 단위까지다. 같은 사용자로 도는 다른 프로세스는
    /// 여전히 값을 읽을 수 있으므로, 앱 단위 격리가 필요해지면 이 인터페이스로는 부족하다.
    ///
    /// 만료 시각이나 프로젝트 id처럼 비밀이 아닌 값은 여기 넣지 않는다. 넣을수록 느려지기만
    /// 하고(키체인은 값 하나마다 프로세스를 하나 띄운다) 지키는 것은 없다.
    /// </remarks>
    internal interface IArtelSecretStore
    {
        /// <summary>없으면 false, <paramref name="value"/>는 빈 문자열.</summary>
        bool TryLoad(string key, out string value);

        void Save(string key, string value);

        /// <summary>없는 키를 지우는 것은 성공으로 친다.</summary>
        void Delete(string key);
    }
}
