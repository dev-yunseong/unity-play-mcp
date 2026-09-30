using System.Collections.Generic;
using System.Text;

namespace UnityPlayMcp.Affordances.CodeGen
{
    internal enum ConditionKind
    {
        /// <summary>참이어야 할 것이 없다.</summary>
        Always,

        /// <summary>코드가 한 비교.</summary>
        Test,

        /// <summary>플레이어가 준 입력.</summary>
        Gesture,

        /// <summary>경로에 있었지만 읽지 못한 조건.</summary>
        Unknown,

        /// <summary><see cref="Condition.Parts"/> 전부.</summary>
        Every,

        /// <summary><see cref="Condition.Parts"/> 중 하나.</summary>
        Either
    }

    /// <summary>
    /// 어딘가에 닿기 위해 참이어야 하는 조건.
    /// </summary>
    /// <remarks>
    /// 목록은 AND 만 표현하므로 트리로 둔다. <c>position == 4 || position == 5</c> 를 목록으로 만들면 만족할 수 없는
    /// 조건이 된다.
    ///
    /// 곱의 합으로 펼치지 않는다. 가지가 조상을 공유하므로 중첩된 트리는 메서드 크기에 머물지만 펼치면 커진다.
    /// </remarks>
    internal sealed class Condition
    {
        private string _key;

        internal ConditionKind Kind { get; private set; }
        internal Precondition Test { get; private set; }
        internal InputRead Gesture { get; private set; }
        internal string Reason { get; private set; }

        /// <summary>읽기를 막은 원인의 종류. 진단 집계용이다.</summary>
        /// <remarks>
        /// 원인(따라갈 수 없는 지역 변수, 이름 없는 연산자, 들여다볼 수 없는 호출)에 따라 개선 작업이 달라진다.
        /// 합성에는 쓰지 않는다.
        /// </remarks>
        internal string Unread { get; private set; }

        /// <summary>반복이 다시 시작되는 IL 오프셋. 없으면 -1.</summary>
        internal int LoopsBackTo { get; private set; } = -1;
        internal List<Condition> Parts { get; private set; }

        internal static readonly Condition Always = new Condition { Kind = ConditionKind.Always };

        internal static Condition FromTest(Precondition test)
        {
            return new Condition { Kind = ConditionKind.Test, Test = test };
        }

        internal static Condition FromGesture(InputRead gesture)
        {
            return new Condition { Kind = ConditionKind.Gesture, Gesture = gesture };
        }

        internal static Condition Unreadable(string reason, string unread = null)
        {
            return new Condition { Kind = ConditionKind.Unknown, Reason = reason, Unread = unread };
        }

        /// <summary>
        /// 반복을 한 번 더 돌아야 닿아서 읽을 수 없는 조건.
        /// </summary>
        /// <remarks>
        /// 반복이 시작되는 오프셋을 함께 남겨 읽는 쪽이 서로 다른 반복에서 일어나는 일을 이을 수 있게 한다.
        /// </remarks>
        internal static Condition Looping(int backTo)
        {
            return new Condition
            {
                Kind = ConditionKind.Unknown,
                Reason = "loop",
                LoopsBackTo = backTo
            };
        }

        internal static Condition Every(IEnumerable<Condition> parts)
        {
            var gathered = new List<Condition>();

            foreach (var part in parts)
            {
                if (part == null || part.Kind == ConditionKind.Always)
                {
                    continue;
                }

                if (part.Kind == ConditionKind.Every)
                {
                    AddDistinct(gathered, part.Parts);
                    continue;
                }

                AddDistinct(gathered, part);
            }

            DropImplied(gathered);

            if (gathered.Count == 0) return Always;
            if (gathered.Count == 1) return gathered[0];

            return new Condition { Kind = ConditionKind.Every, Parts = gathered };
        }

        internal static Condition Either(IEnumerable<Condition> parts)
        {
            var gathered = new List<Condition>();

            foreach (var raw in parts)
            {
                var part = WithoutShortCircuit(raw);

                if (part == null)
                {
                    continue;
                }

                // 조건 없는 경우가 하나라도 있으면 전체가 조건 없다.
                if (part.Kind == ConditionKind.Always)
                {
                    return Always;
                }

                if (part.Kind == ConditionKind.Either)
                {
                    AddDistinct(gathered, part.Parts);
                    continue;
                }

                AddDistinct(gathered, part);
            }

            if (gathered.Count == 0) return Always;
            if (gathered.Count == 1) return gathered[0];

            return new Condition { Kind = ConditionKind.Either, Parts = gathered };
        }

