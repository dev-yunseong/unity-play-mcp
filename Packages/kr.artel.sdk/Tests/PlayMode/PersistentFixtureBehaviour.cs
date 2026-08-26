using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// 자기 자신을 <c>DontDestroyOnLoad</c>로 넘겨 자기가 태어난 씬보다 오래 사는 오브젝트.
    /// 게임의 싱글톤 매니저나 튜토리얼 컨트롤러가 하는 일과 같다.
    /// </summary>
    /// <remarks>
    /// 씬에 심어 두고 저장할 수 있어야 해서 자체 파일에 둔 public 타입이다. Unity는 파일 이름과
    /// 클래스 이름이 같은 MonoBehaviour만 씬에 붙일 수 있다.
    /// </remarks>
    public sealed class PersistentFixtureBehaviour : MonoBehaviour
    {
        /// <summary>씬에 심어 둔 인스턴스를 이름으로 찾을 때 쓰는 root 이름.</summary>
        public const string RootName = "Artel Persistent Fixture";

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }
    }
}
