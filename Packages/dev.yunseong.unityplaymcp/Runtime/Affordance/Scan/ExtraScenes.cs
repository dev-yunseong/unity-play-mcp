using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 빌드 설정에 없는 씬들.
    /// </summary>
    /// <remarks>
    /// 씬을 주소로 로드하는 프로젝트는 빌드 인덱스에 씬을 넣지 않으므로 순회가 대부분을 놓친다.
    /// Addressables 는 없을 수도 있는 패키지라 이 어셈블리가 참조하지 못하고, 그것을 참조하는 어셈블리가 값을 채운다.
    /// null 이면 빌드 인덱스만 순회한다.
    /// </remarks>
    public static class ExtraScenes
    {
        /// <summary>주소로 로드할 수 있는 모든 씬. <see cref="Load"/> 가 받는 이름이다.</summary>
        public static Func<List<string>> List;

        /// <summary>씬 하나를 Single 로 로드한다. 끝까지 돌려야 하는 coroutine 이다.</summary>
        public static Func<string, IEnumerator> Load;

        internal static bool Available => List != null && Load != null;
    }
}
