using System;
using System.Collections;
using System.Collections.Generic;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 빌드 설정이 알지 못하는 씬들.
    /// </summary>
    /// <remarks>
    /// 순회는 빌드 인덱스를 따라가는데, 씬을 주소로 로드하는 프로젝트는 그것을 거기 넣을 이유가 없다 — Chop Chop 에서
    /// 실측하니 등록된 것은 하나이고 디스크에는 쉰이 있었으므로, 순회는 하나를 방문하고 아무것도 없는 게임을 보고했다.
    ///
    /// 여기서 청하지 않고 바깥에서 채워 넣는다. Addressables 는 프로젝트에 없을 수도 있는 패키지이고, 이 어셈블리는
    /// 존재하지 않을 수 있는 것을 참조할 수 없다. 참조할 수 있는 어셈블리는 그것이 있을 때만 컴파일되어 제 답을 건넨다.
    /// null 로 두면 모든 것이 전과 똑같이 동작한다.
    /// </remarks>
    public static class ExtraScenes
    {
        /// <summary>주소로 닿을 수 있는 모든 씬. <see cref="Load"/> 가 원하는 방식으로 이름 붙인 것.</summary>
        public static Func<List<string>> List;

        /// <summary>그중 하나를 홀로 띄운다. 끝까지 돌려야 하는 코루틴으로.</summary>
        public static Func<string, IEnumerator> Load;

        internal static bool Available => List != null && Load != null;
    }
}