        /// <summary>
        /// OR 의 한 경우에서 단락 평가가 남긴 부재 입력을 걷어낸다.
        /// </summary>
        /// <remarks>
        /// <c>GetKey(Left) || GetKey(Right)</c> 의 오른쪽 경우는 <c>no Left</c> 를 함께 나른다. 이는 C# 의 <c>||</c> 평가
        /// 방식일 뿐 게임 규칙이 아니다.
        ///
        /// OR 아래에서만 걷어낸다. AND 최상위의 부재 입력(<c>if (!Input.GetKey(Shift))</c>)은 진짜 규칙이다. 요구를 떨어뜨리면
        /// 조건이 약해질 뿐이므로 OR 전체는 전에 성립하던 곳에서 여전히 성립한다.
        /// </remarks>
        private static Condition WithoutShortCircuit(Condition way)
        {
            if (way == null)
            {
                return null;
            }

            if (way.Kind == ConditionKind.Gesture)
            {
                return way.Gesture.Absent ? Always : way;
            }

            if (way.Kind != ConditionKind.Every)
            {
                return way;
            }

            List<Condition> kept = null;

            for (var index = 0; index < way.Parts.Count; index++)
            {
                var part = way.Parts[index];
                var absent = part.Kind == ConditionKind.Gesture && part.Gesture.Absent;

                if (absent && kept == null)
                {
                    kept = new List<Condition>(way.Parts.GetRange(0, index));
                    continue;
                }

                if (!absent)
                {
                    kept?.Add(part);
                }
            }

            return kept == null ? way : Every(kept);
        }

        /// <summary>
        /// 같은 조건을 호출자 쪽 용어로 옮긴 것. 옮길 수 없으면 null.
        /// </summary>
        /// <remarks>
        /// 호출자가 이름 붙일 수 있는 수신 객체에 대해 불렀다면 옮길 수 있다. 예:
        /// <c>CombineZone.spellCards.Count</c> 는 호출 지점에서 <c>DraggableCard.combineZone.spellCards.Count</c> 가 된다.
        ///
        /// 항의 머리가 피호출자 자신의 타입일 때만 바꾼다. 그렇지 않은 항이 하나라도 있으면 전체를 null 로 한다. 반만
        /// 옮긴 조건은 두 객체에 대한 내용을 한 객체의 것처럼 읽히게 한다.
        /// </remarks>
        internal Condition ReadFrom(Binding binding)
        {
            if (binding == null || !binding.Anything)
            {
                return null;
            }

            switch (Kind)
            {
                case ConditionKind.Test:
                {
                    string head;
                    string term;
                    string standing;

                    if (Test.Context == "this")
                    {
                        if (binding.Receiver == null)
                        {
                            return null;
                        }

                        head = binding.Owner;
                        term = binding.Receiver;
                        standing = binding.ReceiverWhere;
                    }
                    else if (Test.Context != null && Test.Context.StartsWith("arg:", System.StringComparison.Ordinal))
                    {
                        // 매개변수에 대한 항은 호출자가 넘긴 인자에 대한 것이다.
                        head = HeadOf(Test.Left);

                        if (head == null || binding.Passed == null ||
                            !binding.Passed.TryGetValue(head, out term))
                        {
                            return null;
                        }

                        standing = binding.PassedWhere != null &&
                                   binding.PassedWhere.TryGetValue(head, out var whose)
                            ? whose
                            : null;
                    }
                    else
                    {
                        // static 이거나 주어가 없는 항은 어디서 읽어도 같은 뜻이다.
                        return Test.Context == "static" || Test.Context == null ? this : null;
                    }

                    var left = Swapped(Test.Left, head, term);
                    var right = Swapped(Test.Right, head, term);

                    if (left == null || right == null)
                    {
                        return null;
                    }

                    return FromTest(new Precondition
                    {
                        Left = left,
                        Operator = Test.Operator,
                        Right = right,
                        Context = standing,
                        SubjectLost = standing == null ? Test.SubjectLost : null,
                        Offset = Test.Offset
                    });
                }

                case ConditionKind.Every:
                case ConditionKind.Either:
                {
                    var moved = new List<Condition>(Parts.Count);

                    foreach (var part in Parts)
                    {
                        var said = part.ReadFrom(binding);

                        if (said == null)
                        {
                            return null;
                        }

                        moved.Add(said);
                    }

                    return Kind == ConditionKind.Every ? Every(moved) : Either(moved);
                }

                default:
                    // Always, 입력, 읽지 못한 조건은 객체를 가리키지 않는다.
                    return this;
            }
        }

