using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 런타임에 값을 되읽을 수 있도록 리플렉션용으로 이름 붙인 멤버.
    /// </summary>
    /// <remarks>
    /// 목록은 조건과 효과를 읽을 때 피연산자의 필드에서 만들어지므로 게임이 따로 표시할 필요가 없다.
    ///
    /// 표시용 문자열이 아니라 선언 타입과 멤버로 나눠 적는다. 런타임이 표시용 문자열을 다시 파싱하지 않게 한다.
    ///
    /// 필드만 본다. 호출 결과를 알려고 호출하면 게임 상태를 바꿀 수 있으므로 호출 결과는 감시하지 않는다.
    /// </remarks>
    internal sealed class WatchTarget
    {
        private const string BackingSuffix = ">k__BackingField";

        /// <summary>선언 타입의 컴파일 시점 이름.</summary>
        internal string Declaring;

        internal string Member;

        /// <summary>
        /// 컴파일러가 만든 backing field 일 때 그 프로퍼티 이름.
        /// </summary>
        /// <remarks>
        /// 리플렉션은 <c>&lt;Instance&gt;k__BackingField</c> 로 찾고 `evidence` 는 <c>Instance</c> 로 부르므로
        /// 읽는 쪽이 `pulse` 를 조건에 이어 붙이려면 둘 다 필요하다.
        /// </remarks>
        internal string Property;

        /// <summary>값의 타입. 읽는 쪽이 비교 방식을 정하는 데 쓴다.</summary>
        internal string Type;

        /// <summary>
        /// static 필드일 때 참.
        /// </summary>
        /// <remarks>
        /// GameObject 를 걷는 스캔은 인스턴스 필드만 담을 수 있으므로 static 필드는 따로 실어야 한다.
        /// </remarks>
        internal bool Static;

        /// <summary>같은 멤버를 가리키는 대상을 하나로 묶는 키.</summary>
        internal string Key => Declaring + "::" + Member;

        /// <summary>
        /// 검사되는 값이 필드 자체가 아닐 때 필드에서 읽은 프로퍼티 이름. 필드 자체가 값이면 null.
        /// </summary>
        /// <remarks>
        /// <c>spellCards.Count == 1</c> 은 필드 <c>spellCards</c> 의 <c>Count</c> 를 비교한다. 읽는 쪽이 타입에서
        /// 추론하지 않도록 명시한다.
        /// </remarks>
        internal string Via;

        /// <summary>
        /// 값이 getter 호출 결과일 때 그 receiver 인 필드.
        /// </summary>
        /// <remarks>
        /// 인자 없는 getter 이고 receiver 가 필드일 때만 받는다. 읽은 프로퍼티는 <see cref="Via"/> 에 적는다.
        /// </remarks>
        internal static WatchTarget ReadOff(
            Instruction from, Instruction boundary, MethodDefinition within)
        {
            if (from == null ||
                (from.OpCode.Code != Code.Call && from.OpCode.Code != Code.Callvirt) ||
                !(from.Operand is MethodReference read) ||
                !read.HasThis || read.Parameters.Count != 0 ||
                !read.Name.StartsWith("get_", System.StringComparison.Ordinal))
            {
                return null;
            }

            var target = From(IlReading.Rooted(read, from, boundary, within));

            if (target == null)
            {
                return null;
            }

            // `transform` 과 `gameObject` 는 필드 자체와 같은 객체를 가리키므로 Via 로 적지 않는다.
            var name = read.Name.Substring(4);
            target.Via = name == "transform" || name == "gameObject" ? null : name;
            return target;
        }

        /// <summary>컴파일러가 만든 backing field 가 속한 프로퍼티 이름, 또는 null.</summary>
        private static string Behind(string name)
        {
            return name != null && name.Length > BackingSuffix.Length + 1 &&
                   name[0] == '<' && name.EndsWith(BackingSuffix, System.StringComparison.Ordinal)
                ? name.Substring(1, name.Length - BackingSuffix.Length - 1)
                : null;
        }

        /// <summary>
        /// 명령어가 읽는 필드. 필드 적재가 아니면 null.
        /// </summary>
        /// <remarks>
        /// <c>this.zone.spellCards</c> 같은 사슬에서는 값을 가진 마지막 필드다. 호출, 인자, 지역 변수, 산술
        /// 결과는 런타임에 찾아볼 자리가 없으므로 추측하지 않는다.
        /// </remarks>
        internal static WatchTarget From(Instruction instruction)
        {
            if (instruction == null)
            {
                return null;
            }

            if (instruction.OpCode.Code != Code.Ldfld &&
                instruction.OpCode.Code != Code.Ldsfld &&
                instruction.OpCode.Code != Code.Ldflda &&
                instruction.OpCode.Code != Code.Ldsflda)
            {
                return null;
            }

            return instruction.Operand is FieldReference read
                ? Of(read, instruction.OpCode.Code == Code.Ldsfld || instruction.OpCode.Code == Code.Ldsflda)
                : null;
        }

        /// <summary>호출자가 이미 가진 필드 참조로 대상을 만든다.</summary>
        /// <remarks>
        /// static 여부는 호출자가 정한다. 프로퍼티 setter 경유 쓰기처럼 opcode 가 필드 적재가 아닐 수 있다.
        /// </remarks>
        internal static WatchTarget Of(FieldReference field, bool isStatic)
        {
            if (field == null)
            {
                return null;
            }

            var declaring = field.DeclaringType?.FullName;

            if (string.IsNullOrEmpty(declaring) || string.IsNullOrEmpty(field.Name))
            {
                return null;
            }

            return new WatchTarget
            {
                Declaring = declaring,
                Member = field.Name,
                Property = Behind(field.Name),
                Type = field.FieldType?.FullName,
                Static = isStatic
            };
        }
    }
}
