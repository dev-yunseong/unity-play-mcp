using System;

namespace Artel.Affordances
{
    /// <summary>
    /// 타입을 자신의 근거에 연결한다. 근거는 같은 어셈블리의 리소스 안에 있다.
    /// </summary>
    /// <remarks>
    /// 예전에는 근거 자체를 실어 날랐다 — 기록 하나당 attribute 하나. 프로젝트 셋에서 재보니 게임
    /// 어셈블리가 제 크기의 3~8배로 불었고, 어느 쪽이 얼마나 부는지는 게임 크기와 아무 상관이 없었다.
    /// 늘어난 양의 98%가 JSON 텍스트였다.
    ///
    /// 지금 싣는 것은 anchor다. attribute는 이름이 바뀌어도 살아남는 유일한 결합 수단이기 때문이다 —
    /// 타입이 무엇으로 바뀌든 거기 붙어 있으므로 난독화된 빌드도 다시 이어 붙일 수 있다. 근거 자체는
    /// 리소스에 있고, 리소스는 메타데이터가 아니며, 요청받기 전까지 파싱되지 않고, 같은 텍스트를
    /// attribute로 실었을 때의 몇 분의 일로 압축된다.
    ///
    /// anchor가 유일한 입구는 아니다. managed stripping 을 High 로 두면 커스텀 attribute 가 통째로
    /// 사라지고 — 실측했고, 이것도 함께 사라졌다 — 리소스는 건드리지 않는다. 그래서 리소스에 각 타입의
    /// 이름도 함께 적어 두고, 스캔은 그 이름 매칭으로 물러선다. 두 처리 각각에 대해 둘 중 하나는
    /// 살아남는다. 세게 스트리핑하면서 난독화까지 한 빌드는 둘 다 무력화하는데, 그때는 빈 게임을
    /// 보고하는 대신 그렇다고 말한다.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AffordanceAttribute : Attribute
    {
        public AffordanceAttribute(int schemaVersion, int anchor)
        {
            SchemaVersion = schemaVersion;
            Anchor = anchor;
        }

        public int SchemaVersion { get; }

        /// <summary>어셈블리의 근거 리소스에서 이 타입에 해당하는 항목.</summary>
        public int Anchor { get; }
    }
}
