using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    internal enum ConditionKind
    {
        /// <summary>참이어야 했던 것이 없다.</summary>
        Always,

        /// <summary>코드가 한 비교.</summary>
        Test,

        /// <summary>플레이어가 준 입력.</summary>
        Gesture,

        /// <summary>여기 오는 길에 있었으나 읽을 수 없었던 것.</summary>
        Unknown,

        /// <summary>이것들 전부.</summary>
        Every,

        /// <summary>이것들 중 아무거나 하나.</summary>
        Either
    }

    /// <summary>
    /// 어딘가에 닿기 위해 참이어야 했던 것.
    /// </summary>
    /// <remarks>
    /// 목록이 아니라 트리다. 목록은 "그리고" 밖에 뜻하지 못하기 때문이다. 한 자리에 두 갈래로 닿는 코드는 —
    /// <c>position == 4 || position == 5</c> — 그 필드가 두 값을 동시에 쥐고 있었다고 말하는 목록으로
    /// 납작해지는데, 그것을 만족하는 상태는 없다. 거기서 만든 명세는 아무도 수행할 수 없는 동작을 서술한다.
    ///
    /// 곱의 합으로 펼치지 않고 중첩된 채로 둔다. 가지들이 조상을 공유하므로 트리는 메서드만 한 크기로 남지만,
    /// 펼치면 그렇지 않다.
    /// </remarks>
    internal sealed class Condition
    {
        private string _key;

        internal ConditionKind Kind { get; private set; }
        internal Precondition Test { get; private set; }
        internal InputRead Gesture { get; private set; }
        internal string Reason { get; private set; }

        /// <summary>읽기를 좌절시킨 것의 모양. 세기 위한 것.</summary>
        /// <remarks>
        /// 읽지 못한 조건의 개수는 얼마나 빠졌는지를 말하지, 다음에 무엇을 만들지는 말하지 않는다. 여기 여든아홉이
        /// 걷기가 따라가기를 거부하는 지역 변수인지, 부를 말이 없는 연산자인지, 안을 들여다볼 수 없는 호출인지에
        /// 따라 서로 다른 일 세 가지가 결정되는데, 이 필드가 생기기 전까지 그 답은 추측이었다. 진단용일 뿐이다 —
        /// 아무것도 이것 위에서 합성하지 않고, 이것을 무시하는 독자는 전에 읽던 것을 그대로 읽는다.
        /// </remarks>
        internal string Unread { get; private set; }

        /// <summary>다시 한 바퀴 도는 일이 시작되는 자리, 또는 -1.</summary>
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
        /// 여기 닿으려면 다시 한 바퀴 돌아야 해서 읽을 수 없었던 조건.
        /// </summary>
        /// <remarks>
        /// 오프셋은 다시 도는 일이 시작되는 자리다. "루프" 라고만 말하면, 그 서로 다른 바퀴에서 일어나는 두 가지를
        /// 잇고 싶은 독자에게 남는 것은 오프셋에 대한 산술뿐이었다 — 리포트가 세운 적 없는 근거다. 이 엣지는
        /// 그래프가 이미 찾아 둔 것이고, 포기하는 순간에 버려지고 있었다.
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

                // 아무것도 필요로 하지 않는 갈래 하나가 선택 전체를 조건 없는 것으로 만든다.
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
        /// 선택으로 들어가는 한 갈래. 단락 평가가 남긴 자국을 걷어낸 것.
        /// </summary>
        /// <remarks>
        /// <c>GetKey(Left) || GetKey(Right)</c> 는 왼쪽이 눌리지 않았을 때만 오른쪽 키를 검사하므로, 오른쪽 키로
        /// 들어가는 갈래는 <c>no Left</c> 를 함께 나른다. 그것은 참이고, 게임에 대한 사실이 아니라 C# 이
        /// <c>||</c> 를 평가하는 방식에 대한 사실이다 — 명세로 읽으면 왼쪽을 조심스럽게 누르지 않은 채 오른쪽을
        /// 누르라는 말이 된다.
        ///
        /// 선택 아래에서만 그렇게 한다. <c>and</c> 맨 위의 부재하는 입력은 진짜 규칙이고 —
        /// <c>if (!Input.GetKey(Shift))</c> 는 게임이 뜻하는 바다 — 건드리지 않는다. 그리고 요구를 하나 떨어뜨리는
        /// 일은 갈래를 쉽게 만들 뿐이므로, 이것이 속한 선택은 전에 성립하던 자리에서 여전히 성립한다.
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
        /// 여기 있는 모든 비교가 호출자 자신의 객체에 대한 것인지, 아니면 아무것에 대한 것도 아닌지.
        /// </summary>
        /// <remarks>
        /// 입력은 비교가 아니고 주어가 없으므로 결코 걸림돌이 되지 않는다. 주어를 알아낼 수 없는 비교는 걸림돌이
        /// 된다 — 이 <c>count</c> 가 누구의 것인지 모른다는 것은 그것을 다른 누군가의 것 옆에서 읽어도 되는지를
        /// 모른다는 뜻이다.
        /// </remarks>
        /// <summary>
        /// 같은 조건을 호출자가 선 자리에서 말한 것, 또는 그럴 수 없을 때 null.
        /// </summary>
        /// <remarks>
        /// 피호출자의 조건은 피호출자의 객체에 대한 것이고 호출자의 용어 옆에서는 다른 말을 한다 — 그래서 합성하지
        /// 않고 거절한다. 거절이 옳은 것은 둘을 한 벌의 말로 데려올 수 없는 동안뿐이다. 호출자가 이름 붙일 수 있는
        /// 것에 대고 그것을 불렀다면 데려올 수 있다: 카드가 드래그되는 자리에서 읽은
        /// <c>CombineZone.spellCards.Count</c> 는 <c>DraggableCard.combineZone.spellCards.Count</c> 이고, 그
        /// 문장은 호출자 자신의 객체에 대한 것이며, 그것이 합성 규칙이 원하는 바다.
        ///
        /// 갈아 끼우기는 이름의 머리에서 일어나고, 그 머리가 피호출자 자신의 타입일 때만 일어난다. 여기의 모든
        /// 이름은 그것이 읽힌 출처로부터 쓰이므로 피호출자의 <c>this</c> 에 대한 항은 그 타입으로 시작한다. 그것으로
        /// 시작하지 않는 항은 다른 무언가에 대한 것이라 있던 자리에 둔다 — 그러면 조건 전체를 여기서 말할 수 없게
        /// 되므로 아무것도 돌려주지 않는다.
        ///
        /// 아무것도 떨어뜨리지 않고 아무것도 추측하지 않는다. 조건 전체를 호출자의 말로 말할 수 있거나, 아니면
        /// 하나도 내놓지 않는다. 반만 번역된 문장은 실제로는 둘인 것을 한 객체의 진술처럼 읽히게 하기 때문이다.
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
                        // 매개변수에 대한 항은 호출자가 거기 넣은 무엇에 대한 것이다.
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
                        // static 이거나 주어 없는 항은 어디서 읽어도 같은 뜻이다.
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
                    // Always 이거나, 제스처이거나, 읽지 못한 것이다. 그중 어느 것도 객체의 이름을 대지 않는다.
                    return this;
            }
        }

        /// <summary>항 하나를 호출자가 선 자리에서 말한 것, 또는 그럴 수 없을 때 null.</summary>
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

            // 숫자, 문자열, `null` — 피호출자의 객체 이름을 대는 것은 하나도 없다.
            if (!term.StartsWith(owner + ".", System.StringComparison.Ordinal))
            {
                return term.IndexOf('.') < 0 ? term : null;
            }

            return receiver + term.Substring(owner.Length);
        }

        /// <summary>항의 첫 이름. 나머지가 거기 매달린다.</summary>
        private static string HeadOf(string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return null;
            }

            var dot = term.IndexOf('.');
            return dot < 0 ? term : term.Substring(0, dot);
        }

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
                    // Always 이거나, 제스처이거나, 읽지 못한 것이다. 그중 어느 것도 객체의 이름을 대지 않는다.
                    return true;
            }
        }

        /// <summary>
        /// 같은 조건에서 입력만 남기고 전부 떨어뜨린 것.
        /// </summary>
        /// <remarks>
        /// 나오는 것은 들어간 것이 함의하는 것이고, 중요한 성질은 그것 하나뿐이다: 진실보다 적게 말할지언정 진실이
        /// 말하지 않는 것을 말하지는 않는다.
        ///
        /// 그래서 잇는 두 방식을 똑같이 다루지 않는다. <c>and</c> 의 모든 부분은 성립해야 했으므로, 그중 어느
        /// 것에서든 입력만 남겨도 여전히 참이다. <c>or</c> 은 *한* 갈래를 탔다는 것만 약속하므로, 그 입력은 **모든**
        /// 갈래에 입력이 있을 때만 남길 수 있다 — 그러지 않으면 입력 없는 갈래는 이것이 부정하게 될 갈래가 된다.
        ///
        /// 이것이 존재하는 이유는, 입력이 조건 안에서 객체에 속하지 않는 유일한 것이기 때문이다. 호출자의
        /// <c>count &gt; 0</c> 은 호출자의 <c>count</c> 에 대한 것이고 피호출자의 용어 옆에서는 다른 뜻이 된다.
        /// 호출자의 <c>Space 가 눌렸다</c> 는 키보드에 대한 것이고 어디서나 같은 뜻이다. 그래서 엣지가 수신자를
        /// 나를 수 있게 되기 전에 호출 엣지를 따라 내려보낼 수 있는 유일한 부분이다.
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
                    // 검사이거나, 알 수 없는 것이거나, 아예 아무것도 아니다. 그중 어느 것도 입력이 아니다.
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
        /// 나머지가 이미 말하는 것을 걷어낸다.
        /// </summary>
        /// <remarks>
        /// <c>else if</c> 사슬은 앞선 모든 검사를 그 부정으로 뒤에 남기므로, 네 번째 팔에 닿는 일은 <c>== 3</c> 이
        /// 이미 함의하는 <c>!=</c> 절 셋을 나른다. 그것들은 참이고, 중요한 절 하나를 파묻는다.
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
        /// 같은 말을 하는 두 조건이 같다고 비교되도록 하는 정규형.
        /// </summary>
        /// <remarks>
        /// 검사는 무엇을 말하는가뿐 아니라 어디서 읽혔는가로도 가려진다. 같은 말로 나온 두 읽기가 같은 사실인 것은
        /// 아니다 — 호출의 이름이 그 선언 타입에 대고 쓰이던 시절 <c>spellCards.Count == 1</c> 과
        /// <c>magicTypeCards.Count == 1</c> 이 한 문장으로 도착했고, 그중 하나는 반복으로 떨어져 나갔다. 그렇게
        /// 나간 것은 제 절반이 사라진 채 그렇다고 말하는 것도 없는 선행 조건이었고, 그것은 읽지 못한 것보다 나쁘다:
        /// 거기서 만든 명세는 게임이 카드 둘을 원하는 자리에서 하나를 청한다.
        ///
        /// 오프셋은 사람이 읽는 문장에 넣지 않는다. 그것은 같음이 결정되는 여기에 있고, 써 나가는 쪽은 건드리지
        /// 않는다.
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
                // 부재해야 했던 입력은 선행 조건이지 이것을 일으키는 방법이 아니다. 그것을 방법으로 나열하면 제 말과
                // 반대로 동작하는 키를 내놓게 된다.
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
        /// 조건을 써 나가되, 길어지면 멈춘다.
        /// </summary>
        /// <remarks>
        /// 트리는 가지를 공유하지만 써 나가는 것은 그렇지 않다. 예산이 메모리에서 촘촘한 모양이 텍스트 한 쪽이 되는
        /// 것을 막고, 표시가 어디서 멈췄는지를 말하므로 결과는 조용히 일부인 것이 아니라 짧은 것이 된다.
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
