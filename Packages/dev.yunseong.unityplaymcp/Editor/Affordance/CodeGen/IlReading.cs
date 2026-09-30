using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>IL 명령어에서 값과 이름을 읽는다.</summary>
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
        /// 명령어가 스택에 올리는 값의 짧은 이름. 이름 붙일 수 없으면 null.
        /// </summary>
        /// <remarks>
        /// 호출자는 null 을 읽지 못한 조건으로 다룬다.
        ///
        /// 경계는 읽는 블록의 첫 명령어이고 그 앞은 읽지 않는다. 경계가 없으면 인자를 읽지 않는다. 합류 지점 앞의
        /// 명령어는 다른 경로의 것일 수 있다.
        /// </remarks>
        internal static string Describe(Instruction instruction)
        {
            return Describe(instruction, null);
        }

        /// <summary>
        /// 호출의 receiver 를 그것을 가진 필드까지 따라간 명령어.
        /// </summary>
        /// <remarks>
        /// <c>MapMove.character.transform.position</c> 의 receiver 는 호출이지만, 따라가면 런타임에 되읽을 수 있는
        /// 필드 <c>character</c> 가 나온다.
        ///
        /// <c>transform</c> 과 <c>gameObject</c> 만 지나간다. 둘은 같은 객체를 가리킨다. 다른 getter 는 다른 객체를
        /// 돌려줄 수 있으므로(<c>list.First().position</c>) 거기서 멈춘다.
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

        /// <summary><see cref="Rooted"/> 와 같은 걷기를 receiver 대신 주어진 명령어에서 시작한다.</summary>
        /// <remarks>
        /// 트윈 확장 메서드는 transform 을 인자로 받으므로 인자에서 시작한다. 두 경우가 같은 걷기를 써야 결과가 일치한다.
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
        /// 한 번만 저장되는 지역 변수를 거슬러 실제로 값을 올린 명령어.
        /// </summary>
        /// <remarks>
        /// <see cref="Describe"/> 와 같은 따라가기라 감시 대상이 조건 문장의 이름과 일치한다. 지역 변수는 메서드가
        /// 끝나면 사라지므로 감시 대상이 될 수 없다.
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
        /// <see cref="Describe(Instruction, Instruction)"/> 에 더해 한 번만 저장되는 지역 변수를 거슬러 읽는다.
        /// </summary>
        /// <remarks>
        /// 디버그 빌드는 값을 지역 변수에 복사해 두고 읽는다. 지역 변수는 다른 곳에서 대입될 수 있으므로 메서드 안에서
        /// 정확히 한 번 저장될 때만 따라간다.
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
                // 디버그 빌드는 `ldarg.1; stloc.1; ldloc.1; stloc.0` 처럼 여러 번 복사하므로 계속 따라간다.
                // 깊이 제한은 순환을 막는다.
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
        /// 인자 적재가 가리키는 <c>this</c> 또는 매개변수 이름.
        /// </summary>
        /// <remarks>
        /// <see cref="SingletonPlumbing"/> 은 <c>Destroy(this)</c> 를 <c>this</c> 로 알아본다.
        ///
        /// 이름은 metadata 에서 읽으므로 난독화로 지워졌으면 null 이다. 조건의 기준 객체는 <see cref="Where"/> 가
        /// <c>arg:N</c> 으로 따로 적는다.
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
        /// 디버그 심볼에 있는 지역 변수 이름. 없으면 null.
        /// </summary>
        /// <remarks>
        /// 값을 따라가지 않고 이름만 댄다. <c>for</c> 루프 카운터 <c>i</c> 는 두 번 저장되어 따라갈 수 없지만
        /// <c>i &lt; cards.Count</c> 로는 읽을 수 있다. 기준 객체는 여전히 unknown 이다.
        ///
        /// 릴리스 빌드에는 심볼이 없다. 컴파일러가 만든 <c>&lt;</c> 로 시작하는 이름은 쓰지 않는다.
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
        /// 산술 식을 소스 형태로 적는다.
        /// </summary>
        /// <remarks>
        /// 게임 루프의 조건은 <c>distance / length</c> 처럼 계산된 값을 자주 비교한다.
        ///
        /// 스택 위의 피연산자만 본다. 긴 식은 읽기 어려우므로 깊이도 제한한다.
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

        /// <summary>단항 부정(<c>neg</c>) 식을 적는다.</summary>
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

        /// <summary>피연산자에 이름을 대고, 산술 식이면 한 단계 더 들어간다.</summary>
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

        /// <summary>산술 식을 적는 최대 깊이.</summary>
        private const int MaxArithmeticDepth = 4;

        /// <summary>
        /// 호출의 반환값을 receiver 와 인자를 붙여 이름 짓는다.
        /// </summary>
        /// <remarks>
        /// <c>CompareTag("Spell")</c> 처럼 인자가 있어야 조건끼리 구분된다. 읽을 수 없는 인자는 <c>_</c> 로 쓴다.
        ///
        /// receiver 를 먼저 쓰고, 읽을 수 없을 때만 선언 타입을 쓴다. 타입만 쓰면 두 목록의 <c>List`1.Count</c> 가
        /// 구분되지 않는다. receiver 도 receiver 를 가지므로 깊이를 제한한다.
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

                // 인덱서는 get_Item 이 아니라 소스처럼 대괄호로 쓴다.
                return method.Parameters.Count == 0
                    ? property
                    : property + "[" + (arguments ?? Unread(method.Parameters.Count)) + "]";
            }

            return owner + "." + method.Name + "(" + (arguments ?? "") + ")";
        }

        /// <summary>
        /// 호출 receiver 의 이름. static 호출이거나 읽을 수 없으면 null.
        /// </summary>
        /// <remarks>
        /// <c>this</c> 에 대한 호출은 선언 타입에 맡긴다. <c>this</c> 의 필드도 <c>CombineZone.spellCards</c> 처럼
        /// 선언 타입으로 쓰고, 기준 객체는 조건의 <c>context</c> 가 나른다.
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

        /// <summary>receiver 를 거슬러 적는 최대 깊이.</summary>
        private const int MaxReceiverDepth = 3;

        /// <summary>
        /// 호출의 인자 목록. 읽지 못한 인자는 <c>_</c>, 하나도 읽지 못하면 null.
        /// </summary>
        /// <remarks>
        /// 호출 바로 앞 명령어가 마지막 인자이므로 뒤에서부터 읽고, 인자 사이는 <see cref="Under"/> 로 건너뛴다.
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

        /// <summary>위치로 지정한 인자 하나의 이름. 읽을 수 없으면 null.</summary>
        /// <remarks>
        /// <see cref="Arguments"/> 와 결과가 어긋나지 않도록 같은 목록 읽기에서 하나를 꺼낸다.
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
        /// 여러 번 저장되는 지역 변수에 들어갈 수 있는 값들.
        /// </summary>
        /// <remarks>
        /// 한 번 저장되는 지역 변수는 <see cref="StoredOnce"/> 로 따라간다. switch 의 각 경우에서 프리팹을 골라
        /// 합류 뒤에 만드는 코드에서 후보 목록을 준다.
        ///
        /// 저장 중 하나라도 읽지 못하면 null 이다. 빠진 목록은 완전한 목록으로 오해된다.
        /// <paramref name="most"/> 를 넘으면 선택이 아니라 누적으로 보고 null 이다.
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
        /// 호출 receiver 의 이름.
        /// </summary>
        /// <remarks>
        /// receiver 는 모든 인자 아래에 있으므로 인자를 차례로 건너뛴다. 서로 다른 채널 필드에 <c>Raise</c> 를 부르는
        /// 두 버튼은 receiver 로 구분된다.
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

        /// <summary>호출자 기준의 receiver 위치(<c>this</c>, <c>arg:N</c>, <c>static</c>).</summary>
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
        /// 값의 기준 객체: <c>this</c>, <c>arg:N</c>, <c>static</c>. 알 수 없으면 null.
        /// </summary>
        /// <remarks>
        /// 기준 객체 없이 피호출자의 조건을 호출자 옆에 놓으면 호출자의 객체에 대한 잘못된 조건이 된다.
        ///
        /// 값이 처음 읽힌 곳까지 거슬러 간다. <c>this</c> 의 필드의 필드는 <c>this</c> 기준이다.
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
        /// 같은 걷기에 더해 멈춘 명령어를 <paramref name="stoppedAt"/> 로 돌려준다.
        /// </summary>
        /// <remarks>
        /// 실패 원인은 출발 피연산자가 아니라 receiver 아래 어딘가에 있으므로 멈춘 자리를 진단에 쓴다.
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

                // 한 번만 저장되는 지역 변수는 저장된 값으로 거슬러 간다. 이름 붙이기(Describe)와 결과를 맞춘다.
                //
                // 한 번만 따라가고 이후에는 `within` 을 비운다. 지역 변수 사슬까지 따라가면 한 번 저장 규칙만으로는
                // 안전하지 않다.
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

                    // 큰 정수와 실수 리터럴도 상수다. 빠지면 `Vector3.x < -10` 같은 비교의 기준 객체를 잃는다.
                    case Code.Ldc_I8:
                    case Code.Ldc_R4:
                    case Code.Ldc_R8:
                        return "static";

                    default:
                        if (TryConstant(instruction, out _))
                        {
                            return "static";
                        }

                        // 산술 식의 기준 객체는 양쪽 피연산자가 일치하는 기준 객체다.
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

                        // 입력이 하나면 그 입력으로 내려간다. 호출은 receiver 로 내려가고, 그 밖에는 멈춘다.
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
        /// 양쪽이 공유하는 기준 객체. 없으면 null.
        /// </summary>
        /// <remarks>
        /// <c>static</c>(상수) 쪽은 어느 것과도 일치한다. 예: <c>this</c> 의 필드를 숫자로 나눈 식.
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

        /// <summary>호출의 receiver 를 올린 명령어.</summary>
        private static Instruction Receiving(MethodReference method, Instruction call, Instruction boundary)
        {
            var at = Preceding(call, boundary);

            for (var index = 0; index < method.Parameters.Count && at != null; index++)
            {
                at = Under(at, boundary);
            }

            return at;
        }

        /// <summary>읽지 못한 인자 수만큼의 <c>_</c> 목록.</summary>
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
        /// 인자 하나의 이름. 가능하면 소스 형태로 적는다.
        /// </summary>
        /// <remarks>
        /// bool 과 enum 은 IL 에서 정수이므로 매개변수 타입으로 <c>false</c> 나 enum 멤버 이름으로 되돌린다.
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

                // int 인자마다 타입을 resolve 하지 않도록 값 타입(시그니처의 enum)일 때만 해석한다.
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
                    // object 로 넘어간 enum 은 박싱되므로 box 의 타입으로 해석한다.
                    return Argument(
                        Preceding(instruction, boundary), instruction.Operand as TypeReference, boundary,
                        within);

                default:
                    return Describe(instruction, boundary, within);
            }
        }

        /// <summary>
        /// 앞 명령어. <c>nop</c> 과 접두 명령은 건너뛰고 블록 시작을 넘지 않는다.
        /// </summary>
        /// <remarks>
        /// 블록 첫 명령어 앞은 여러 경로 중 하나일 뿐이다. 경계를 넘으면 단락된 <c>&amp;&amp;</c> 가 놓은 리터럴
        /// <c>0</c> 을 읽어 <c>0 != 0</c> 같은 조건을 만든다.
        /// </remarks>
        internal static Instruction Preceding(Instruction instruction, Instruction boundary)
        {
            if (instruction == null || instruction == boundary)
            {
                return null;
            }

            var previous = instruction.Previous;

            // constrained., volatile., readonly. 같은 접두 명령은 스택을 건드리지 않는다. 여기서 멈추면 값 타입
            // receiver 호출(Enum.Equals 등)의 인자를 읽지 못한다.
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
        /// 주어진 명령어가 만든 값 바로 아래 스택 슬롯의 값을 올린 명령어.
        /// </summary>
        /// <remarks>
        /// 명령어가 소비한 입력을 재귀적으로 건너뛴다. 이것을 무시하면 <c>a == b.Count</c> 가 <c>b == b.Count</c> 로
        /// 읽힌다. 스택 효과를 모르는 명령어에서는 null 을 돌려주고 호출자는 읽지 못한 조건으로 다룬다.
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

        /// <summary>명령어가 소비하는 스택 슬롯 수. 알 수 없으면 -1.</summary>
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
        /// 이름 접두어로 판단하는 opcode 계열의 소비 슬롯 수.
        /// </summary>
        /// <remarks>
        /// <c>conv.*</c> 와 <c>ldind.*</c> 는 하나, <c>ldelem.*</c> 는 배열과 인덱스 둘을 소비한다. 그 밖에는 -1 이다.
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
        /// 필드 이름. 컴파일러가 만든 상태 필드면 null.
        /// </summary>
        /// <remarks>
        /// coroutine 이나 람다의 생성 타입 필드(<c>&lt;&gt;1__state</c>, <c>&lt;&gt;4__this</c> 등)는 게임 상태가 아니다.
        ///
        /// 이름이 아니라 선언 타입으로 거른다. 자동 프로퍼티의 backing field 도 꺾쇠로 시작하지만 게임 상태다.
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

            // backing field 는 소스에 쓰인 프로퍼티 이름으로 적는다.
            return declaring.Name + "." + name.Substring(1, name.Length - BackingSuffix.Length - 1);
        }

        /// <summary>
        /// coroutine 생성 타입의 필드로 옮겨진 지역 변수의 소스 이름. 컴파일러 상태 필드면 null.
        /// </summary>
        /// <remarks>
        /// yield 를 건너 사는 <c>for</c> 카운터 같은 지역 변수는 필드가 된다. 옮겨진 지역 변수는 꺾쇠 안에 이름이
        /// 있고(<c>&lt;i&gt;5__1</c>), 컴파일러 상태 필드는 비어 있다(<c>&lt;&gt;1__state</c>).
        ///
        /// 생성 타입 이름(<c>&lt;StoryTelling&gt;d__8</c>)은 소스에 없으므로 앞에 붙이지 않는다.
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
        /// 메서드 안에서 정확히 한 번 저장되는 지역 변수의 값을 올린 명령어. 아니면 null.
        /// </summary>
        /// <remarks>
        /// 저장이 한 번뿐이면 다른 대입이 있을 수 없으므로 안전하다.
        ///
        /// 이름(<see cref="Describe(Instruction, Instruction, MethodDefinition)"/>)과 기준 객체(<see cref="Where"/>)가
        /// 모두 이것을 써야 디버그 빌드와 릴리스 빌드의 결과가 같다.
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

            // 저장은 현재 블록 밖에 있을 수 있으므로 boundary 없이 바로 앞 명령어를 돌려준다.
            return only?.Previous;
        }

        /// <summary>
        /// 필드가 활성 씬 이름의 복사본일 때 <see cref="ActiveScene"/>. 아니면 null.
        /// </summary>
        /// <remarks>
        /// <c>sceneName = SceneManager.GetActiveScene().name</c> 을 저장해 두고 <c>sceneName == "GameClearScene"</c> 으로
        /// 분기하는 컨트롤러는 이것이 있어야 씬 조건으로 읽힌다.
        ///
        /// 이 식만 인정한다. 한 번 쓰인 필드를 그 값으로 일반화하면 틀린다. 필드 저장은 읽기보다 먼저 돈다는 보장이
        /// 없어 <c>flag = true</c> 가 <c>1 == 0</c> 같은 조건을 만든다. 활성 씬 이름은 언제 읽어도 같은 식이다.
        ///
        /// private 이고 직렬화되지 않는 필드만 본다. 그래야 그 타입의 저장 하나가 유일한 값의 출처다.
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

        /// <summary><see cref="WhichScene"/> 이 필드 대신 쓰는 유일한 식.</summary>
        private const string ActiveScene = "SceneManager.GetActiveScene().name";

        /// <summary>타입 안에서 필드가 정확히 한 번 저장될 때 저장 값을 올린 명령어.</summary>
        private static Instruction WrittenOnce(
            FieldReference field, MethodDefinition within, out MethodDefinition wroteIt)
        {
            wroteIt = null;

            var owner = within?.DeclaringType;

            // 다른 어셈블리를 resolve 하지 않도록 읽고 있는 타입의 필드만 본다.
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

        /// <summary>인스펙터가 코드 실행 전에 값을 넣었을 수 있는지.</summary>
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
        /// enum 값의 멤버 이름.
        /// </summary>
        /// <remarks>
        /// 이름은 enum 을 정의한 어셈블리의 metadata 에 있다. 찾지 못하면 숫자를 돌려준다.
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
