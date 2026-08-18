using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 살아 있는 값이 무언가를 결정하는 멤버. 되읽을 수 있을 만큼 정확히 이름 붙인 것.
    /// </summary>
    /// <remarks>
    /// 리포트는 무엇이 참이어야 했는지를 말하지만 — <c>MapMove.position == 0</c> — 누군가 지금
    /// <c>position</c> 이 무엇을 쥐고 있는지 볼 수 있기 전까지는 그것으로 테스트를 돌릴 수 없다. 다른
    /// SDK 는 게임더러 그 멤버들을 손으로 표시하라고 청했는데, 그것은 부담을 게임에 지우면서 늘 그렇듯 두
    /// 가지로 틀린다: 아무도 표시하지 않은 필드는 보이지 않고, 전부 표시하면 매 프레임이 변화처럼 보인다.
    ///
    /// 여기서는 표시할 것이 없다. 분석은 모든 조건의 좌변을 만들어낸 명령어를 이미 걸었고, 필드는 바로 그
    /// 피연산자 안에 있다. 그래서 무엇을 감시할지의 목록은 조건을 읽은 부산물이고, 조건이 요구하는 만큼
    /// 정확히 길다 — 샘플 게임에서 실측하니 수천 개짜리 게임에서 그 전체가 멤버 이백 남짓이었다.
    ///
    /// 리포트가 보여 주는 문장이 아니라 부품으로 적는다. <c>MapMove.position</c> 은 사람에게 읽어 줄
    /// 것이고, <c>(WordVenture.Map.MapMove, position)</c> 은 리플렉션이 찾을 수 있는 것이다. 런타임에
    /// 앞의 것을 뒤의 것으로 되돌리는 일은 우리 자신의 산문을 파싱하는 일이 된다.
    ///
    /// 필드만 본다. <c>spellCards.Count</c> 나 <c>CompareTag("Spell")</c> 에 대한 조건은 호출이
    /// 만들어내는 것이고, 알아보려고 그것을 부르는 일은 감시가 아니라 — 게임을 하는 것이다. 그것들은 호출이
    /// 이루어진 대상을 감시해 근사하는 대신 빼 두고 센다.
    /// </remarks>
    internal sealed class WatchTarget
    {
        private const string BackingSuffix = ">k__BackingField";

        /// <summary>그것을 선언하는 타입. 이것이 컴파일될 때 가지고 있던 이름으로.</summary>
        internal string Declaring;

        internal string Member;

        /// <summary>
        /// 컴파일러가 만든 것일 때, 이 필드가 뒤에 서 있는 프로퍼티.
        /// </summary>
        /// <remarks>
        /// 자동 프로퍼티는 <c>&lt;Instance&gt;k__BackingField</c> 라 불리는 필드이고, 리플렉션에 필요한 이름이
        /// 그것이다. 다른 무엇도 그 이름을 쓰지 않는다: 근거는 <c>StageDataSingleton.Instance</c> 라고 하므로,
        /// 멤버 이름에 대한 조건에 판독을 이어 붙이려는 독자는 아무것도 찾지 못한다. 둘 다 쓴다 — 찾아볼 때 쓸
        /// 이름과, 나머지 전부가 그것을 부르는 이름.
        /// </remarks>
        internal string Property;

        /// <summary>어떤 종류의 값이 돌아오는지. 독자가 무엇을 비교하는지 알도록.</summary>
        internal string Type;

        /// <summary>
        /// 그것을 찾아볼 인스턴스가 없을 때 참.
        /// </summary>
        /// <remarks>
        /// 이 차이가 값을 어디에 실을 수 있는지를 정한다. GameObject 를 걷는 스캔은 인스턴스 필드를 놓을 자리는
        /// 있어도 static 필드를 놓을 자리는 없다 — 다른 SDK 가 <c>MapMove.StagePosition</c> 에 대해 아무 답도
        /// 갖지 못하게 된 경위가 그것이고, 샘플 게임의 맵 화면 전체가 그 필드 위에서 돈다.
        /// </remarks>
        internal bool Static;

        /// <summary>이 둘을 같은 하나로 만드는 것.</summary>
        internal string Key => Declaring + "::" + Member;

        /// <summary>
        /// 검사되는 값이 필드 자체가 아닐 때, 필드에서 무엇을 읽었는가.
        /// </summary>
        /// <remarks>
        /// <c>spellCards.Count == 1</c> 은 목록의 크기에 대한 것이고, 목록이 필드다. 필드를 감시하면 답이 된다 —
        /// 판독은 컬렉션을 그 개수로 쓴다 — 다만 양쪽 끝이 그 두 숫자 중 어느 쪽을 비교하는지에 대해 합의할
        /// 때만 그렇다. 타입에서 추론하도록 두지 않고 적어 둔다.
        ///
        /// 필드 자체가 값일 때는 null 이고, 대부분이 그렇다.
        /// </remarks>
        internal string Via;

        /// <summary>
        /// 값이 그 자체로는 어디에도 없을 때, 그 값을 읽어 온 필드.
        /// </summary>
        /// <remarks>
        /// <c>CombineZone.spellCards.Count</c> 에 대한 조건은 호출이 만들어내는 것이라, 곧이곧대로 물으면 볼
        /// 자리가 없고 그 조건은 감시되지 않은 채 남는다 — 그 때문에 명세 네 줄이 제 전제를 확인하지 못했고,
        /// 정작 그 줄들이 말하는 목록은 그동안 내내 필드 안에 앉아 있었다.
        ///
        /// 아무것도 받지 않는 프로퍼티 읽기이면서 그 대상이 필드로 뿌리내릴 때만 받아들인다. 인자를 받는 getter
        /// 는 물음에 따라 답이 달라지는 물음이고, 필드가 아닌 수신자는 찾아볼 자리가 아니다. 무엇을 읽었는지는
        /// <see cref="Via"/> 에 적으므로 필드의 어느 숫자를 뜻했는지 추측할 일이 없다.
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

            // `transform` 과 `gameObject` 은 필드로 가는 길에 밟고 지나가므로, 그중 하나를 읽은 것은 이미 필드
            // 자체가 답한 것이고 다시 말하면 그 객체를 제 자신의 프로퍼티로 서술하게 된다.
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
        /// 명령어가 읽는 필드. 읽지 않으면 null.
        /// </summary>
        /// <remarks>
        /// 건네받는 명령어는 검사되는 값을 만들어낸 것이고, <c>this.zone.spellCards</c> 같은 사슬에서는 그중
        /// 마지막 필드다. 그 마지막이 답이다: 값을 쥐고 있는 것이 그것이고, 그 앞의 것들은 거기 가는 길이다.
        ///
        /// 그 밖의 모든 것에 대해서는 일부러 null 이다. 호출, 인자, 지역 변수, 산술 결과는 전부 리포트가 이름을
        /// 댈 수 있는 값이면서 그중 어느 것도 찾아볼 자리가 아니므로, 하나를 추측하면 아무도 읽을 수 없는 멤버를
        /// 목록에 넣게 된다.
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

        /// <summary>같은 대상을, 호출자가 이미 쥐고 있는 필드에서 이름 붙인 것.</summary>
        /// <remarks>
        /// 효과는 필드에 쓰고 그 일을 하는 명령어가 참조를 그대로 나르므로 걸어갈 것이 없다. 그것이 홀로 서는지는
        /// 호출자가 말할 몫이다 — 필드에 대입하는 프로퍼티 setter 는 그 필드에 대한 쓰기이고, 그 지점의 opcode 가
        /// 말하는 것은 호출에 대한 것이지 그 뒤의 필드에 대한 것이 아니다.
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
