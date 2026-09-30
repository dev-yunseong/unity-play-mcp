using System.Collections.Generic;
using System.Text;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>플레이어 입력 하나.</summary>
    internal sealed class InputRead
    {
        internal string Gesture;
        internal string Name;
        internal string Phase;

        /// <summary>
        /// 이 입력이 주어지지 않아야 여기 닿을 때 참.
        /// </summary>
        /// <remarks>
        /// 단락된 <c>||</c> 는 첫 키가 눌리지 않았을 때만 둘째 키를 검사한다. 이것이 없으면 둘 중 하나면 되는
        /// 키가 둘 다 필요한 것으로 읽힌다.
        /// </remarks>
        internal bool Absent;
        internal int Offset;

        public override string ToString()
        {
            var text = Phase == null ? Gesture + ":" + Name : Gesture + ":" + Name + " (" + Phase + ")";
            return Absent ? "no " + text : text;
        }
    }

    /// <summary>여기 닿기 위해 참이어야 하는 조건 하나.</summary>
    internal sealed class Precondition
    {
        internal string Left;
        internal string Operator;
        internal string Right;

        /// <summary>
        /// <see cref="Context"/> 가 unknown 인 이유. 원인별로 세기 위한 진단용이다.
        /// </summary>
        /// <remarks>
        /// 합성에는 쓰지 않으므로 읽는 쪽이 무시해도 된다.
        /// </remarks>
        internal string SubjectLost;

        /// <summary>
        /// 조건의 기준 객체: <c>this</c>, <c>arg:N</c>, <c>static</c>, unknown.
        /// </summary>
        /// <remarks>
        /// 이것이 있어야 피호출자의 <c>this</c> 조건을 호출자의 receiver 기준으로 바꿔 쓸 수 있다.
        /// </remarks>
        internal string Context;

        internal int Offset;

        /// <summary>
        /// 런타임에 좌변을 되읽을 수 있는 멤버. 없으면 null.
        /// </summary>
        /// <remarks>
        /// 호출이나 지역 변수에 대한 조건은 메서드 실행 중에만 있는 값이므로 대부분 null 이다.
        /// </remarks>
        internal WatchTarget Watch;

        public override string ToString()
        {
            return Left + " " + Operator + " " + Right;
        }
    }

    /// <summary>게임이 일으키는 효과 하나.</summary>
    internal sealed class Outcome
    {
        internal string Kind;
        internal string Category;
        internal string Target;
        internal string Detail;

        /// <summary>
        /// 대상이 여러 곳에서 대입된 지역 변수일 때 들어갈 수 있었던 값들.
        /// </summary>
        /// <remarks>
        /// <c>Target</c> 을 대체하지 않는다. <c>Target</c> 은 코드의 이름이고 이것은 대입된 값이다.
        /// </remarks>
        internal System.Collections.Generic.List<string> TargetCandidates;

        /// <summary>
        /// 효과가 바꾼 필드를 되읽을 수 있는 멤버. 필드가 아니면 null.
        /// </summary>
        /// <remarks>
        /// 조건은 무엇을 마련할지, 효과는 그 뒤 무엇을 확인할지 알려 준다. 둘 다 값을 되읽을 수 있어야 쓸 수 있다.
        /// </remarks>
        internal WatchTarget Watch;

        /// <summary>
        /// 대입된 값을 읽어 온 멤버. 되읽을 수 있을 때만.
        /// </summary>
        /// <remarks>
        /// <c>character.transform.position = battle2.transform.position</c> 에서 바뀐 쪽만 감시하면 목적지가
        /// <c>battle2</c> 인지 확인할 수 없다.
        /// </remarks>
        internal WatchTarget WatchSource;

        /// <summary>
        /// 코드에 리터럴로 적힌 animator 파라미터나 상태 이름.
        /// </summary>
        /// <remarks>
        /// <c>AnimatorStateInfo</c> 는 해시만 주므로 `pulse` 는 이 후보를 <c>IsName</c> 으로 시험해 상태 이름을
        /// 찾는다. 트리거 이름과 상태 이름이 같다는 보장은 없어 <c>IsName</c> 이 참일 때만 이름을 대고 해시는
        /// 항상 함께 적는다.
        /// </remarks>
        internal string AnimatorName;

        internal int Offset;

        public override string ToString()
        {
            return Detail == null ? Kind + " " + Target : Kind + " " + Target + " " + Detail;
        }
    }

    /// <summary>이 경우의 조건 아래에서 일어나는 같은 어셈블리 안의 호출.</summary>
    internal sealed class CallEdge
    {
        internal string TargetId;
        internal string Target;

        /// <summary>
        /// 호출의 receiver.
        /// </summary>
        /// <remarks>
        /// 같은 <c>Raise</c> 를 부르는 두 버튼은 receiver 가 있어야 구분된다. 피호출자의 조건을 호출자 기준으로
        /// 합성할 때도 필요하다.
        /// </remarks>
        internal string Receiver;

        /// <summary>호출자 기준의 receiver 위치(<c>this</c>, <c>arg:N</c> 등).</summary>
        internal string ReceiverWhere;

        internal string Arguments;

        internal int Offset;
    }

    /// <summary>같은 경우에 닿는 또 하나의 진입 경로.</summary>
    internal sealed class Arrival
    {
        internal string Entry;
        internal string EntryId;
        internal string TriggerKind;
        internal List<string> CallPath;
    }

    /// <summary>델리게이트로 등록된 메서드.</summary>
    internal sealed class Subscription
    {
        /// <summary>핸들러가 붙은 필드나 프로퍼티. 알 수 없으면 null.</summary>
        internal string Channel;

        /// <summary>채널을 선언한 타입. 같은 타입의 발행자와 이어질 수 있다.</summary>
        internal string ChannelType;

        /// <summary>이벤트 또는 필드 이름.</summary>
        internal string Member;

        internal string Handler;
        internal string HandlerId;
        internal int Offset;
    }

    /// <summary>
    /// 입력, 조건, 효과의 묶음 하나.
    /// </summary>
    /// <remarks>
    /// 같은 키라도 분기마다 다른 variant 다. 합치면 어느 분기의 동작도 설명하지 못한다.
    /// </remarks>
    internal sealed class Variant
    {
        /// <summary><see cref="ToString"/> 에서 조건을 자르기 전까지 쓰는 길이.</summary>
        private const int WriteBudget = 40;

        internal string Method;
        internal string MethodId;

        /// <summary>
        /// 이 기록의 조건이 효과에 닿는 조건 전체인지 나타낸다.
        /// </summary>
        /// <remarks>
        /// 호출 경로로 찾은 기록은 자기 메서드의 조건만 가지고 호출 지점의 조건은 없다. 합성되기 전에는
        /// 테스트를 쓸 수 있는 기록과 구분되어야 한다.
        /// </remarks>
        internal string RecordKind = "candidate";

        /// <summary>이 `evidence` 의 출발점인 Unity 진입점.</summary>
        internal string Entry;
        internal string EntryId;

        /// <summary>진입 방식: Unity 이벤트, 생명주기, 코드 입력.</summary>
        internal string TriggerKind;

        /// <summary>진입점에서 이 메서드까지 따라온 같은 어셈블리 안의 모든 호출.</summary>
        internal readonly List<string> CallPath = new List<string>();

        /// <summary>기록이 붙을 타입. GameObject 에 붙을 수 있는 타입일 때만.</summary>
        internal Mono.Cecil.TypeDefinition Owner;

        internal Condition When = Condition.Always;
        internal readonly List<InputRead> Inputs = new List<InputRead>();
        internal readonly List<Outcome> Outcomes = new List<Outcome>();
        internal readonly List<CallEdge> Calls = new List<CallEdge>();
        internal readonly List<Subscription> Handles = new List<Subscription>();

        /// <summary>
        /// 같은 경우에 닿는 다른 진입점들.
        /// </summary>
        /// <remarks>
        /// 여러 곳에서 불리는 헬퍼를 기록 하나로 합친다. 첫 경로는 <see cref="Entry"/> 와 <see cref="CallPath"/>
        /// 에 그대로 있어 기존 읽는 쪽은 바뀌지 않는다.
        /// </remarks>
        internal readonly List<Arrival> AlsoReachedBy = new List<Arrival>();

        /// <summary>
        /// 이 메서드가 호출되지 않고 델리게이트로 건네진 오프셋. 없으면 -1.
        /// </summary>
        /// <remarks>
        /// 건네진 본문을 주변 효과와 순서대로 놓는 데 쓴다.
        /// </remarks>
        internal int HandedAt = -1;

        /// <summary><see cref="HandedAt"/> 이 속한 <see cref="CallPath"/> 의 인덱스. 없으면 -1.</summary>
        /// <remarks>
        /// 건네기 뒤에 호출이 더 이어져도 오프셋이 어느 메서드 본문의 것인지 알 수 있게 한다.
        /// </remarks>
        internal int HandedIn = -1;

        /// <summary>
        /// 건네진 메서드를 받은 쪽의 이름.
        /// </summary>
        /// <remarks>
        /// <c>UnityEngine.WaitUntil</c> 같은 이름만 적고, 대기인지 판단은 읽는 쪽에 맡긴다.
        /// </remarks>
        internal string HandedTo;

        /// <summary>
        /// 이 경우가 반복될 때 제어가 되돌아오는 오프셋. 없으면 -1.
        /// </summary>
        /// <remarks>
        /// 조건을 읽었는지와 상관없이 루프가 있으면 적는다.
        ///
        /// 되돌아오는 대상 블록은 자기 오프셋을, 되돌아가는 블록은 점프 대상을 적는다. 둘 다 같은 자리다.
        /// 루프 안에 있기만 한 블록은 적지 않는다.
        /// </remarks>
        internal int LoopsBackTo = -1;

        /// <summary>이 `evidence` 를 완전한 것으로 볼 수 없는 이유들.</summary>
        internal readonly List<string> Gaps = new List<string>();

        /// <summary>경로의 일부를 읽지 못했을 때 참.</summary>
        internal bool Incomplete;

        internal void AddGap(string gap)
        {
            if (!string.IsNullOrEmpty(gap) && !Gaps.Contains(gap))
            {
                Gaps.Add(gap);
                Incomplete = true;
            }
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(Method).Append("  when ");

            var budget = WriteBudget;
            When.Write(text, ref budget);

            text.Append("  -> ").Append(string.Join(", ", Outcomes));
            return text.ToString();
        }
    }
}
