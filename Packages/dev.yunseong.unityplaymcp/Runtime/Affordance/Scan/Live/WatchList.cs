using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>게임이 도는 동안 읽을 멤버 하나.</summary>
    internal sealed class Watched
    {
        internal string Declaring;
        internal string Member;

        /// <summary>분석이 기록한 값의 타입 이름.</summary>
        /// <remarks>
        /// 출력 모양이 아니라 타입으로 값을 보고하기 위해 싣는다. bool <c>True</c> 와 int <c>1</c> 을 구분해야 한다.
        /// </remarks>
        internal string Type;

        internal bool Static;

        /// <summary>
        /// 컴파일러가 바꾼 이름일 때 원래 프로퍼티 이름.
        /// </summary>
        /// <remarks>
        /// 자동 프로퍼티의 필드 이름은 <c>&lt;Instance&gt;k__BackingField</c> 라, <c>evidence</c> 의
        /// <c>StageDataSingleton.Instance</c> 와 이으려면 프로퍼티 이름이 필요하다.
        /// </remarks>
        internal string Property;

        /// <summary>필드 자체가 값이 아닐 때 필드에서 따라갈 경로.</summary>
        internal string Via;

        /// <summary>리플렉션으로 찾은 필드.</summary>
        /// <remarks>
        /// <c>null</c> 이면 컴포넌트에서 <see cref="Member"/> 프로퍼티를 읽는다. <see cref="Drawn"/> 이 만드는 멤버가 그렇고,
        /// 모두 인스턴스 멤버라 static 목록에는 없다.
        /// </remarks>
        internal FieldInfo Field;

        /// <summary>멤버를 선언한 타입. 인스턴스를 찾는 데 쓴다.</summary>
        internal Type Owner;

        /// <summary>
        /// 조건이나 효과가 이 멤버를 이름 댔는지, 읽을 수 있어서 실었을 뿐인지.
        /// </summary>
        /// <remarks>
        /// 둘 다 pulse 에 실린다. 읽는 쪽이 조건을 확인할 때 게임 상태 전체가 아니라 청한 멤버만 볼 수 있게 구분한다
        /// (<see cref="Readable"/> 참고). watch list 가 만든 멤버는 모두 true 다.
        /// </remarks>
        internal bool Asked = true;

        /// <summary>멤버를 유일하게 가리키는 key.</summary>
        internal string Key => Declaring + "::" + Member;
    }

    /// <summary>
    /// 분석이 어셈블리 리소스에 구워 둔 감시 대상 멤버 목록.
    /// </summary>
    /// <remarks>
    /// 게임이 필드에 표시를 달 필요가 없다. 분석이 조건과 효과에서 감시할 멤버를 이미 뽑아 두었다.
    ///
    /// 이름을 필드로 해석하는 리플렉션은 비싸므로 한 번만 하고 결과를 기억한다.
    /// </remarks>
    internal static class WatchList
    {
        private const string ResourceName = "dev.yunseong.unityplaymcp.affordance.watch";

        private static List<Watched> _resolved;
        private static List<string> _animatorNames;
        private static Dictionary<string, Offer> _offers;

        /// <summary>
        /// 한 타입이 씬에 있을 때 플레이어가 줄 수 있는 입력: 키와 포인터.
        /// </summary>
        /// <remarks>
        /// 버튼의 persistent call 은 스캔이 씬에서 찾지만, 키와 포인터 handler 는 컴파일된 코드에만 있어 분석이 구워 둔다.
        ///
        /// 씬이 아니라 타입으로 묶는다. pulse 가 지금 있는 객체의 컴포넌트를 물으므로 그 타입이 없는 화면에서는 나오지 않는다.
        /// </remarks>
        internal sealed class Offer
        {
            internal readonly List<KeyOffer> Keys = new List<KeyOffer>();
            internal readonly List<string> Pointers = new List<string>();
        }

        /// <summary>키 하나와 그 키를 누를 때의 효과.</summary>
        /// <remarks>
        /// <see cref="Does"/> 가 비어 있으면 "아무 일도 안 한다" 가 아니라 "분석이 못 읽었다" 이다. 출력에서도 구분한다.
        /// </remarks>
        internal sealed class KeyOffer
        {
            internal string Key;
            internal readonly List<string> Does = new List<string>();
        }

        /// <summary>이 타입이 받는 입력, 또는 null.</summary>
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
        /// Unity 는 animator 상태를 해시로만 돌려준다. 후보 이름을 알면 <c>IsName</c> 으로 물어 상태 이름을 알아낼 수 있다.
        /// </remarks>
        internal static IReadOnlyList<string> AnimatorNames
        {
            get
            {
                All();
                return _animatorNames;
            }
        }

        /// <summary>분석이 이름 댔으나 리플렉션이 찾지 못한 멤버 수.</summary>
        /// <remarks>
        /// 흔한 원인은 난독화다. 세어 두지 않으면 멤버가 빠진 것이 상태가 적은 게임처럼 보인다.
        /// </remarks>
        internal static int Unresolved { get; private set; }

        /// <summary>분석이 읽을 위치를 찾지 못한 값의 수. 모든 어셈블리의 합이다.</summary>
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
                    // 동적 어셈블리이거나 리소스가 열리지 않는 어셈블리다. 건너뛰어도 목록이 틀리지는 않는다.
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
        /// 문서는 이 패키지의 writer 가 쓰며, 배열 원소에 중첩이 없고 중괄호를 담은 문자열도 없다. 런타임 어셈블리에 파서
        /// 의존성을 들이지 않으려고 직접 찾는다.
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

                // 항목 뒤 문자가 `]` 이면 이 배열이 끝난 것이다. 제네릭 타입 이름에 대괄호가 있어 대괄호를 세지 않는다.
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

            // 게임 상태는 대개 private 이고 기반 클래스 필드도 이 컴포넌트의 상태라 둘 다 찾는다.
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
        /// 필드 타입에서 <paramref name="path"/> 를 따라갈 수 있으면 그 경로를, 아니면 null 을 돌려준다.
        /// </summary>
        /// <remarks>
        /// 타입의 멤버는 실행 중에 바뀌지 않으므로 pulse 마다가 아니라 여기서 한 번 판단한다.
        ///
        /// 따라갈 수 없으면 오류로 보고하지 않고 경로만 버려 필드 값을 그대로 읽는다. <c>evidence</c> 는 <c>transform</c> 을
        /// 벗겨 내므로 <c>MapMove.battle1.transform.position</c> 이 <c>GameObject</c> 의 <c>position</c> 으로 와서 경로가
        /// 성립하지 않는다.
        ///
        /// 선언 타입으로 판단하므로 더 파생된 인스턴스의 멤버 경로는 놓칠 수 있다.
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

        /// <summary><c>inputs</c> 배열의 한 항목에서 한 타입의 입력을 읽어 <c>_offers</c> 에 넣는다.</summary>
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

            Keyed(entry, offer.Keys);
            Listed(entry, "\"pointers\":[", offer.Pointers);
        }

        /// <summary>
        /// 키 배열을 읽는다. 각 항목은 <c>키\u0001효과\u0001효과…</c> 다.
        /// </summary>
        /// <remarks>
        /// <see cref="Entries"/> 는 항목의 끝을 첫 <c>}</c> 로 찾으므로, 키를 객체로 만들면 항목이 첫 키에서 잘린다.
        /// 그래서 문자열 하나에 구분자로 담는다.
        ///
        /// 구분자가 없는 항목은 키 이름만 담던 옛 형식이며 그대로 읽는다.
        /// </remarks>
        private static void Keyed(string entry, List<KeyOffer> into)
        {
            var said = new List<string>();
            Listed(entry, "\"keys\":[", said);

            foreach (var one in said)
            {
                // 이 파일의 파서는 JSON 이스케이프를 풀지 않는다. 구분자만 예외로 여기서 푼다.
                var parts = one.Replace("\\u0001", "\u0001").Split('\u0001');
                var offer = new KeyOffer { Key = parts[0] };

                for (var at = 1; at < parts.Length; at++)
                {
                    offer.Does.Add(parts[at]);
                }

                into.Add(offer);
            }
        }

        /// <summary>
        /// 한 항목 안의 평평한 문자열 배열을 읽는다.
        /// </summary>
        /// <remarks>
        /// 한 항목에 이런 배열이 둘(<c>keys</c>, <c>pointers</c>) 있으므로 배열의 <c>]</c> 에서 멈춰야 첫째가 둘째를 읽지 않는다.
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

            // 키 앞에서 시작하면 키 자신의 따옴표 쌍이 첫 항목으로 잡히므로 `[` 에서 시작한다.
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