        /// <summary>항 하나를 호출자 쪽 용어로 옮긴 것. 옮길 수 없으면 null.</summary>
        internal static string Swapped(string term, string owner, string receiver)
        {
            if (term == null || owner == null || receiver == null)
            {
                return null;
            }

            if (term == owner)
            {
                return receiver;
            }

            // 숫자, 문자열, `null` 은 피호출자의 객체를 가리키지 않는다.
            if (!term.StartsWith(owner + ".", System.StringComparison.Ordinal))
            {
                return term.IndexOf('.') < 0 ? term : null;
            }

            return receiver + term.Substring(owner.Length);
        }

        /// <summary>항의 첫 이름.</summary>
        private static string HeadOf(string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return null;
            }

            var dot = term.IndexOf('.');
            return dot < 0 ? term : term.Substring(0, dot);
        }

        /// <summary>
        /// 모든 검사가 호출자 자신의 객체나 static 에 대한 것일 때 참.
        /// </summary>
        /// <remarks>
        /// 입력은 주어가 없으므로 막지 않는다. 주어를 알 수 없는 검사는 다른 객체의 조건과 함께 읽어도 되는지 알 수
        /// 없으므로 거짓이 된다.
        /// </remarks>
        internal bool AboutSelfOnly()
        {
            switch (Kind)
            {
                case ConditionKind.Test:
                    return Test.Context == "this" || Test.Context == "static";

                case ConditionKind.Every:
                case ConditionKind.Either:
                    foreach (var part in Parts)
                    {
                        if (!part.AboutSelfOnly())
                        {
                            return false;
                        }
                    }

                    return true;

                default:
                    // Always, 입력, 읽지 못한 조건은 객체를 가리키지 않는다.
                    return true;
            }
        }

        /// <summary>
        /// 같은 조건에서 입력만 남긴 것.
        /// </summary>
        /// <remarks>
        /// 결과는 원래 조건이 함의하는 것이어야 한다. 덜 말할 수는 있어도 틀린 것을 말하면 안 된다. AND 는 어느 부분에서든
        /// 입력을 남겨도 참이다. OR 은 모든 경우에 입력이 있을 때만 남긴다. 입력 없는 경우가 있으면 Always 다.
        ///
        /// 입력은 객체에 속하지 않아 어디서나 같은 뜻이므로 수신 객체를 옮기지 않고도 호출 엣지를 따라 내려보낼 수 있다.
        /// </remarks>
        internal Condition InputsOnly()
        {
            switch (Kind)
            {
                case ConditionKind.Gesture:
                    return this;

                case ConditionKind.Every:
                {
                    var kept = new List<Condition>();

                    foreach (var part in Parts)
                    {
                        var inputs = part.InputsOnly();

                        if (inputs.Kind != ConditionKind.Always)
                        {
                            kept.Add(inputs);
                        }
                    }

                    return kept.Count == 0 ? Always : Every(kept);
                }

                case ConditionKind.Either:
                {
                    var kept = new List<Condition>();

                    foreach (var part in Parts)
                    {
                        var inputs = part.InputsOnly();

                        if (inputs.Kind == ConditionKind.Always)
                        {
                            return Always;
                        }

                        kept.Add(inputs);
                    }

                    return kept.Count == 0 ? Always : Either(kept);
                }

                default:
                    // 검사, 읽지 못한 조건 등은 입력이 아니다.
                    return Always;
            }
        }

        private static void AddDistinct(List<Condition> gathered, Condition part)
        {
            foreach (var existing in gathered)
            {
                if (existing.Key == part.Key)
                {
                    return;
                }
            }

            gathered.Add(part);
        }

        private static void AddDistinct(List<Condition> gathered, List<Condition> parts)
        {
            foreach (var part in parts)
            {
                AddDistinct(gathered, part);
            }
        }

