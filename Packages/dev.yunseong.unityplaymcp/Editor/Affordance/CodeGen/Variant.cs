using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>플레이어가 하는 무언가.</summary>
    internal sealed class InputRead
    {
        internal string Gesture;
        internal string Name;
        internal string Phase;

        /// <summary>
        /// 여기 닿는다는 것이 이 입력이 주어지지 *않았다는* 뜻일 때 참.
        /// </summary>
        /// <remarks>
        /// 단락된 <c>||</c> 는 첫 키가 눌리지 않았을 때만 둘째 키를 검사하므로, 키 한 쌍을 지나는 갈래의 절반은
        /// 부재해야 하는 제스처를 나른다. 이것 없이 읽으면 둘 중 하나면 되는 두 키가 함께 필요한 것으로 읽힌다.
        /// </remarks>
        internal bool Absent;
        internal int Offset;

        public override string ToString()
        {
            var text = Phase == null ? Gesture + ":" + Name : Gesture + ":" + Name + " (" + Phase + ")";
            return Absent ? "no " + text : text;
        }
    }

    /// <summary>여기 오기 위해 참이어야 했던 것.</summary>
    internal sealed class Precondition
    {
        internal string Left;
        internal string Operator;
        internal string Right;

        /// <summary>
        /// 주어가 왜 unknown 으로 나왔는가. 세기 위한 것이고 진단용일 뿐이다.
        /// </summary>
        /// <remarks>
        /// 같은 원본을 두 번 읽으면 두 답이 나오고 — 에디터 스캔에서 서른아홉, 개발 빌드에서 하나도 없었다 —
        /// 개수만으로는 그것이 한 가지 원인인지 다섯 가지인지 말할 수 없다. 아무것도 이것 위에서 합성하지 않고,
        /// 이것을 무시하는 독자는 전에 읽던 것을 읽는다.
        /// </remarks>
        internal string SubjectLost;

        /// <summary>
        /// 이것이 누구의 용어로 쓰였는가 — <c>this</c>, <c>arg:N</c>, <c>static</c>, 아니면 unknown.
        /// </summary>
        /// <remarks>
        /// 그것 없이는 조건이 주어 없는 문장이고, 그런 둘은 나란히 놓을 수 없다. 그것이 있으면 <c>this</c> 에 대한
        /// 피호출자의 조건을, 호출자가 그 메서드를 무엇에 대고 불렀는지 말하는 순간 호출자의 용어로 고쳐 쓸 수 있다.
        /// </remarks>
        internal string Context;

        internal int Offset;

        /// <summary>
        /// 게임이 도는 동안 좌변을 되읽을 수 있는 자리. 그런 자리가 있을 때.
        /// </summary>
        /// <remarks>
        /// 조건은 테스터가 마련해야 하는 것이고, 마련하는 일은 지금 무엇이 있는지 보는 데서 시작한다. 리포트는
        /// 존재해 온 내내 <c>MapMove.position == 0</c> 이라고 말해 왔지만 아무것도 <c>position</c> 을 들여다볼 수
        /// 없었고, 그래서 그런 줄 하나하나가 제 전제를 확인할 방법이 없는 규칙이었다.
        ///
        /// 대부분에 대해 null 이고 그것은 결함이 아니다: 호출이나 지역 변수에 대한 조건은 메서드가 도는 동안에만
        /// 존재하는 값에 대한 것이다. 근사하지 않고 센다.
        /// </remarks>
        internal WatchTarget Watch;

        public override string ToString()
        {
            return Left + " " + Operator + " " + Right;
        }
    }

    /// <summary>게임이 그것에 대해 하는 무언가.</summary>
    internal sealed class Outcome
    {
        internal string Kind;
        internal string Category;
        internal string Target;
        internal string Detail;

        /// <summary>
        /// 대상이 될 수 있었던 값들. 그것이 여러 자리에서 쓰인 지역 변수일 때.
        /// </summary>
        /// <remarks>
        /// <c>Target</c> 옆에 두지 그것을 대신하지 않는다. 대상은 원본이 그것을 부른 이름이고, 이것은 원본이 거기
        /// 넣은 것이며, 둘은 서로 다른 물음에 답한다.
        /// </remarks>
        internal System.Collections.Generic.List<string> TargetCandidates;

        /// <summary>
        /// 이것이 바꾼 것을 되읽을 수 있는 자리. 그것이 필드일 때.
        /// </summary>
        /// <remarks>
        /// 같은 필요의 나머지 절반이다. 조건은 무엇을 마련할지 말하고 효과는 그 뒤에 무엇을 확인할지 말하는데, 둘 다
        /// 누군가 그 값을 볼 수 있기 전까지는 돌릴 수 없다. 필드에 대한 효과는 존재하는 감시 대상 중 가장 확실한
        /// 것이기도 하다 — 무언가 거기에 쓴다는 것을 리포트가 이미 세워 두었으므로, 그저 거기 놓여 있을 뿐인 값이
        /// 아니다.
        /// </remarks>
        internal WatchTarget Watch;

        /// <summary>
        /// 값이 어디서 왔는가. 그것도 어딘가일 때.
        /// </summary>
        /// <remarks>
        /// <c>character.transform.position = battle2.transform.position</c> 은 객체 둘의 이름을 대고 그중 하나만이
        /// 바뀐 것이다. 바뀐 쪽만 감시하면 커서가 어디 있는지는 말해도 어디로 가고 있었는지는 영영 말하지 못하므로,
        /// "커서가 <c>battle2</c> 에 도착했다" — 명세 줄이 확인하는 것의 전부 — 에 답할 수 없다.
        ///
        /// 화면 녹화 옆에서 가장 중요해진다. 영상은 무언가가 어딘가에 도착하는 것을 볼 수 있어도 그 어딘가가
        /// <c>battle2</c> 라 불린다는 것은 알 수 없고, 아무도 감시하지 않는 목적지에는 비교할 위치가 없다.
        /// </remarks>
        internal WatchTarget WatchSource;

        /// <summary>
        /// 게임이 animator 에 건넨 이름. 그것이 코드에 적혀 있었을 때.
        /// </summary>
        /// <remarks>
        /// Unity 는 런타임에 상태의 이름을 돌려주지 않는다. <c>AnimatorStateInfo</c> 는 해시를 나르고 그것을 말로
        /// 바꿔 주는 것은 없으므로, animator 에 대한 판독은 상태가 바뀌었다고는 말해도 어느 상태로 바뀌었는지는
        /// 말하지 못한다 — 그리고 그것이 정확히 화면 녹화가 이미 주는 절반이고, 정확히 녹화가 줄 수 없는 절반이다.
        ///
        /// 다만 물어볼 수는 있다. <c>IsName</c> 은 현재 상태가 무엇으로 불리는지에 답하므로, 후보를 아는 판독은
        /// 그것들을 시험해 상태의 이름을 댈 수 있다. 후보는 코드 안에 있다: <c>SetTrigger("Death")</c> 가 하나를
        /// 적어 두었다.
        ///
        /// 트리거의 이름과 상태의 이름은 같은 것이 아니고, 하나를 다른 하나로 쓰는 게임들은 규칙이 아니라 관례를
        /// 따르는 것이다. 그래서 답은 <c>IsName</c> 이 예라고 할 때만 주어지고, 해시는 어느 쪽이든 그 옆에 적힌다.
        /// </remarks>
        internal string AnimatorName;

        internal int Offset;

        public override string ToString()
        {
            return Detail == null ? Kind + " " + Target : Kind + " " + Target + " " + Detail;
        }
    }

    /// <summary>이 경우의 조건 아래 이루어진 같은 어셈블리 안의 호출.</summary>
    internal sealed class CallEdge
    {
        internal string TargetId;
        internal string Target;

        /// <summary>
        /// 호출이 무엇에 대고, 무엇을 가지고 이루어졌는가.
        /// </summary>
        /// <remarks>
        /// 둘 다 <c>Raise</c> 를 부르는 버튼 둘은, 수신자가 각각 어느 필드에 대고 그것을 불렀는지 말하기 전까지 같은
        /// 엣지다. 피호출자의 조건이 호출자의 것으로 합성되지 못하게 막는 것도 같은 결핍이다 —
        /// <c>count &gt; 0</c> 은 누군가에 대한 것인데 엣지가 그 누구인지를 한 번도 말하지 않았다.
        /// </remarks>
        internal string Receiver;

        /// <summary>수신자가 누구의 객체였는가. 호출자 자신의 용어로.</summary>
        internal string ReceiverWhere;

        internal string Arguments;

        internal int Offset;
    }

    /// <summary>같은 경우에 닿는 또 하나의 갈래.</summary>
    internal sealed class Arrival
    {
        internal string Entry;
        internal string EntryId;
        internal string TriggerKind;
        internal List<string> CallPath;
    }

    /// <summary>나중에 자신을 부를 무언가에 걸린 메서드.</summary>
    internal sealed class Subscription
    {
        /// <summary>핸들러가 붙은 필드나 프로퍼티. 이름을 댈 수 있었을 때.</summary>
        internal string Channel;

        /// <summary>그 채널의 타입 — 같은 타입의 발행자가 닿을 수 있는 것.</summary>
        internal string ChannelType;

        /// <summary>그중 어느 멤버인지: 이벤트의 이름, 또는 필드의 이름.</summary>
        internal string Member;

        internal string Handler;
        internal string HandlerId;
        internal int Offset;
    }

    /// <summary>
    /// 입력 하나, 참이어야 했던 것, 그리고 바뀐 것.
    /// </summary>
    /// <remarks>
    /// 같은 키라도 그것을 다루는 분기마다 다른 variant 다. 맵의 한 자리에서의 방향키는 한 걸음 옆에서의 같은
    /// 키와 다른 데로 옮기고, 그 둘을 뭉갠 명세는 어느 쪽도 서술하지 못한다.
    /// </remarks>
    internal sealed class Variant
    {
        /// <summary>조건을 잘라내기 전까지 얼마나 써 나가는지.</summary>
        private const int WriteBudget = 40;

        internal string Method;
        internal string MethodId;

        /// <summary>
        /// 여기의 조건이 플레이어가 이 효과들에 닿는 경위의 전부인지.
        /// </summary>
        /// <remarks>
        /// 호출 경로를 따라 찾은 기록은 제 메서드의 조건을 나르는데, 그것은 애초에 그 호출이 이루어지려면 무엇이
        /// 참이어야 했는지에 대해 아무 말도 하지 않는다. 그것이 합성돼 들어오기 전까지 그 기록은 테스트를 쓸 수 있는
        /// 무엇이 아니라 가는 길의 한 걸음이고, 이것을 읽는 이에게 그 둘이 같아 보여서는 안 된다.
        /// </remarks>
        internal string RecordKind = "candidate";

        /// <summary>이 근거에 닿은 출발점인 Unity 진입점.</summary>
        internal string Entry;
        internal string EntryId;

        /// <summary>실행이 뿌리로 들어오는 방식: Unity 이벤트, 생명주기, 또는 코드 입력.</summary>
        internal string TriggerKind;

        /// <summary>진입점에서 이 메서드까지 따라온 같은 어셈블리 안의 모든 호출.</summary>
        internal readonly List<string> CallPath = new List<string>();

        /// <summary>이것이 구워지는 타입. 그것이 GameObject 가 나를 수 있는 것일 때.</summary>
        internal Mono.Cecil.TypeDefinition Owner;

        internal Condition When = Condition.Always;
        internal readonly List<InputRead> Inputs = new List<InputRead>();
        internal readonly List<Outcome> Outcomes = new List<Outcome>();
        internal readonly List<CallEdge> Calls = new List<CallEdge>();
        internal readonly List<Subscription> Handles = new List<Subscription>();

        /// <summary>
        /// 이 같은 경우에 닿는 다른 진입점들.
        /// </summary>
        /// <remarks>
        /// 여섯 자리에서 불리는 헬퍼 하나가 예전에는 같은 말을 하는 기록 여섯이었다. 첫 갈래가
        /// <see cref="Entry"/> 와 <see cref="CallPath"/> 를 그대로 쥐고 있어 이것을 읽는 쪽은 아무것도 바꾸지
        /// 않아도 되고, 나머지가 여기 있다.
        /// </remarks>
        internal readonly List<Arrival> AlsoReachedBy = new List<Arrival>();

        /// <summary>
        /// 이 메서드가 불린 것이 아니라 건네진 자리, 또는 -1.
        /// </summary>
        /// <remarks>
        /// 이것 바로 앞, <see cref="CallPath"/> 의 마지막 메서드 안의 오프셋이다. 건네진 본문을 그 주위의 효과들
        /// 옆에 순서대로 놓는 유일한 수단이다 — 그 메서드 안의 호출들은 이미 오프셋을 나르고, 이것이 없으면 그 둘
        /// 사이의 기다림은 갈 자리가 없다.
        /// </remarks>
        internal int HandedAt = -1;

        /// <summary>그 오프셋이 <see cref="CallPath"/> 의 어느 메서드에 속하는지, 또는 -1.</summary>
        /// <remarks>
        /// 오프셋이 여행할 수 있도록 말해 둔다. 이것이 없으면 그 숫자는 건네기가 마지막 걸음일 때만 뜻이 있었고,
        /// 그래서 평범한 호출이 뒤따르는 순간 버려졌다 — 기록 넷 중 하나만 그것을 지켰고, 샘플 게임의 모든
        /// 드래그 앤 드롭이 필요한 순서를 잃었다. 인덱스가 있으면 독자는 그 오프셋이 어느 본문 안의 오프셋인지 알고,
        /// 경로에서의 위치로 무엇을 추측할 필요가 없다.
        /// </remarks>
        internal int HandedIn = -1;

        /// <summary>
        /// 건네진 그 메서드를 무엇이 가져갔는가.
        /// </summary>
        /// <remarks>
        /// 술어가 어디로 갔는지가 무언가 그것을 기다리는지를 말한다. 판단하지 않고 이름만 댄다 —
        /// <c>UnityEngine.WaitUntil</c> 이라고 적어 두고, 기다림이 무엇을 뜻하는지는 독자가 알 몫이다.
        /// </remarks>
        internal string HandedTo;

        /// <summary>
        /// 이것이 두 번 이상 돌 때 제어가 되돌아오는 자리, 또는 -1.
        /// </summary>
        /// <remarks>
        /// 예전에는 루프가 읽기를 좌절시켰을 때만 말했고, 그래서 읽기가 좋아지는 딱 그만큼 사라졌다: 카운터의 이름을
        /// 댈 수 있게 되자 조건이 풀렸고, 걷기는 포기하지 않았으며, 엣지는 언급되지 않은 채로 갔다. 다시 한 바퀴
        /// 도는 것은 코드에 대한 사실이지 그것을 읽지 못한 실패가 아니므로, 다른 무엇을 읽을 수 있었든 없었든
        /// 말한다.
        ///
        /// 두 모양이 그것을 나르고 둘 다 같은 자리를 뜻한다. 제어가 되돌아오는 *대상* 블록은 제 오프셋을 말하고,
        /// *되돌아가는* 블록은 어디로 뛰는지를 말한다. 그 사이에 앉아 있기만 한 것은 아무 말도 하지 않는다 — 그저
        /// 속해 있을 뿐인 루프의 이름을 대는 일은 이것이 묻지 않는 그래프 물음을 요구한다.
        /// </remarks>
        internal int LoopsBackTo = -1;

        /// <summary>이 근거를 전수로 취급해서는 안 되는 구체적인 이유들.</summary>
        internal readonly List<string> Gaps = new List<string>();

        /// <summary>여기 오는 길의 일부를 읽을 수 없었을 때 참.</summary>
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
