using System;
using System.Collections.Generic;
using System.Reflection;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 근거가 청한 것 너머로, 컴포넌트에서 되읽을 수 있는 모든 것.
    /// </summary>
    /// <remarks>
    /// watch list 는 조건과 효과가 이름 댄 것이고, 이미 가진 명세를 확인하는 데는 그것이 정확히 맞는 목록이다 — 게임의
    /// 길이가 아니라 근거가 요구하는 길이만큼이다.
    ///
    /// 아직 갖지 못한 명세에 대해서는 틀린 목록이다. 분석은 놓치는 것이 있다: 에이전트 자신의 산출물 진술이 해결되지 않은
    /// 대상 118 건과 한 번도 관측하지 못한 런타임 인스턴스 52 건을 세고, 시나리오 170 개 중 95 개가 <c>review</c> 에
    /// 앉아 있으며, 트리거 26 개는 아예 닿지 못했다. 그것들이 나중에 사람이 손으로 쓸 줄이고, 그런 줄이 걸려 있는 필드는
    /// 바로 어떤 조건도 언급하지 않은 필드다 — 언급했다면 분석이 그 줄을 스스로 찾아냈을 것이다.
    ///
    /// 그래서 여기서 묻는 것은 "근거가 이것을 원하는가" 가 아니라 "이것을 읽을 수 있는가" 다. 두 실수의 값은 같지 않다.
    /// 너무 좁게 감시하면 그 값을 그냥 얻을 수 없고, 얻는 유일한 길은 게임을 다시 컴파일하는 것인데 — 그것이 이 패키지가
    /// 요구하지 않으려고 존재하는 바로 그것이다. 너무 넓게 감시하면 읽는 시간과 트래픽이 드는데, 둘 다 우리가 조절할 수
    /// 있다. 여기서 되돌릴 수 없는 실수와 되돌릴 수 있는 실수 사이라면 되돌릴 수 있는 쪽을 택한다.
    ///
    /// 여전히 읽을 수 없는 것은 이 무엇으로도 달라지지 않는다: 지역 변수나 매개변수는 제 메서드가 도는 동안에만 존재하고,
    /// 아무리 넓혀도 이미 끝난 프레임 안으로는 손이 닿지 않는다.
    /// </remarks>
    internal static class Readable
    {
        /// <summary>
        /// 그 필드가 게임 자신의 것이 아닌 어셈블리들.
        /// </summary>
        /// <remarks>
        /// 분석이 어떤 어셈블리를 읽을지 고르는 방식과 똑같이, 무엇을 취할지가 아니라 무엇을 건너뛸지로 이름 붙인다.
        /// <c>Image</c>, <c>TMP_Text</c>, <c>EventTrigger</c> 의 private 필드를 전부 읽으면 게임 자신의 상태가 아무도 청하지
        /// 않은 레이아웃 값 수백 개 아래 파묻히고, 그 값들은 어떤 명세도 언급하지 않는 이유로 바뀐다 — 그것은 아무것도 아닌
        /// 것을 위해 열어 둔 게이트다.
        ///
        /// 이름 경계에서 맞추므로 <c>Unity</c> 는 <c>Unity.TextMeshPro</c> 를 덮고 그저 그 글자로 시작하기만 하는 어셈블리는
        /// 건드리지 않는다.
        /// </remarks>
        private static readonly string[] NotTheGames =
        {
            "UnityEngine", "UnityEditor", "Unity", "Artel", "System", "mscorlib", "netstandard",
            "nunit", "Newtonsoft", "Mono", "TMPro"
        };

        /// <summary>
        /// 전부 버리고 다시 알아내기 전까지 타입을 몇 개나 기억하는지.
        /// </summary>
        /// <remarks>
        /// <see cref="Worth"/> 가 하는 것과 같은 거래다. 한 시간 동안 어셈블리를 로드하는 게임은 그러지 않으면 여태 본
        /// 타입마다 줄 하나씩을 늘린다. 전부 버리는 값은 리플렉션 한 바퀴이고 틀린 답을 줄 수는 없다.
        /// </remarks>
        private const int MaxRemembered = 2048;

        private const string BackingPrefix = "<";
        private const string BackingSuffix = ">k__BackingField";

        private static readonly Dictionary<Type, List<Watched>> Answered =
            new Dictionary<Type, List<Watched>>();

        /// <summary>
        /// 이 컴포넌트에서 읽을 멤버들: 근거가 이름 댄 것과, 그 밖에 거기 있는 것.
        /// </summary>
        /// <param name="type">읽고 있는 객체 위의 구체 컴포넌트 타입.</param>
        /// <param name="named">watch list 가 그것에 대해 이미 쥐고 있는 것, 또는 null.</param>
        internal static List<Watched> On(Type type, List<Watched> named)
        {
            if (type == null)
            {
                return named;
            }

            if (Answered.TryGetValue(type, out var already))
            {
                return already;
            }

            if (Answered.Count >= MaxRemembered)
            {
                Answered.Clear();
            }

            var answer = Ask(type, named);
            Answered[type] = answer;
            return answer;
        }

        private static List<Watched> Ask(Type type, List<Watched> named)
        {
            var members = new List<Watched>();
            var taken = new HashSet<string>(StringComparer.Ordinal);

            if (named != null)
            {
                foreach (var member in named)
                {
                    members.Add(member);

                    if (member.Field != null)
                    {
                        taken.Add(member.Field.Name);
                    }
                }
            }

            if (!TheGames(type))
            {
                return members;
            }

            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo[] fields;

            try
            {
                // 여기 선언된 것과 상속된 것 둘 다. 제 기반 클래스에 상태를 두는 behaviour 는 게임 코드의 평범한 모양이고, 잎만
                // 읽으면 그 서브클래스에 아무것도 없다고 보고하게 된다.
                fields = type.GetFields(Flags);
            }
            catch (Exception)
            {
                // 리플렉션이 열지 못하는 타입은 컴포넌트 하나이지, 객체를 잃을 이유가 아니다.
                return members;
            }

            foreach (var field in fields)
            {
                if (taken.Contains(field.Name) || Skip(field))
                {
                    continue;
                }

                members.Add(new Watched
                {
                    Declaring = field.DeclaringType == null ? type.FullName : field.DeclaringType.FullName,
                    Member = field.Name,
                    Property = Spoken(field.Name),
                    Type = field.FieldType.FullName,
                    Static = false,
                    Field = field,
                    Owner = type,
                    Asked = false
                });
            }

            return members;
        }

        /// <summary>
        /// 아무것도 답하지 않으면서 요동만 더할 필드들.
        /// </summary>
        /// <remarks>
        /// 델리게이트 필드는 구독자 목록이다. 그것이 null 이 아니라는 것은 핸들러가 붙어 있다는 말이지 게임이 무엇을 하고
        /// 있는지에 대한 말이 아니고, 무언가 구독할 때마다 그 정체가 바뀐다 — 어떤 명세도 언급하지 않는 이유로 움직이는 값이
        /// 바로 게이트가 막으려고 존재하는 그것이다.
        /// </remarks>
        private static bool Skip(FieldInfo field)
        {
            if (field.IsStatic || field.IsLiteral)
            {
                return true;
            }

            var type = field.FieldType;

            return typeof(Delegate).IsAssignableFrom(type);
        }

        /// <summary>
        /// 리플렉션 말고 나머지 전부가 이 필드를 부르는 이름.
        /// </summary>
        /// <remarks>
        /// watch list 가 건네받은 멤버들에 대해 이미 갖는 것과 같은 필요다: 자동 프로퍼티는
        /// <c>&lt;Instance&gt;k__BackingField</c> 라 불리는 필드이고, 그렇게 이름 붙인 판독은 다른 누가 쓴 무엇에도 이어지지
        /// 않는다. 둘이 같을 때는 null 이므로, 판독은 두 번째 이름이 있을 때만 그것을 나른다.
        /// </remarks>
        private static string Spoken(string name)
        {
            if (!name.StartsWith(BackingPrefix, StringComparison.Ordinal) ||
                !name.EndsWith(BackingSuffix, StringComparison.Ordinal))
            {
                return null;
            }

            var length = name.Length - BackingPrefix.Length - BackingSuffix.Length;

            return length <= 0 ? null : name.Substring(BackingPrefix.Length, length);
        }

        private static bool TheGames(Type type)
        {
            string assembly;

            try
            {
                assembly = type.Assembly.GetName().Name;
            }
            catch (Exception)
            {
                return false;
            }

            if (string.IsNullOrEmpty(assembly))
            {
                return false;
            }

            foreach (var prefix in NotTheGames)
            {
                if (!assembly.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (assembly.Length == prefix.Length || assembly[prefix.Length] == '.')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