        /// <summary>
        /// 다른 부분이 이미 함의하는 부분을 걷어낸다.
        /// </summary>
        /// <remarks>
        /// <c>else if</c> 사슬의 네 번째 경우는 <c>== 3</c> 이 이미 함의하는 <c>!=</c> 절 셋을 나르고, 그것이 중요한 절을 가린다.
        /// </remarks>
        private static void DropImplied(List<Condition> parts)
        {
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                var candidate = parts[i];

                if (candidate.Kind != ConditionKind.Test || candidate.Test.Operator != "!=")
                {
                    continue;
                }

                foreach (var other in parts)
                {
                    if (other.Kind != ConditionKind.Test ||
                        other.Test.Operator != "==" ||
                        other.Test.Left != candidate.Test.Left ||
                        other.Test.Right == candidate.Test.Right)
                    {
                        continue;
                    }

                    parts.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// 같은 내용의 두 조건이 같다고 비교되게 하는 정규형.
        /// </summary>
        /// <remarks>
        /// 검사는 내용뿐 아니라 읽힌 오프셋으로도 구분한다. 같은 문자열로 쓰인 두 검사가 같은 사실은 아니며, 하나를 중복으로
        /// 떨어뜨리면 선행 조건의 절반이 말없이 사라진다.
        ///
        /// 오프셋은 사람이 읽는 문자열에는 넣지 않고 여기서만 쓴다.
        /// </remarks>
        internal string Key
        {
            get
            {
                if (_key != null)
                {
                    return _key;
                }

                switch (Kind)
                {
                    case ConditionKind.Always:
                        _key = "T";
                        break;
                    case ConditionKind.Test:
                        _key = "t:" + Test + "@" + Test.Offset;
                        break;
                    case ConditionKind.Gesture:
                        _key = "g:" + Gesture;
                        break;
                    case ConditionKind.Unknown:
                        _key = "?:" + Reason;
                        break;
                    default:
                        var keys = new List<string>(Parts.Count);
                        foreach (var part in Parts)
                        {
                            keys.Add(part.Key);
                        }

                        keys.Sort(System.StringComparer.Ordinal);
                        _key = (Kind == ConditionKind.Every ? "&(" : "|(") +
                               string.Join(",", keys) + ")";
                        break;
                }

                return _key;
            }
        }

        internal void CollectGestures(List<InputRead> into, HashSet<Condition> seen)
        {
            if (!seen.Add(this))
            {
                return;
            }

            if (Kind == ConditionKind.Gesture)
            {
                // 부재해야 하는 입력은 선행 조건이지 이것을 일으키는 방법이 아니다. 방법으로 나열하면 반대로 동작하는 키가 된다.
                if (Gesture.Absent)
                {
                    return;
                }

                foreach (var existing in into)
                {
                    if (existing.ToString() == Gesture.ToString())
                    {
                        return;
                    }
                }

                into.Add(Gesture);
                return;
            }

            if (Parts == null)
            {
                return;
            }

            foreach (var part in Parts)
            {
                part.CollectGestures(into, seen);
            }
        }

        internal bool HasUnknown(HashSet<Condition> seen)
        {
            if (!seen.Add(this))
            {
                return false;
            }

            if (Kind == ConditionKind.Unknown)
            {
                return true;
            }

            if (Parts == null)
            {
                return false;
            }

            foreach (var part in Parts)
            {
                if (part.HasUnknown(seen))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 조건을 써 나가되 예산을 넘으면 멈춘다.
        /// </summary>
        /// <remarks>
        /// 트리는 가지를 공유하지만 텍스트는 그렇지 않아 펼치면 커진다. 멈춘 자리에 표시를 남겨 잘렸음을 드러낸다.
        /// </remarks>
        internal void Write(StringBuilder text, ref int budget)
        {
            if (budget-- <= 0)
            {
                text.Append('…');
                return;
            }

            switch (Kind)
            {
                case ConditionKind.Always:
                    text.Append("always");
                    return;

                case ConditionKind.Test:
                    text.Append(Test);
                    return;

                case ConditionKind.Gesture:
                    text.Append(Gesture);
                    return;

                case ConditionKind.Unknown:
                    text.Append('<').Append(Reason).Append('>');
                    return;
            }

            var joiner = Kind == ConditionKind.Every ? " and " : " or ";

            for (var index = 0; index < Parts.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(joiner);
                }

                var part = Parts[index];
                var wrap = part.Kind == ConditionKind.Every || part.Kind == ConditionKind.Either;

                if (wrap) text.Append('(');
                part.Write(text, ref budget);
                if (wrap) text.Append(')');

                if (budget <= 0)
                {
                    return;
                }
            }
        }
    }
}
