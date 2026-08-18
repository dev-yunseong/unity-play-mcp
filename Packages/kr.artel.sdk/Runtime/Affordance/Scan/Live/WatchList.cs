using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Artel.Affordances.Live
{
    /// <summary>게임이 도는 동안 읽어 달라고 근거가 청하는 멤버 하나.</summary>
    internal sealed class Watched
    {
        internal string Declaring;
        internal string Member;

        /// <summary>아무것도 읽기 전에, 분석이 그 값이 무엇이라고 말했는지.</summary>
        /// <remarks>
        /// 값을 그것이 출력되는 모습이 아니라 그것인 바로 보고할 수 있도록 나른다. bool 은 필드에서 <c>True</c> 로 나오고
        /// int 는 <c>1</c> 로 나오는데, 리포트는 그 둘이 닮아 보이지 않게 하는 법을 오래 배워 왔다.
        /// </remarks>
        internal string Type;

        internal bool Static;

        /// <summary>
        /// 컴파일러가 이름을 바꿨을 때, 리플렉션 말고 나머지 전부가 이것을 부르는 이름.
        /// </summary>
        /// <remarks>
        /// 자동 프로퍼티는 <c>&lt;Instance&gt;k__BackingField</c> 라 불리는 필드다. 그 이름이 그것을 찾아 주고 다른 무엇도
        /// 그것을 쓰지 않는다 — 근거는 <c>StageDataSingleton.Instance</c> 라고 말한다 — 그래서 필드 이름을 댄 판독은 그것이
        /// 답하는 조건에 이어지지 않는다.
        /// </remarks>
        internal string Property;

        /// <summary>필드 자체가 값이 아닐 때, 그 필드에서 무엇을 읽었는지.</summary>
        internal string Via;

        /// <summary>리플렉션에 물어본 뒤의 필드 그 자체. 그 전까지는 null.</summary>
        internal FieldInfo Field;

        /// <summary>그것이 사는 타입. 그것을 나르는 인스턴스를 찾기 위한 것.</summary>
        internal Type Owner;

        /// <summary>
        /// 조건이나 효과가 이 멤버의 이름을 댔는지, 아니면 그저 읽을 수 있을 뿐인지.
        /// </summary>
        /// <remarks>
        /// 둘 다 읽히고 둘 다 나간다. 차이는 독자가 그것으로 무엇을 해야 하는가다. 근거가 청한 멤버는 어떤 명세 줄이 걸려 있는
        /// 것이고, 그 줄을 확인하는 독자는 정확히 그것들을 원한다. 나머지는 아직 아무도 쓰지 않은 줄을 위해 나른다 — 애초에 왜
        /// 나르는지는 <see cref="Readable"/> 참고 — 그리고 그 둘을 똑같이 다루는 독자는 전제를 읽으려던 자리에서 게임의 상태
        /// 전체를 읽게 된다.
        ///
        /// watch list 자신이 쥔 것에 대해서는 전부 참이다. 그 목록은 청해진 것 말고는 아무것도 아니기 때문이다.
        /// </remarks>
        internal bool Asked = true;

        /// <summary>이 멤버를 다른 어떤 것과도 구별해 부르는 이름.</summary>
        internal string Key => Declaring + "::" + Member;
    }

    /// <summary>
    /// 게임이 도는 동안 무엇을 볼 것인가. 분석이 알아낸 그대로.
    /// </summary>
    /// <remarks>
    /// 다른 SDK 는 게임더러 제 필드를 표시하라고 청했다. 그것은 결정을 두 겹으로 엉뚱한 자리에 두었다: 아무도 표시할 생각을
    /// 못 한 필드는 리포트가 아무리 그것에 걸려 있어도 보이지 않고, 탈출구인 — 직렬화된 필드를 전부 읽기 — 쪽은 idle
    /// 애니메이션을 상태 변화처럼 보이게 만드는데, 그래서 그것은 라이브 경로에서 쓰인 적이 없다.
    ///
    /// 여기서는 표시할 것이 없다. 분석은 모든 조건과 모든 효과 뒤의 명령어를 이미 읽었으므로, 감시할 값이 있는 멤버는 어차피
    /// 하고 있던 일에서 떨어져 나왔고, 목록은 게임의 길이가 아니라 근거가 요구하는 만큼 정확히 길다.
    ///
    /// 한 번 읽는다. 이름을 필드로 해석하는 일은 리플렉션이고, 폴링마다 멤버 백 개에 리플렉션을 거는 것은 그것들을 읽는
    /// 것보다 비싸다.
    /// </remarks>
    internal static class WatchList
    {
        private const string ResourceName = "kr.artel.affordance.watch";

        private static List<Watched> _resolved;
        private static List<string> _animatorNames;
        private static Dictionary<string, Offer> _offers;

        /// <summary>
        /// 플레이어가 어떤 타입을 움직이게 만들 수 있는 방법들. 그것이 씬 안의 무엇에 붙어 있을 때.
        /// </summary>
        /// <remarks>
        /// 판독은 게임이 무엇을 쥐고 있는지를 말하고, 이것이 없으면 다음에 그것에 무엇을 할 수 있는지는 아무것도 말하지 않는다.
        /// 스캔은 버튼을 스스로 찾는다 — persistent call 은 누구나 볼 수 있는 배선이다 — 그리고 이것들이 스캔이 볼 수 없는
        /// 둘이다: 여기서 어떤 키가 뜻을 가지는가, 그리고 어떤 객체가 포인터에 답하는가. 둘 다 컴파일된 코드 안의 리터럴과
        /// 메서드 이름이다.
        ///
        /// 씬이 아니라 타입으로 키를 잡는다. 키를 뜻 있게 만드는 것은 그것을 읽는 무언가가 지금 화면에 있다는 사실이기
        /// 때문이다. 판독은 거기 있는 객체들을 걷고 각각의 컴포넌트에 물으므로, 그 타입이 없는 화면은 누가 여기가 어느 화면인지
        /// 알아내지 않아도 아무것도 내놓지 않는다.
        /// </remarks>
        internal sealed class Offer
        {
            internal readonly List<string> Keys = new List<string>();
            internal readonly List<string> Pointers = new List<string>();
        }

        /// <summary>이 타입이 무엇에 답하는지, 또는 null.</summary>
        internal static Offer OfferedBy(string declaring)
        {
            All();

            if (declaring == null || _offers == null)
            {
                return null;
            }

            return _offers.TryGetValue(declaring, out var offer) ? offer : null;
        }

        /// <summary>
        /// 게임 코드가 animator 에 건네는 모든 이름.
        /// </summary>
        /// <remarks>
        /// Unity 는 animator 가 있는 상태를 해시로 돌려주고 그것을 말로 바꿔 주는 것은 없으므로, 판독은 상태가 바뀌었다고만
        /// 말하고 무엇으로 바뀌었는지는 말하지 못한다 — 화면 녹화가 이미 보여 주는 절반이고, 녹화가 줄 수 없는 절반은 아니다.
        /// <c>IsName</c> 은 그 물음에 거꾸로 답하므로, 후보를 아는 판독은 물어서 상태의 이름을 댈 수 있다.
        /// </remarks>
        internal static IReadOnlyList<string> AnimatorNames
        {
            get
            {
                All();
                return _animatorNames;
            }
        }

        /// <summary>분석은 이름을 댔으나 리플렉션이 찾지 못한 것이 몇인지.</summary>
        /// <remarks>
        /// 난독화가 흔한 원인이다: 목록은 코드가 컴파일될 때 가지고 있던 이름을 쥐고 있고 어셈블리는 다른 이름으로 나간다.
        /// 건너뛰지 않고 말한다. 이백 개 멤버 중 열하나를 아무 설명 없이 보고하는 감시자는 상태가 거의 없는 게임처럼 보이기
        /// 때문이다.
        /// </remarks>
        internal static int Unresolved { get; private set; }

        /// <summary>분석이 읽을 자리를 찾지 못한 값이 몇인지. 어셈블리를 통틀어 합한 것.</summary>
        internal static int Unwatchable { get; private set; }

        internal static IReadOnlyList<Watched> All()
        {
            if (_resolved != null)
            {
                return _resolved;
            }

            _resolved = new List<Watched>();
            _animatorNames = new List<string>();
            _offers = new Dictionary<string, Offer>(StringComparer.Ordinal);
            Unresolved = 0;
            Unwatchable = 0;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Read(assembly, _resolved);
                }
                catch (Exception)
                {
                    // 동적 어셈블리이거나 리소스가 열리지 않는 어셈블리다. 건너뛰면 목록이 짧아지지 틀리지는 않는다.
                }
            }

            return _resolved;
        }

        internal static void Forget()
        {
            _resolved = null;
        }

        private static void Read(Assembly assembly, List<Watched> into)
        {
            using (var packed = assembly.GetManifestResourceStream(ResourceName))
            {
                if (packed == null)
                {
                    return;
                }

                string text;

                using (var expanded = new DeflateStream(packed, CompressionMode.Decompress))
                using (var reader = new StreamReader(expanded, Encoding.UTF8))
                {
                    text = reader.ReadToEnd();
                }

                Unwatchable += Number(text, "\"unwatchable\":");
                Names(text, _animatorNames);

                foreach (var entry in Entries(text, "watch"))
                {
                    Resolve(assembly, entry, into);
                }

                foreach (var entry in Entries(text, "inputs"))
                {
                    Offered(entry);
                }
            }
        }

        /// <summary>
        /// JSON 파서 없이 <c>watch</c> 배열의 각 객체를 찾는다.
        /// </summary>
        /// <remarks>
        /// 문서는 그것을 읽는 바로 그 패키지가 쓰고, 필드 모양은 하나이며, 배열 원소 안에 중첩이 없고, 중괄호를 담은 문자열도
        /// 없다 — 타입 이름은 그것을 담을 수 없다. 그것 하나 때문에 런타임 어셈블리에 파서를 들이는 일은 이것을 싣고 나가는 모든
        /// 게임에 얹히는 무게이고, writer 는 오십 줄 옆에 있다.
        /// </remarks>
        private static IEnumerable<string> Entries(string text, string array)
        {
            var start = text.IndexOf("\"" + array + "\":[", StringComparison.Ordinal);

            if (start < 0)
            {
                yield break;
            }

            var index = start;

            while (true)
            {
                var open = text.IndexOf('{', index);

                if (open < 0)
                {
                    yield break;
                }

                var close = text.IndexOf('}', open);

                if (close < 0)
                {
                    yield break;
                }

                yield return text.Substring(open + 1, close - open - 1);
                index = close + 1;

                // 다음 배열까지 달려가지 않고 이 배열 자신의 끝에서 멈춘다. 항목 뒤에 오는 것으로 안다 — 쉼표이거나 닫는 대괄호이지
                // 그 밖의 것은 아니다 — 대괄호를 세는 방식은 필드가 제 대괄호를 나르는 제네릭 타입 이름을 쥐는 순간 틀리기 때문이다.
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index >= text.Length || text[index] == ']')
                {
                    yield break;
                }
            }
        }

        private static void Resolve(Assembly assembly, string entry, List<Watched> into)
        {
            var declaring = Text(entry, "\"declaring\":\"");
            var member = Text(entry, "\"member\":\"");

            if (declaring == null || member == null)
            {
                return;
            }

            var owner = assembly.GetType(declaring, false);

            if (owner == null)
            {
                Unresolved++;
                return;
            }

            // private 인 것과 상속된 것 둘 다. 게임의 상태는 대개 private 이고, 기반 클래스가 선언한 필드에 대한 조건도 여전히 이
            // 컴포넌트에 대한 조건이다.
            var field = owner.GetField(
                member,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

            if (field == null)
            {
                Unresolved++;
                return;
            }

            into.Add(new Watched
            {
                Declaring = declaring,
                Member = member,
                Property = Text(entry, "\"property\":\""),
                Via = Walkable(field.FieldType, Text(entry, "\"via\":\"")),
                Type = Text(entry, "\"type\":\""),
                Static = entry.Contains("\"static\":true"),
                Field = field,
                Owner = owner
            });
        }

        /// <summary>
        /// 필드가 쥔 것에서 실제로 걸어갈 수 있을 때의 경로 — 아니면 없음.
        /// </summary>
        /// <remarks>
        /// 판독마다 묻지 않고 여기서 한 번 결정한다. 타입이 어떤 멤버를 가졌는지는 게임이 도는 동안 바뀌지 않고, 옆의 이름과
        /// 다르게 답하는 판독은 둘 중 어느 답보다도 나쁘기 때문이다.
        ///
        /// 걸어지지 않을 때는 보고하지 않고 떨어뜨린다. 근거는 필드로 가는 길에 <c>transform</c> 을 벗겨 내므로,
        /// <c>MapMove.battle1.transform.position</c> 으로 쓰인 목적지가 여기에는 <c>GameObject</c> 위의 <c>position</c> 으로
        /// 도착하는데 그런 멤버는 없다 — 정작 그 줄이 원하는 좌표는 이미 참조가 쓰이는 방식 그 자체다. 그것을 읽을 수 없다고 한
        /// 탓에 열세 줄의 목적지를 잃었다. 필드가 스스로 답하게 두는 것이 경로라는 것이 있기 전에 이것이 하던 일이다.
        ///
        /// 선언된 타입에 대고 판단한다. 더 파생된 것을 쥔 필드는 이것이 볼 수 있는 것보다 많이 내놓을 수 있고, 그 값은 틀린 값이
        /// 아니라 가지 않은 경로 하나다.
        /// </remarks>
        private static string Walkable(Type from, string path)
        {
            if (path == null || from == null)
            {
                return null;
            }

            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var step in path.Split('.'))
            {
                var field = from.GetField(step, Flags);

                if (field != null)
                {
                    from = field.FieldType;
                    continue;
                }

                var property = from.GetProperty(step, Flags);

                if (property == null || !property.CanRead ||
                    property.GetIndexParameters().Length != 0)
                {
                    return null;
                }

                from = property.PropertyType;
            }

            return path;
        }

        /// <summary><c>inputs</c> 배열의 한 항목에서 꺼낸, 한 타입이 내놓는 입력들.</summary>
        private static void Offered(string entry)
        {
            var declaring = Text(entry, "\"declaring\":\"");

            if (declaring == null)
            {
                return;
            }

            if (!_offers.TryGetValue(declaring, out var offer))
            {
                offer = new Offer();
                _offers[declaring] = offer;
            }

            Listed(entry, "\"keys\":[", offer.Keys);
            Listed(entry, "\"pointers\":[", offer.Pointers);
        }

        /// <summary>
        /// 한 항목 안에 앉은 평평한 문자열 배열. 그 배열 자신의 끝까지.
        /// </summary>
        /// <remarks>
        /// 문서 끝까지 달리지 않고 배열로 가둔다. <see cref="Names"/> 가 그렇게 할 수 있는 것은 그것이 제 이름의 유일한 배열을
        /// 읽기 때문이다. 이런 것 둘이 한 항목 안에 나란히 앉아 있으므로, 그러지 않으면 첫째가 둘째를 삼킨다.
        /// </remarks>
        private static void Listed(string entry, string key, List<string> into)
        {
            var start = entry.IndexOf(key, StringComparison.Ordinal);

            if (start < 0)
            {
                return;
            }

            var index = start + key.Length;
            var end = entry.IndexOf(']', index);

            if (end < 0)
            {
                return;
            }

            while (index < end)
            {
                var open = entry.IndexOf('"', index);

                if (open < 0 || open > end)
                {
                    return;
                }

                var close = entry.IndexOf('"', open + 1);

                if (close < 0 || close > end)
                {
                    return;
                }

                var said = entry.Substring(open + 1, close - open - 1);

                if (said.Length > 0 && !into.Contains(said))
                {
                    into.Add(said);
                }

                index = close + 1;
            }
        }

        /// <summary>writer 가 animator 이름을 넣어 둔 평평한 문자열 배열을 읽는다.</summary>
        private static void Names(string text, List<string> into)
        {
            const string key = "\"animatorNames\":[";

            var start = text.IndexOf(key, StringComparison.Ordinal);

            if (start < 0)
            {
                return;
            }

            // 키 자신의 닫는 따옴표 뒤에서 시작하지 그 자리에서 시작하지 않는다. 키에서 시작하면 처음 찾은 따옴표 쌍이 키 자신의
            // 것이고 그 이름이 첫 항목이 됐다 — 실측하니 목록이 `:[` 와 `,` 로 나왔고, 그래서 모든 상태가 이름 없이 남았으며,
            // 이름 없는 상태는 상태를 다르게 이름 짓는 게임이 만들어내는 것이기도 해서 그 이유가 보이지 않았다.
            var index = start + key.Length - 1;
            var end = text.IndexOf(']', index);

            while (index < end)
            {
                var open = text.IndexOf('"', index);

                if (open < 0 || open > end)
                {
                    return;
                }

                var close = text.IndexOf('"', open + 1);

                if (close < 0 || close > end)
                {
                    return;
                }

                var name = text.Substring(open + 1, close - open - 1);

                if (name.Length > 0 && !into.Contains(name))
                {
                    into.Add(name);
                }

                index = close + 1;
            }
        }

        private static string Text(string entry, string key)
        {
            var at = entry.IndexOf(key, StringComparison.Ordinal);

            if (at < 0)
            {
                return null;
            }

            var from = at + key.Length;
            var to = entry.IndexOf('"', from);
            return to < 0 ? null : entry.Substring(from, to - from);
        }

        private static int Number(string text, string key)
        {
            var at = text.IndexOf(key, StringComparison.Ordinal);

            if (at < 0)
            {
                return 0;
            }

            var from = at + key.Length;
            var to = from;

            while (to < text.Length && (char.IsDigit(text[to]) || text[to] == '-'))
            {
                to++;
            }

            return int.TryParse(text.Substring(from, to - from), out var value) ? value : 0;
        }
    }
}
