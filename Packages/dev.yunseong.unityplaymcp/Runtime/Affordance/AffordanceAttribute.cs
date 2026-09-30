using System;

namespace UnityPlayMcp.Affordances
{
    /// <summary>
    /// 타입을 같은 어셈블리 리소스에 있는 `evidence` 항목에 연결한다.
    /// </summary>
    /// <remarks>
    /// `evidence` 본문을 attribute 에 실으면 게임 어셈블리가 크게 불어나므로 anchor 만 싣는다.
    /// attribute 는 난독화로 이름이 바뀌어도 타입에 붙어 남는다.
    /// managed stripping 이 High 면 attribute 가 사라지므로 리소스에 타입 이름도 적고 스캔은 이름 매칭으로 물러선다.
    /// 스트리핑과 난독화를 함께 하면 둘 다 실패하며, 이때는 빈 게임 대신 그 사실을 보고한다.
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

        /// <summary>어셈블리 `evidence` 리소스에서 이 타입의 항목.</summary>
        public int Anchor { get; }
    }
}
