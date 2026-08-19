using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>명령어에서 값과 이름을 되읽어 내는 일.</summary>
    internal static class IlReading
    {
        internal static bool TryConstant(Instruction instruction, out int value)
        {
            value = 0;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1: value = -1; return true;
                case Code.Ldc_I4_0: value = 0; return true;
                case Code.Ldc_I4_1: value = 1; return true;
                case Code.Ldc_I4_2: value = 2; return true;
                case Code.Ldc_I4_3: value = 3; return true;
                case Code.Ldc_I4_4: value = 4; return true;
                case Code.Ldc_I4_5: value = 5; return true;
                case Code.Ldc_I4_6: value = 6; return true;
                case Code.Ldc_I4_7: value = 7; return true;
                case Code.Ldc_I4_8: value = 8; return true;
                case Code.Ldc_I4_S: value = (sbyte)instruction.Operand; return true;
                case Code.Ldc_I4: value = (int)instruction.Operand; return true;
                default: return false;
            }
        }

        /// <summary>
        /// 명령어가 스택에 올려놓는 무엇이든, 그것의 짧은 이름.
        /// </summary>
        /// <remarks>
        /// 이것이 이름 붙일 수 없는 값일 때 null 이다 — 계산된 식, 내력이 추적되지 않는 지역 변수. 호출자는 null 을
        /// 읽지 못한 조건으로 다루고 그럴듯한 것을 지어내는 대신 그렇다고 말한다.
        ///
        /// 경계는 읽고 있는 블록의 첫 명령어이고, 그것을 지나서는 아무것도 읽지 않는다. 경계가 없으면 인자는 아예 읽히지
        /// 않는다: 호출의 인자는 그 앞의 명령어들인데, 제어가 여러 자리에서 도착할 수 있는 곳에서는 그 앞의 명령어들이
        /// 마침 위에 쓰인 경로의 것이기 때문이다.
        /// </remarks>
        internal static string Describe(Instruction instruction)
        {
            return Describe(instruction, null);
        }

        /// <summary>
        /// 호출이 이루어진 객체를, 그것을 쥔 필드까지 따라 내려간 것.
        /// </summary>
        /// <remarks>
        /// <c>MapMove.character.transform.position = MapMove.battle2.transform.position</c> 은 샘플 게임이 맵 커서를
        /// 옮기는 것이고, 그 양쪽 절반 다 끝에 <c>.transform</c> 이 붙은 필드다. 수신자로 읽으면 답이 호출이지만, 한 걸음
        /// 더 읽으면 <c>character</c> 이고 그것은 게임이 도는 동안 값을 되읽을 수 있는 자리다.
        ///
        /// 화면 녹화를 판독 옆에서 함께 보게 된 지금 이것이 더 중요해졌다. 영상은 스프라이트가 어딘가에 도착하는 것을
        /// 보여 주지만 그 스프라이트가 <c>wordHead</c> 라거나 그 어딘가가 <c>battle2</c> 라고는 말할 수 없다. 그것들은
        /// 이름이고, 이름을 대는 일이 서로 무관한 관측 둘을 하나의 사실로 만든다.
        ///
        /// <c>transform</c> 과 <c>gameObject</c> 만 밟고 지나가며, 그 둘만이다. 그 둘은 같은 객체를 다른 모습으로
        /// 답해 주는 접근자라, 그 뒤의 필드가 실제로 움직인 그것이다. 다른 getter 는 전혀 다른 것을 돌려줄 수 있으므로 —
        /// <c>list.First().position</c> 은 <c>list</c> 로 뿌리내릴 텐데 목록에는 위치가 없다 — 걷기를 멈추고 그 값을
        /// 감시하지 않은 채 둔다. 그것이 그럴듯한 답이 아니라 정직한 답이다.
        /// </remarks>
        internal static Instruction Rooted(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within)
        {
            if (method == null || !method.HasThis || call == null || boundary == null)
            {
                return null;
            }

            return RootedAt(Receiving(method, call, boundary), boundary, within);
        }

        /// <summary>같은 걷기를, 호출의 수신자가 아니라 어떤 명령어에서 시작한 것.</summary>
        /// <remarks>
        /// 트윈 라이브러리는 대고 불린 것이 아니라 넘겨받은 transform 을 옮기므로, 뿌리내려야 할 것이 인자다. 같은 걷기,
        /// 다른 출발점 — 그리고 그 둘을 한 걷기로 두는 것이 두 모양이 어느 필드가 움직였는지에 대해 어긋나지 않게 한다.
        /// </remarks>
        internal static Instruction RootedAt(
            Instruction from, Instruction boundary, MethodDefinition within)
        {
            var at = Holding(from, within);

            for (var depth = 0; depth < MaxReceiverDepth && at != null; depth++)
            {
                if (at.OpCode.Code != Code.Call && at.OpCode.Code != Code.Callvirt)
                {
                    return at;
                }

                if (!(at.Operand is MethodReference getter) ||
                    !getter.HasThis || getter.Parameters.Count != 0 ||
                    (getter.Name != "get_transform" && getter.Name != "get_gameObject"))
                {
                    return at;
                }

                at = Holding(Receiving(getter, at, boundary), within);
            }

            return at;
        }

        /// <summary>
        /// <see cref="Describe"/> 가 도착하는 자리: 실제로 값을 쥔 명령어.
        /// </summary>
        /// <remarks>
        /// 한 번만 쓰인 지역 변수를 거쳐 가는 같은 따라가기이고, 그것뿐이다. 값에 이름을 대는 일과 그것을 되읽을 자리를
        /// 찾는 일은 같은 걷기이므로, 런타임에 감시되는 멤버는 그 문장이 말하는 바로 그것이어야 한다 — 디버깅 빌드에서
        /// 복사본 둘을 거쳐 이름 붙은 <c>MapMove.position</c> 은 여전히 그 필드이고, 지역 변수를 겨눈 감시자는 메서드가
        /// 돌아오는 순간 존재하기를 그만두는 무언가를 감시하게 된다.
        ///
        /// <see cref="Describe"/> 에 접어 넣지 않고 그 옆에 두는 것은, 둘이 서로 다른 물음에 답하고 그중 하나만 정중하게
        /// 실패할 수 있기 때문이다. 읽을 수 없는 이름은 읽지 못한 조건이지만, 필드가 아닌 명령어는 그냥 찾아볼 자리가
        /// 아닌 것이고 그것은 평범하고 잦은 답이다.
        /// </remarks>
        internal static Instruction Holding(Instruction instruction, MethodDefinition within)
        {
            for (var depth = 0; depth < MaxReceiverDepth; depth++)
            {
                var stored = StoredOnce(instruction, within);

                if (stored == null)
                {
                    return instruction;
                }

                instruction = stored;
            }

            return instruction;
        }

        internal static string Describe(Instruction instruction, Instruction boundary)
        {
            return Describe(instruction, boundary, null);
        }

        /// <summary>
        /// 같은 이름 붙이기인데, 한 가지만 담을 수 있는 지역 변수를 꿰뚫어 볼 수 있는 것.
        /// </summary>
        /// <remarks>
        /// 최적화하는 컴파일러는 값을 지역 변수에 넣고 되읽는 자리에서 디버깅용 컴파일러는 그것을 다시 가져오므로, 같은
        /// 소스가 에디터 스캔에서는 조건 스물을 이름 없이 남기고 개발 빌드에서는 읽히게 했다. 지역 변수를 따라가는 일은
        /// 일반적으로는 거절한다 — 보이지 않는 데서 대입됐을 수 있다 — 그리고 메서드가 그것을 정확히 한 자리에서 저장할
        /// 때만 허용한다. 그러면 다른 어디서도 올 수 없기 때문이다.
        /// </remarks>
        internal static string Describe(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            return Describe(instruction, boundary, within, 0);
        }

        private static string Describe(
            Instruction instruction, Instruction boundary, MethodDefinition within, int depth)
        {
            var stored = StoredOnce(instruction, within);

            if (stored != null)
            {
                // 하나에서 멈추지 않고 계속 따라간다. 디버깅용 컴파일러는 switch 의 주어를 검사하기 전에 지역 변수 둘을 거쳐
                // 복사하는데 — `ldarg.1; stloc.1; ldloc.1; stloc.0` — 첫 번째에서 멈추는 바람에 샘플 게임의 맵 화면 다섯과 단어
                // 위치 다섯이 아무도 이름 댈 수 없는 것에 대한 switch 로 남았다. 각 걸음은 여전히 제 메서드가 정확히 한 번 쓰는
                // 지역 변수이고 그것이 안전의 전부이며 두 번 적용한다고 약해지지 않는다. 깊이는 제 자신에서 대입된 필드가 원을
                // 도는 것을 막는 몫이다.
                return Describe(stored, boundary, depth + 1 < MaxReceiverDepth ? within : null, depth + 1);
            }

            if (instruction == null)
            {
                return null;
            }

            if (TryConstant(instruction, out var number))
            {
                return number.ToString();
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldstr:
                    return "\"" + instruction.Operand + "\"";

                case Code.Ldnull:
                    return "null";

                case Code.Ldfld:
                case Code.Ldsfld:
                {
                    var field = instruction.Operand as FieldReference;

                    return WhichScene(field, within, boundary, depth) ?? FieldName(field);
                }

                case Code.Ldc_I8:
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                    return Convert.ToString(
                        instruction.Operand, System.Globalization.CultureInfo.InvariantCulture);

                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldarg:
                case Code.Ldarg_S:
                    return ArgumentName(instruction, within);

                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca:
                case Code.Ldloca_S:
                    return LocalName(instruction, within);

                case Code.Call:
                case Code.Callvirt:
                    return CallName(
                        instruction.Operand as MethodReference, instruction, boundary, within, depth);

                default:
                    return Arithmetic(instruction, boundary, 0, within);
            }
        }

        /// <summary>
        /// 메서드가 대고 불린 객체, 또는 매개변수가 선언될 때 받은 이름.
        /// </summary>
        /// <remarks>
        /// 지금까지 읽지 않았고, 그 값은 서로 다른 둘이었다. 매개변수를 비교하는 조건은 좌변이 아예 없어 읽지 못한 것으로
        /// 보고됐는데, 그것은 아무도 적을 수 없는 규칙이다. 그리고 <c>Destroy(this)</c> 는 아무도 이름 댈 수 없는 대상으로
        /// 나왔다 — 정작 그것을 알아보려는 싱글턴 배관 인식은 내내 <c>this</c> 라는 말을 찾고 있었으므로, 제 두 번째
        /// 복사본을 파괴하는 <c>Awake</c> 가 기능으로 도착하고 있었다.
        ///
        /// 이름은 어셈블리 자신의 메타데이터에 있는 것이므로, 난독화된 빌드는 그것이 지킨 것을 돌려주고 — 아무것도 지키지
        /// 않았다면 추측이 아니라 아무것도 돌려주지 않는다.
        ///
        /// 매개변수의 이름을 대는 일은 그 비교가 무엇에 대한 것인지를 말하지, 누가 그것을 마련할 수 있는지를 말하지
        /// 않는다. 그것은 주어의 몫이고, <see cref="Where"/> 가 늘 그랬듯 <c>arg:N</c> 으로 답한다.
        /// </remarks>
        private static string ArgumentName(Instruction instruction, MethodDefinition within)
        {
            if (instruction.Operand is ParameterDefinition declared)
            {
                return string.IsNullOrEmpty(declared.Name) ? null : declared.Name;
            }

            if (within == null)
            {
                return null;
            }

            int index;

            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg_0: index = 0; break;
                case Code.Ldarg_1: index = 1; break;
                case Code.Ldarg_2: index = 2; break;
                case Code.Ldarg_3: index = 3; break;
                default: return null;
            }

            if (within.HasThis)
            {
                if (index == 0)
                {
                    return "this";
                }

                index--;
            }

            if (index >= within.Parameters.Count)
            {
                return null;
            }

            var parameter = within.Parameters[index];

            return string.IsNullOrEmpty(parameter.Name) ? null : parameter.Name;
        }

        /// <summary>
        /// 어셈블리가 아직 그것을 나를 때, 소스가 지역 변수에 준 이름.
        /// </summary>
        /// <remarks>
        /// 이름을 대는 것이지 따라가는 것이 아니다. 지역 변수를 따라가는 일은 이것이 일반적으로 거절하고 메서드가 한 번
        /// 쓸 때만 허용하는 것이다. 이름을 대는 일은 다른 물음이고, 그 답은 알아내는 것이 아니라 심볼에 적혀 있다.
        ///
        /// 이것을 가질 값이 있게 만든 것은 <c>for</c> 루프의 카운터다. 루프 자신의 검사는 <c>i &lt; cards.Count</c> 이고
        /// <c>i</c> 는 두 번 쓰인다 — 한 번은 0 으로, 한 번은 제 자신 더하기 1 로 — 그래서 정확히 한 번 저장 규칙이
        /// 거절하는 모양이다. 그 검사는 읽지 못한 조건으로 나왔고 기록 전체를 함께 끌고 갔다: 샘플 게임의 기록 열셋이
        /// 루프가 관련됐다는 것 말고는 아무 말도 하지 않았다.
        ///
        /// 지역 변수는 그것이 누구의 것인지 아무 말도 하지 않으므로 주어는 여전히 잃고 리포트도 여전히 그렇다고 말한다.
        /// 얻는 것은 문장이다: "지나온 개수가 카드 개수보다 작다" 는 누군가 읽을 수 있는 규칙이지만 "읽지 못한 조건" 은
        /// 아니다.
        ///
        /// 심볼이 없으면 아무것도 주장하지 않는다. 릴리스 빌드는 아예 굽지 않고 난독화된 것은 그것이 지킨 것을 돌려주는데
        /// 그것은 아무것도 아닐 수 있다 — 그러면 이것도 아무것도 아니라고 말한다. 컴파일러가 스스로 지어낸 이름은
        /// 건드리지 않는다.
        /// </remarks>
        private static string LocalName(Instruction instruction, MethodDefinition within)
        {
            if (within == null || !within.HasBody || !IsLoadingLocal(instruction, out var slot))
            {
                return null;
            }

            var variables = within.Body.Variables;

            if (slot >= variables.Count || within.DebugInformation == null)
            {
                return null;
            }

            if (!within.DebugInformation.TryGetName(variables[slot], out var name) ||
                string.IsNullOrEmpty(name) || name.StartsWith("<", StringComparison.Ordinal))
            {
                return null;
            }

            return name;
        }

        /// <summary>
        /// 코드가 계산해 낸 값을, 소스가 쓴 방식으로 적은 것.
        /// </summary>
        /// <remarks>
        /// 게임 루프 안의 조건은 저장된 것이 아니라 계산된 것을 비교한다: 슬라이드는 시작한 뒤 이동한 거리를 그 길이로 나눈
        /// 값이 1 에 닿을 때 끝난다. 그 합에 이름 대기를 거절한 탓에 그것들 하나하나가 읽지 못한 조건으로 남았고 —
        /// Trash Dash 에서 367 건 — 읽지 못한 조건은 아무도 적을 수 없는 규칙이다.
        ///
        /// 그 순간 스택 위에 있는 것만 본다. 코드가 지역 변수에 넣고 되읽은 값은 따라가지 않는다. 지역 변수는 이것이 볼 수
        /// 없는 데서 대입됐을 수 있고, 엉뚱한 대입에 묶인 조건은 값비싼 종류의 틀림이기 때문이다. 그것은 별개의 일이고
        /// 여기서 하지 않는다.
        ///
        /// 블록뿐 아니라 깊이로도 가둔다. 긴 식은 긴 문장을 만들고, 몇 단계를 지나면 그 문장은 누구도 읽는 것이 아니게 된다.
        /// </remarks>
        private static string Arithmetic(
            Instruction instruction, Instruction boundary, int depth, MethodDefinition within)
        {
            if (depth >= MaxArithmeticDepth || boundary == null)
            {
                return null;
            }

            var symbol = Operator(instruction.OpCode.Code);

            if (symbol == null)
            {
                return Negation(instruction, boundary, depth, within);
            }

            var rightAt = Preceding(instruction, boundary);
            var leftAt = Under(rightAt, boundary);

            var right = Read(rightAt, boundary, depth + 1, within);
            var left = Read(leftAt, boundary, depth + 1, within);

            return left == null || right == null ? null : "(" + left + " " + symbol + " " + right + ")";
        }

        /// <summary>단항 연산. 피연산자가 둘이 아니라 하나다.</summary>
        private static string Negation(
            Instruction instruction, Instruction boundary, int depth, MethodDefinition within)
        {
            if (instruction.OpCode.Code != Code.Neg)
            {
                return null;
            }

            var value = Read(Preceding(instruction, boundary), boundary, depth + 1, within);
            return value == null ? null : "-" + value;
        }

        /// <summary>피연산자에 이름을 대고, 그것이 합이면 한 단계 더 들어간다.</summary>
        private static string Read(
            Instruction instruction, Instruction boundary, int depth, MethodDefinition within)
        {
            if (instruction == null)
            {
                return null;
            }

            return Operator(instruction.OpCode.Code) != null || instruction.OpCode.Code == Code.Neg
                ? Arithmetic(instruction, boundary, depth, within)
                : Describe(instruction, boundary, within);
        }

        private static string Operator(Code code)
        {
            switch (code)
            {
                case Code.Add: case Code.Add_Ovf: case Code.Add_Ovf_Un: return "+";
                case Code.Sub: case Code.Sub_Ovf: case Code.Sub_Ovf_Un: return "-";
                case Code.Mul: case Code.Mul_Ovf: case Code.Mul_Ovf_Un: return "*";
                case Code.Div: case Code.Div_Un: return "/";
                case Code.Rem: case Code.Rem_Un: return "%";
                default: return null;
            }
        }

        /// <summary>계산된 값을 몇 단계까지 써 나가는지.</summary>
        private const int MaxArithmeticDepth = 4;

        /// <summary>
        /// 호출이 준 답에, 읽을 수 있는 인자를 곁들여 이름을 댄다.
        /// </summary>
        /// <remarks>
        /// 실제 코드의 조건은 필드를 검사하는 만큼이나 자주 메서드가 무엇을 돌려줬는지를 검사한다 — 세이브가 있는지,
        /// 목록이 비었는지. 그것들에 이름 대기를 거절한 탓에 그것들이 지키는 분기가 읽지 못한 조건으로 보고됐고, 샘플
        /// 게임에서 그것은 어느 씬이 로드되는지를 결정하는 사실이 빠진 바로 그 하나였다는 뜻이다.
        ///
        /// 인자는 조건마다 시그니처를 다는 값이 그것이 말하는 것보다 크다는 이유로 빼 왔다. 그것은 실측으로 답해졌다:
        /// <c>Component.CompareTag()</c> 는 샘플 게임에서 조건 105 건이고 그 하나하나가 똑같이 읽혀서, 태그에 기반한
        /// 전투 규칙의 절반이 반복되는 한 문장으로 도착했다. 원하는 것은 시그니처가 아니라 인자이고 — 읽을 수 없는 인자는
        /// <c>_</c> 로 쓰므로 조건이 모르는 것을 안다고 주장하는 일은 없다.
        ///
        /// 호출이 대고 이루어진 것에 대해 쓰고, 그것을 읽을 수 없을 때만 선언 타입에 대해 쓴다. 타입은 그 타입의 모든
        /// 객체에 대해 같으므로 한 객체 위의 목록 둘이 모두 <c>List`1.Count</c> 였고 독자는 어느 쪽인지 알 방법이 없었다 —
        /// 샘플 게임의 합치기 버튼은 주문 카드 하나와 원소 카드 하나를 필요로 하는데 그 두 조건이 한 문장으로 도착했다.
        /// 수신자는 없던 적이 없다. 그저 아무도 청하지 않았을 뿐이고, <see cref="Receiver"/> 는 호출 엣지에서 내내 그것을
        /// 청해 왔다.
        ///
        /// 수신자 자체도 수신자를 가진 값이므로 이것은 걷는다. 가둔다. 이름은 읽으라고 있는 것이고 몇 고리를 지나면 그것은
        /// 이름이기를 그만두기 때문이다.
        /// </remarks>
        private static string CallName(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within,
            int depth)
        {
            if (method == null || method.ReturnType.MetadataType == MetadataType.Void)
            {
                return null;
            }

            var owner = Owner(method, call, boundary, within, depth) ?? method.DeclaringType?.Name;

            if (owner == null)
            {
                return null;
            }

            var arguments = Arguments(method, call, boundary);

            if (method.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                var property = owner + "." + method.Name.Substring(4);

                // 인덱서는 매개변수가 있는 getter 이고, 소스에서도 그렇게 읽힌다. 컴파일러가 지어낸 이름이 명세에 들어가지
                // 않도록 get_Item 이 아니라 대괄호로 쓴다.
                return method.Parameters.Count == 0
                    ? property
                    : property + "[" + (arguments ?? Unread(method.Parameters.Count)) + "]";
            }

            return owner + "." + method.Name + "(" + (arguments ?? "") + ")";
        }

        /// <summary>
        /// 호출이 대고 이루어진 것의 이름, 또는 이름을 붙일 값이 없을 때 null.
        /// </summary>
        /// <remarks>
        /// static 호출은 수신자가 없고, <c>this</c> 에 대고 이루어진 것은 일부러 선언 타입에 맡긴다: <c>this</c> 의 필드는
        /// 이미 그렇게 쓰이고 (<c>this.spellCards</c> 가 아니라 <c>CombineZone.spellCards</c>) 주어는 조건 자신의
        /// <c>context</c> 가 나른다. 그래서 평범한 경우에는 아무것도 움직이지 않고, 움직이는 것은 정확히 두 객체가 한
        /// 이름을 나눠 쓰고 있던 경우다.
        /// </remarks>
        private static string Owner(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within,
            int depth)
        {
            if (!method.HasThis || depth >= MaxReceiverDepth)
            {
                return null;
            }

            return Describe(Receiving(method, call, boundary), boundary, within, depth + 1);
        }

        /// <summary>이름이 읽을 수 있기를 그만두기 전까지 수신자를 몇 겹까지 써 나가는지.</summary>
        private const int MaxReceiverDepth = 3;

        /// <summary>
        /// 호출이 받은 인자들. 스택을 따라갈 수 있는 데까지.
        /// </summary>
        /// <remarks>
        /// 마지막 인자에서 거슬러 읽는다. 아무 분석 없이도 확실한 위치가 마지막 하나뿐이기 때문이다: 호출 앞 명령어가
        /// 스택에 남긴 것이 무엇이든 그것이 마지막 인자다. 한 걸음 더 거슬러 갈 때마다 방금 읽은 인자가 소비한 것을 건너뛰어야
        /// 하고, 그 일을 <see cref="Under"/> 가 하며, 스택에 대한 효과가 알려지지 않은 명령어를 만나는 순간 멈춘다.
        ///
        /// 아무것도 읽지 못했을 때는 null 이다. 인자를 읽을 수 없는 호출이 빈 인자 목록이 아니라 늘 그랬던 대로 읽히도록.
        /// </remarks>
        internal static string Arguments(MethodReference method, Instruction call, Instruction boundary)
        {
            var names = ArgumentsRead(method, call, boundary, null);

            if (names == null)
            {
                return null;
            }

            for (var index = 0; index < names.Length; index++)
            {
                if (names[index] == null)
                {
                    names[index] = "_";
                }
            }

            return string.Join(", ", names);
        }

        /// <summary>위치로 지목한 인자 하나, 또는 그것을 읽을 수 없으면 null.</summary>
        /// <remarks>
        /// 확장 메서드에서는 작용 대상 객체가 수신자가 아니라 인자 0 이므로, 호출이 무엇을 바꿨는지 이름 대려면 위치 하나를
        /// 청해야 한다. 그 위치로 곧장 걷는 대신 목록 전체를 읽고 그중 하나를 취한다: 걷기는 어차피 그 뒤의 모든 인자를
        /// 지나가야 하고, 그 일을 두 번 하면 둘이 어긋날 여지를 부른다.
        /// </remarks>
        internal static string ArgumentAt(
            MethodReference method, Instruction call, Instruction boundary, int index)
        {
            return ArgumentAt(method, call, boundary, index, null);
        }

        internal static string ArgumentAt(
            MethodReference method, Instruction call, Instruction boundary, int index,
            MethodDefinition within)
        {
            var names = ArgumentsRead(method, call, boundary, within);

            return names != null && index >= 0 && index < names.Length ? names[index] : null;
        }

        /// <summary>호출의 인자 하나를 만들어낸 명령어.</summary>
        internal static Instruction ArgumentFrom(
            MethodReference method, Instruction call, Instruction boundary, int index)
        {
            var count = method?.Parameters.Count ?? 0;

            if (count == 0 || call == null || boundary == null || index < 0 || index >= count)
            {
                return null;
            }

            var at = Preceding(call, boundary);

            for (var slot = count - 1; slot > index && at != null; slot--)
            {
                at = Under(at, boundary);
            }

            return at;
        }

        /// <summary>
        /// 지역 변수가 여러 번 쓰일 때, 그것에 쓰인 모든 값.
        /// </summary>
        /// <remarks>
        /// 한 번 쓰인 지역 변수는 그 값이고 그것으로 읽힌다 (<see cref="StoredOnce"/> 참고). 다섯 번 쓰이면 그중 어느
        /// 것도 아니고, 지금까지 리포트는 아무 말도 하지 않는 방식으로 그렇다고 말했다 — 샘플 게임은 switch 팔 다섯에서
        /// 주문 프리팹을 고르고 그것들이 합쳐진 뒤에 인스턴스화하므로, 만들어진 것이 `(not a simple target)` 으로 나왔고
        /// 나중에 지역 변수에 이름이 생긴 뒤에는 `prefabToInstantiate` 로 나왔다. 둘 다 정직하고 둘 다 답이 아니다.
        ///
        /// 이름 다섯은 다른 종류의 답이다: 어느 것인가가 아니라 어느 다섯인가. 독자는 시전된 주문이 이것들 중 하나라고 말하고
        /// 찾으러 갈 수 있는데, 전에는 게임이 변수에 붙인 말 한 마디를 쥐고 있었다.
        ///
        /// 전부 아니면 전무다. 저장 중 하나라도 이름 댈 수 없으면 집합을 아예 돌려주지 않는다 — 멤버가 빠진 목록은 완전한
        /// 것처럼 읽히고, 독자는 실제로 거기 있는 값을 배제해 버린다. 그것이 이것이 일으키는 것이 아니라 막으려는 실패다.
        ///
        /// 가둔다. 스무 자리에서 대입되는 지역 변수는 그중에서 고르는 것이 아니라 거기에 누적되는 것이고, 이름 스물을
        /// 나열해 봐야 무엇이 만들어졌는지에 대해 아무 말도 하지 않기 때문이다.
        /// </remarks>
        internal static List<string> Candidates(
            Instruction instruction, Instruction boundary, MethodDefinition within, int most)
        {
            if (within == null || !within.HasBody || !IsLoadingLocal(instruction, out var slot))
            {
                return null;
            }

            var stores = new List<Instruction>();

            foreach (var candidate in within.Body.Instructions)
            {
                if (IsStoringLocal(candidate, out var stored) && stored == slot)
                {
                    stores.Add(candidate);
                }
            }

            if (stores.Count < 2 || stores.Count > most)
            {
                return null;
            }

            var named = new List<string>();

            foreach (var store in stores)
            {
                var value = Describe(store.Previous, boundary, null, 0);

                if (value == null)
                {
                    return null;
                }

                if (!named.Contains(value))
                {
                    named.Add(value);
                }
            }

            return named;
        }

        private static string[] ArgumentsRead(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within)
        {
            var count = method?.Parameters.Count ?? 0;

            if (count == 0 || call == null || boundary == null)
            {
                return null;
            }

            var names = new string[count];
            var read = false;
            var at = Preceding(call, boundary);

            for (var index = count - 1; index >= 0 && at != null; index--)
            {
                names[index] = Argument(at, method.Parameters[index].ParameterType, boundary, within);
                read |= names[index] != null;
                at = Under(at, boundary);
            }

            return read ? names : null;
        }

        /// <summary>
        /// 호출이 대고 이루어진 것.
        /// </summary>
        /// <remarks>
        /// 수신자는 모든 인자 아래에 앉아 있으므로 거기 닿으려면 그것들을 차례로 건너뛰어야 한다. 그것은 호출 엣지에서 그
        /// 호출이 어느 객체에 대한 것이었는지를 말하는 절반이다 — 서로 다른 두 채널 필드에 대고 <c>Raise</c> 를 부르는
        /// 버튼 둘은 서로 다른 두 배선인데, 이것 없이는 같은 줄이었다.
        /// </remarks>
        internal static string Receiver(MethodReference method, Instruction call, Instruction boundary)
        {
            return Receiver(method, call, boundary, null);
        }

        internal static string Receiver(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within)
        {
            if (method == null || !method.HasThis || call == null || boundary == null)
            {
                return null;
            }

            return Describe(Receiving(method, call, boundary), boundary, within);
        }

        /// <summary>호출의 수신자가 어디서 왔는가. 호출자 자신의 용어로.</summary>
        internal static string ReceiverWhere(
            MethodReference method, Instruction call, Instruction boundary, bool hasThis)
        {
            if (method == null || !method.HasThis || call == null || boundary == null)
            {
                return null;
            }

            return Where(Receiving(method, call, boundary), boundary, hasThis);
        }

        /// <summary>
        /// 이 값이 누구의 것인가 — 이름이 대고 쓰인 객체.
        /// </summary>
        /// <remarks>
        /// <c>count &gt; 0</c> 은 누구의 <c>count</c> 인지 말하기 전까지 사실이 아니다. 피호출자의 조건을 호출자의 것 옆에
        /// 놓을 수 없는 이유가 그것이다: 호출자의 용어 옆에서 읽히면 그것은 호출자의 객체에 대한 주장이 되고, 틀린 선행
        /// 조건은 없는 것보다 나쁘다.
        ///
        /// 식이 궁극적으로 무엇에서 읽혔는지까지 걸어 내려가 찾는다. <c>this</c> 의 필드의 필드는 여전히 <c>this</c> 에 대한
        /// 것이고, 인자의 필드는 거기 넘어간 무엇에 대한 것이다. 걷기가 따라갈 수 없는 것은 그렇다고 말하고, 아무것도
        /// 어쩌면 위에서 합성되지 않는다.
        /// </remarks>
        internal static string Where(Instruction instruction, Instruction boundary, bool hasThis)
        {
            return Where(instruction, boundary, hasThis, out _);
        }

        internal static string Where(
            Instruction instruction, Instruction boundary, bool hasThis, out Instruction stoppedAt)
        {
            return Where(instruction, boundary, hasThis, null, out stoppedAt);
        }

        /// <summary>
        /// 같은 걷기인데, 어디서 포기했는지를 말하는 것.
        /// </summary>
        /// <remarks>
        /// 조건이 출발한 피연산자는 주어를 잃은 자리가 아니다 — 호출은 제 이름을 대고, 걷기를 좌절시킨 것은 그 수신자
        /// 어딘가 아래에 있다. 출발점을 세는 것은 호출이 관련됐다는 것만 알려 주고 그 이상은 알려 주지 않았다.
        /// </remarks>
        internal static string Where(
            Instruction instruction,
            Instruction boundary,
            bool hasThis,
            MethodDefinition within,
            out Instruction stoppedAt)
        {
            stoppedAt = instruction;

            for (var step = 0; step < 32 && instruction != null; step++)
            {
                stoppedAt = instruction;

                // 한 자리에서 쓰인 지역 변수는 거기 쓰인 값이고, 그 값이 누구의 것인지는 한 걸음 더 거슬러 간 같은 물음이다.
                // 이름 대기는 이미 그런 지역 변수를 꿰뚫어 보았지만 주어는 그러지 못했고, 그래서 디버그 빌드가
                // `MapMove.StagePosition` 이라고 말하면서 같은 숨에 그것이 누구의 것인지는 말하기를 거절할 수 있었다.
                //
                // 한 번만 따라간다. 나머지를 `within` 없이 물으면 다른 지역 변수를 거쳐 이름 붙은 지역 변수가 사슬을 이루지 못하게
                // 되는데, 거기가 한 번 저장이 안전의 전부이기를 그만두는 지점이다.
                var stored = StoredOnce(instruction, within);

                if (stored != null)
                {
                    instruction = stored;
                    within = null;
                    continue;
                }

                switch (instruction.OpCode.Code)
                {
                    case Code.Ldarg_0:
                        return hasThis ? "this" : "arg:0";

                    case Code.Ldarg_1: return hasThis ? "arg:0" : "arg:1";
                    case Code.Ldarg_2: return hasThis ? "arg:1" : "arg:2";
                    case Code.Ldarg_3: return hasThis ? "arg:2" : "arg:3";

                    case Code.Ldarg:
                    case Code.Ldarg_S:
                    {
                        var parameter = instruction.Operand as ParameterDefinition;
                        return parameter == null ? null : "arg:" + parameter.Index;
                    }

                    case Code.Ldsfld:
                    case Code.Ldstr:
                    case Code.Ldnull:

                    // 작은 정수 opcode 로 담기에 너무 넓은 수도 여전히 수이고, 필드를 -10 과 비교하는 조건은 그 필드에 대한 것이다.
                    // 이것을 빼놓으면 그 리터럴이 아무 객체의 이름도 대지 않고, 아무것도 그것과 일치하지 않으며, 비교 전체가 주어를
                    // 잃었다 — `Vector3.x < -10` 이 -10 을 읽지 못한 탓에 쓸모없었다.
                    case Code.Ldc_I8:
                    case Code.Ldc_R4:
                    case Code.Ldc_R8:
                        return "static";

                    default:
                        if (TryConstant(instruction, out _))
                        {
                            return "static";
                        }

                        // 합은 그 양쪽이 대한 그것에 대한 것이다. 이것이 없으면, 전에는 읽을 수 없던 식을 읽어낸 결과가 조건을 아무도 읽지
                        // 못하던 때보다 *덜* 합성 가능하게 만들었다 — atom 이 "아무 객체의 이름도 대지 않는다" 에서 "아무도 알아내지 못한
                        // 객체의 이름을 댄다" 로 옮겨 갔다.
                        if (Operator(instruction.OpCode.Code) != null)
                        {
                            var rightSide = Preceding(instruction, boundary);

                            return Agreeing(
                                Where(Under(rightSide, boundary), boundary, hasThis),
                                Where(rightSide, boundary, hasThis));
                        }

                        if (instruction.OpCode.Code == Code.Neg)
                        {
                            instruction = Preceding(instruction, boundary);
                            continue;
                        }

                        // 이것이 무엇에서 읽혔는지까지 내려간다. 입력이 하나면 그것이 읽혀 온 그것이고, 하나보다 많거나 따라갈 수 있는
                        // 것이 없으면 걷기가 끝난다.
                        if (Consumes(instruction) != 1)
                        {
                            var call = instruction.Operand as MethodReference;

                            if ((instruction.OpCode.Code == Code.Call ||
                                 instruction.OpCode.Code == Code.Callvirt) && call != null)
                            {
                                if (!call.HasThis)
                                {
                                    return "static";
                                }

                                instruction = Receiving(call, instruction, boundary);
                                continue;
                            }

                            return null;
                        }

                        instruction = Preceding(instruction, boundary);
                        continue;
                }
            }

            return null;
        }

        /// <summary>
        /// 양쪽이 함께 대한 하나의 객체, 또는 그런 것이 없을 때 null.
        /// </summary>
        /// <remarks>
        /// 상수만으로 된 쪽은 무엇과도 일치하는데, 그것이 평범한 모양이다 — <c>this</c> 의 필드를 숫자로 나눈 것.
        /// </remarks>
        internal static string Agreeing(string left, string right)
        {
            if (left == null || right == null)
            {
                return null;
            }

            if (left == "static") return right;
            if (right == "static") return left;

            return left == right ? left : null;
        }

        /// <summary>호출의 수신자를 만들어낸 명령어.</summary>
        private static Instruction Receiving(MethodReference method, Instruction call, Instruction boundary)
        {
            var at = Preceding(call, boundary);

            for (var index = 0; index < method.Parameters.Count && at != null; index++)
            {
                at = Under(at, boundary);
            }

            return at;
        }

        /// <summary>읽을 수 없었던 인자마다 놓는 자리.</summary>
        private static string Unread(int count)
        {
            var places = new string[count];

            for (var index = 0; index < count; index++)
            {
                places[index] = "_";
            }

            return string.Join(", ", places);
        }

        /// <summary>
        /// 인자 하나. 알 수 있는 자리에서는 소스가 썼을 방식으로 이름 붙인 것.
        /// </summary>
        /// <remarks>
        /// 플래그와 열거형은 둘 다 숫자로 도착하고, 숫자만으로는 읽을 수 없다 — <c>SetActive(0)</c> 과 <c>Play(4)</c> 는
        /// 아무 말도 하지 않는다. 그것들을 <c>false</c> 와 멤버의 이름으로 되돌리는 것이 매개변수 자신의 타입이다.
        /// </remarks>
        private static string Argument(
            Instruction instruction, TypeReference parameter, Instruction boundary,
            MethodDefinition within)
        {
            if (instruction == null)
            {
                return null;
            }

            if (TryConstant(instruction, out var number))
            {
                if (parameter?.MetadataType == MetadataType.Boolean)
                {
                    return number == 0 ? "false" : "true";
                }

                // 값 타입일 때만 해석한다. int 인자가 조건마다 타입 로드 값을 치르지 않도록. 시그니처에서 열거형은 값 타입이다.
                return parameter?.MetadataType == MetadataType.ValueType
                    ? EnumName(parameter, number)
                    : number.ToString();
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I8:
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                    return Convert.ToString(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture);

                case Code.Box:
                    // Equals 를 거쳐 비교되거나 object 로 넘어간 열거형은 박싱돼 도착하고, 그 아래의 숫자는 박스가 이름 대는 타입
                    // 없이는 아무 뜻도 없다.
                    return Argument(
                        Preceding(instruction, boundary), instruction.Operand as TypeReference, boundary,
                        within);

                default:
                    return Describe(instruction, boundary, within);
            }
        }

        /// <summary>
        /// 앞 명령어. 디버그 빌드의 채움을 밟고 지나가되, 읽고 있는 블록의 시작을 결코 넘지 않는다.
        /// </summary>
        /// <remarks>
        /// 거슬러 읽기를 건전하게 만드는 것이 그 경계다. 블록은 제어가 여러 자리에서 도착할 수 있는 데서 시작하므로, 블록의
        /// 첫 명령어 앞의 명령어는 그 값이 만들어졌을 수 있는 갈래 중 하나일 뿐이다. 그 경계를 넘어가면 마침 위에 쓰인
        /// 경로의 꼬리를 읽게 되고, 단락된 <c>&amp;&amp;</c> 는 거기에 리터럴 <c>0</c> 을 놓는다 — 이것의 첫 시도는 맵의
        /// 해금 규칙을 <c>0 != 0</c> 으로 보고했는데, 그것은 아무것도 보고하지 않는 것보다 나쁘다.
        /// </remarks>
        internal static Instruction Preceding(Instruction instruction, Instruction boundary)
        {
            if (instruction == null || instruction == boundary)
            {
                return null;
            }

            var previous = instruction.Previous;

            // 접두 명령 — constrained., volatile., readonly. — 은 제 명령어로 쓰이면서 스택은 건드리지 않는데, 거기서 멈춘
            // 탓에 값 타입에 대고 불린 메서드의 모든 인자가 가려졌다. Enum.Equals 가 흔한 경우이고 그것은 비교다.
            while (previous != null &&
                   (previous.OpCode.Code == Code.Nop || previous.OpCode.OpCodeType == OpCodeType.Prefix))
            {
                if (previous == boundary)
                {
                    return null;
                }

                previous = previous.Previous;
            }

            return previous;
        }

        /// <summary>
        /// 어떤 명령어가 만든 값 아래에 앉은 값을 만들어낸 것.
        /// </summary>
        /// <remarks>
        /// 명령어 하나를 되짚는 일이 스택 슬롯 하나를 되짚는 것과 같은 것은 아무것도 소비하지 않는 명령어에 대해서뿐이다.
        /// <c>ldfld</c> 는 객체 참조를 먹고 <c>op_Equality</c> 는 인자 둘을 먹으며, 그것을 무시하는 독자는 아무 이름도 대지
        /// 않는 것이 아니라 엉뚱한 피연산자의 이름을 댄다 — <c>a == b.Count</c> 가 <c>b == b.Count</c> 로 읽힌다.
        ///
        /// 그래서 각 입력을 같은 규칙으로 재귀적으로 건너뛰고, 스택에 대한 효과가 아래 표에 없는 것은 걷기를 멈춘다. 거기서
        /// 거절하는 것이 요점이다: 호출자는 그 조건을 읽지 못한 것으로 보고하고, 그것이 정직한 답이다.
        /// </remarks>
        internal static Instruction Under(Instruction instruction, Instruction boundary)
        {
            var eaten = Consumes(instruction);

            if (eaten < 0)
            {
                return null;
            }

            var cursor = Preceding(instruction, boundary);

            for (var index = 0; index < eaten && cursor != null; index++)
            {
                cursor = Under(cursor, boundary);
            }

            return cursor;
        }

        /// <summary>명령어가 스택 슬롯을 몇 개 먹는지, 또는 여기서 알 수 없을 때 -1.</summary>
        private static int Consumes(Instruction instruction)
        {
            if (instruction == null)
            {
                return -1;
            }

            if (TryConstant(instruction, out _))
            {
                return 0;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldstr:
                case Code.Ldnull:
                case Code.Ldc_I8:
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldarg:
                case Code.Ldarg_S:
                case Code.Ldarga:
                case Code.Ldarga_S:
                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca:
                case Code.Ldloca_S:
                case Code.Ldsfld:
                case Code.Ldsflda:
                case Code.Ldtoken:
                case Code.Ldftn:
                case Code.Sizeof:
                    return 0;

                case Code.Ldfld:
                case Code.Ldflda:
                case Code.Ldlen:
                case Code.Ldobj:
                case Code.Ldvirtftn:
                case Code.Newarr:
                case Code.Box:
                case Code.Unbox:
                case Code.Unbox_Any:
                case Code.Castclass:
                case Code.Isinst:
                case Code.Neg:
                case Code.Not:
                    return 1;

                case Code.Add:
                case Code.Sub:
                case Code.Mul:
                case Code.Div:
                case Code.Rem:
                case Code.And:
                case Code.Or:
                case Code.Xor:
                case Code.Shl:
                case Code.Shr:
                case Code.Shr_Un:
                case Code.Ceq:
                case Code.Clt:
                case Code.Clt_Un:
                case Code.Cgt:
                case Code.Cgt_Un:
                    return 2;

                case Code.Call:
                case Code.Callvirt:
                    return instruction.Operand is MethodReference called
                        ? called.Parameters.Count + (called.HasThis ? 1 : 0)
                        : -1;

                case Code.Newobj:
                    return instruction.Operand is MethodReference constructor
                        ? constructor.Parameters.Count
                        : -1;

                default:
                    return ByName(instruction.OpCode.Name);
            }
        }

        /// <summary>
        /// 하나하나 나열하기에는 너무 긴 계열들.
        /// </summary>
        /// <remarks>
        /// <c>conv.*</c> 는 받은 값을 갈아치우고, <c>ldind.*</c> 는 주소를 그 자리에 있는 것으로 갈아치우며,
        /// <c>ldelem.*</c> 는 배열과 인덱스를 먹는다. 그 밖의 것은 unknown 이고, unknown 은 걷기를 멈춘다.
        /// </remarks>
        private static int ByName(string opcode)
        {
            if (opcode == null)
            {
                return -1;
            }

            if (opcode.StartsWith("conv.", StringComparison.Ordinal) ||
                opcode.StartsWith("ldind.", StringComparison.Ordinal))
            {
                return 1;
            }

            return opcode.StartsWith("ldelem", StringComparison.Ordinal) ? 2 : -1;
        }

        private const string BackingSuffix = ">k__BackingField";

        /// <summary>
        /// 필드에 이름을 대거나, 그것이 컴파일러 자신의 장부일 때 거절한다.
        /// </summary>
        /// <remarks>
        /// 코루틴이나 람다는 제 타입으로 컴파일되고, 그 위의 필드들은 — <c>&lt;&gt;1__state</c>, <c>&lt;&gt;4__this</c>,
        /// display class 가 붙든 지역 변수들 — 배관이다. 효과로 보고되면 게임이 무언가를 바꾸는 것으로 읽히는데, 샘플
        /// 게임에서 그것들이 분석이 찾았다고 주장한 것 전체의 7분의 1이었다.
        ///
        /// 제 이름이 아니라 그것들을 선언하는 타입으로 거절한다. 꺾쇠 이름 패턴 하나는 배관이 아니기 때문이다: 자동 프로퍼티
        /// 뒤의 필드는 게임 자신의 타입에 선언되고 게임 자신의 상태를 쥔다. 그것들을 이름으로 떨어뜨리면 코드베이스의 모든
        /// <c>public int Score { get; set; }</c> 을 잃는다.
        /// </remarks>
        internal static string FieldName(FieldReference field)
        {
            var declaring = field?.DeclaringType;

            if (declaring == null)
            {
                return null;
            }

            if (declaring.Name.StartsWith("<", StringComparison.Ordinal))
            {
                return Hoisted(field.Name);
            }

            var name = field.Name;

            if (!name.StartsWith("<", StringComparison.Ordinal))
            {
                return declaring.Name + "." + name;
            }

            if (!name.EndsWith(BackingSuffix, StringComparison.Ordinal))
            {
                return null;
            }

            // 프로퍼티로 이름 붙인다. 그것이 소스가 말하는 바이고 명세가 적어야 할 바다.
            return declaring.Name + "." + name.Substring(1, name.Length - BackingSuffix.Length - 1);
        }

        /// <summary>
        /// 컴파일러가 코루틴 위로 옮겨 놓은 지역 변수. 소스가 부르던 이름으로.
        /// </summary>
        /// <remarks>
        /// 코루틴 안의 <c>for</c> 카운터는 읽힐 무렵이면 지역 변수가 아니다: yield 를 건너 살아 있으므로 생성된 타입 위의
        /// 필드다. 타입 전체를 거절하면 그것을 배관과 함께 거절하게 되고, 샘플 게임의 이야기 화면은 루프가 언제 끝나는지를
        /// 말하는 유일한 항을 잃었다 — 그것이 없으면 "아무 키나 누르면 맵이 열린다" 가 마지막 누름이 아니라 모든 누름에
        /// 대해 약속된다.
        ///
        /// 둘은 꺾쇠 안에 무엇이 있는지로 가려진다. 배관은 거기 아무것도 없고 (<c>&lt;&gt;1__state</c>,
        /// <c>&lt;&gt;4__this</c>, <c>&lt;&gt;t__builder</c>) 지킬 소스 이름이 없었기 때문이다. 옮겨진 지역 변수는 제
        /// 이름을 가진다 (<c>&lt;i&gt;5__1</c>). 그러니 이것은 필드가 무엇을 뜻하는지에 대한 추측이 아니다 — 소스가 쓴
        /// 이름을, 컴파일러가 넣어 둔 자리에서 되읽은 것이다.
        ///
        /// 그 앞에 타입 이름을 붙이지 않는다. 선언 타입은 아무도 쓰지 않았고 아무도 찾아볼 수 없는 이름이며,
        /// <c>&lt;StoryTelling&gt;d__8.i</c> 는 <c>i</c> 보다 적게 말한다.
        /// </remarks>
        private static string Hoisted(string name)
        {
            if (name == null || !name.StartsWith("<", StringComparison.Ordinal))
            {
                return null;
            }

            var close = name.IndexOf('>');

            return close > 1 ? name.Substring(1, close - 1) : null;
        }

        /// <summary>
        /// 메서드가 정확히 한 자리에서 쓸 때 지역 변수가 쥐고 있는 것.
        /// </summary>
        /// <remarks>
        /// 저장된 값을 만들어낸 명령어를 돌려주므로, 그것에 이름 대는 일은 다른 무엇에 이름 대는 일과 같은 일이다. 한 번
        /// 저장이 안전의 전부다: 그것이 몇 번을 돌든 이 읽기가 보았을 수 있는 다른 대입은 없으므로, 그중 어느 것이었는지에
        /// 대해 추측하는 것이 없다. 하나보다 많으면 물음이 되돌아오고, 답은 여전히 아니오다.
        ///
        /// 값이 무엇이라 불리는지와 그것이 누구의 것인지가 둘 다 여기를 지나고, 둘은 함께 지나야 한다. 최적화하는 컴파일러는
        /// 값을 지역 변수에 넣고 되읽는 자리에서 디버깅용은 그것을 다시 가져오므로, 같은 소스가 에디터 스캔에서 한 방식으로
        /// 개발 빌드에서 다른 방식으로 읽혔다. 이름만 지역 변수를 꿰뚫어 보게 두면 <c>MapMove.StagePosition == 0</c> 이라고
        /// 말하면서 그것이 누구의 것인지는 말하기를 거절하는 조건이 남았다.
        /// </remarks>
        private static Instruction StoredOnce(Instruction instruction, MethodDefinition within)
        {
            if (within == null || !within.HasBody || !IsLoadingLocal(instruction, out var slot))
            {
                return null;
            }

            Instruction only = null;

            foreach (var candidate in within.Body.Instructions)
            {
                if (!IsStoringLocal(candidate, out var stored) || stored != slot)
                {
                    continue;
                }

                if (only != null)
                {
                    return null;
                }

                only = candidate;
            }

            // 값은 저장 앞에 온 것이다. 그것을 읽는 일은 여기서 아무것에도 가두지 않는데, 저장은 이 읽기가 있는 자리가 아니라
            // 메서드가 그것을 놓은 자리에 있기 때문이다.
            return only?.Previous;
        }

        /// <summary>
        /// 필드가 어느 씬이 도는지의 복사본에 지나지 않을 때, 그 필드가 불리는 이름.
        /// </summary>
        /// <remarks>
        /// <c>sceneName = SceneManager.GetActiveScene().name</c> 을 쥐고 제 본문 전체를
        /// <c>sceneName == "GameClearScene"</c> 으로 지키는 컨트롤러는, 이것이 없으면 아무도 평가할 수 없는 문자열에 대한
        /// 조건으로 읽힌다. 샘플 게임은 그 컨트롤러 하나를 화면 둘에 올리므로 그것이 말하는 것의 절반은 그것이 있지 않은
        /// 화면에 대한 것이고 — 그 파수꾼을 보지 못하는 명세는 클리어 화면이 하는 모든 것을 게임오버 화면에도 약속한다.
        /// 객체가 어느 씬에서 발견됐는지를 아는 쪽이면 그것을 결판낼 수 있지만, 아무도
        /// <c>GameClearController.sceneName</c> 은 결판낼 수 없었다.
        ///
        /// 이 한 가지 모양만 본다. 일반 규칙 — 한 번 쓰인 필드를 거기 쓰인 무엇으로 이름 붙인다 — 은 시도하고 실측했으며,
        /// 틀렸다: 지역 변수의 한 번 저장은 읽기와 같은 메서드 안에 앉아 있지만 필드의 저장은 먼저 돌 필요가 없다.
        /// <c>flag = true</c> 는 <c>flag</c> 에 대한 유일한 쓰기이고, 그 필드를 <c>1</c> 로 읽으면 그것에 대한 모든 검사가
        /// <c>1 == 0</c> 이 되는데, 게임은 매번 그 분기를 타는데도 그것은 결코 탈 수 없는 분기로 읽힌다. 게다가 좋은 이름
        /// 여든넷을 함께 잃었다 — <c>onPushArea1</c> 은 그것을 채운 <c>Array.Exists()</c> 보다 많은 말을 한다.
        ///
        /// 활성 씬은 그 반론을 견딘다. 그것은 값이 아니기 때문이다: 어디서 읽히든 같은 식이고, 쓰기 전에도 쓴 뒤에도 같다.
        /// 그래서 이것은 그 식에 이름을 대고 멈추며, 그것이 무엇이 되는지는 독자의 몫이다.
        ///
        /// private 이므로 C# 이 허용하는 유일한 쓰기 주체가 그 타입 자신이고, 직렬화되지 않으므로 그 한 번의 저장 아래에
        /// 작성자가 넣어 둔 값이 없다.
        /// </remarks>
        private static string WhichScene(
            FieldReference field, MethodDefinition within, Instruction boundary, int depth)
        {
            if (depth >= MaxReceiverDepth)
            {
                return null;
            }

            var held = WrittenOnce(field, within, out var wroteIt);

            if (held == null)
            {
                return null;
            }

            var named = Describe(held, boundary, wroteIt, depth + 1);

            return named == ActiveScene ? named : null;
        }

        /// <summary>필드가 그것으로 읽혀도 되는 유일한 식. 다른 무엇도 되는 일이 없기 때문이다.</summary>
        private const string ActiveScene = "SceneManager.GetActiveScene().name";

        /// <summary>타입이 정확히 한 자리에서 쓸 때, 그 타입의 필드가 무엇에서 쓰이는가.</summary>
        private static Instruction WrittenOnce(
            FieldReference field, MethodDefinition within, out MethodDefinition wroteIt)
        {
            wroteIt = null;

            var owner = within?.DeclaringType;

            // 읽고 있는 타입만 본다. 필드에 이름 대는 일이 참조를, 들여다보라고 청받은 적 없는 어셈블리로 해석하는 일이 결코
            // 없도록.
            if (field?.DeclaringType == null || owner == null ||
                field.DeclaringType.FullName != owner.FullName)
            {
                return null;
            }

            FieldDefinition declared = null;

            foreach (var candidate in owner.Fields)
            {
                if (candidate.Name == field.Name)
                {
                    declared = candidate;
                    break;
                }
            }

            if (declared == null || !declared.IsPrivate || IsSerialized(declared))
            {
                return null;
            }

            Instruction only = null;

            foreach (var method in owner.Methods)
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code != Code.Stfld && instruction.OpCode.Code != Code.Stsfld)
                    {
                        continue;
                    }

                    if (!(instruction.Operand is FieldReference stored) || stored.Name != field.Name ||
                        stored.DeclaringType == null ||
                        stored.DeclaringType.FullName != owner.FullName)
                    {
                        continue;
                    }

                    if (only != null)
                    {
                        return null;
                    }

                    only = instruction;
                    wroteIt = method;
                }
            }

            if (only == null)
            {
                wroteIt = null;
                return null;
            }

            return only.Previous;
        }

        /// <summary>어떤 코드가 돌기 전에 인스펙터가 여기에 값을 넣어 두었을 수 있는지.</summary>
        private static bool IsSerialized(FieldDefinition field)
        {
            if (!field.HasCustomAttributes)
            {
                return false;
            }

            foreach (var attribute in field.CustomAttributes)
            {
                if (attribute.AttributeType != null &&
                    attribute.AttributeType.Name == "SerializeField")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLoadingLocal(Instruction instruction, out int slot)
        {
            slot = -1;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc_0: slot = 0; return true;
                case Code.Ldloc_1: slot = 1; return true;
                case Code.Ldloc_2: slot = 2; return true;
                case Code.Ldloc_3: slot = 3; return true;
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca:
                case Code.Ldloca_S:
                    slot = (instruction.Operand as VariableDefinition)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        private static bool IsStoringLocal(Instruction instruction, out int slot)
        {
            slot = -1;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Stloc_0: slot = 0; return true;
                case Code.Stloc_1: slot = 1; return true;
                case Code.Stloc_2: slot = 2; return true;
                case Code.Stloc_3: slot = 3; return true;
                case Code.Stloc:
                case Code.Stloc_S:
                    slot = (instruction.Operand as VariableDefinition)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        /// <summary>프로퍼티 읽기를 getter 가 아니라 프로퍼티로 이름 붙인다.</summary>
        private static string PropertyName(MethodReference method)
        {
            if (method == null || !method.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                return null;
            }

            return method.DeclaringType.Name + "." + method.Name.Substring(4);
        }

        internal static TypeDefinition SafeResolve(TypeReference reference)
        {
            try
            {
                return reference?.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 열거형이 제 값 하나에 붙이는 이름.
        /// </summary>
        /// <remarks>
        /// 숫자는 명령어가 나르는 것이고, 이름은 그것을 정의하는 어셈블리 안, 열거형 자신의 메타데이터에 산다. 숫자로
        /// 물러서는데, 그것은 여전히 쓸 만하고 눈에 띄게 이름이 아니다.
        /// </remarks>
        internal static string EnumName(TypeReference enumType, int value)
        {
            var definition = SafeResolve(enumType);

            if (definition == null || !definition.IsEnum)
            {
                return value.ToString();
            }

            foreach (var field in definition.Fields)
            {
                if (field.HasConstant && field.Constant is int constant && constant == value)
                {
                    return field.Name;
                }
            }

            return value.ToString();
        }
    }
}
