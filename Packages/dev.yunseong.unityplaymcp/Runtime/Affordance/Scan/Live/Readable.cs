using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// <c>evidence</c> 가 청한 멤버에 더해, 컴포넌트에서 읽을 수 있는 필드 전부.
    /// </summary>
    /// <remarks>
    /// watch list 는 조건과 효과가 이름 댄 멤버뿐이라, 분석이 놓친 조건에 필요한 필드는 빠진다. 좁게 감시하면 그 값을 얻으려고
    /// 게임을 다시 컴파일해야 하지만, 넓게 감시하는 비용(읽기 시간, 트래픽)은 조절할 수 있으므로 넓게 읽는다.
    ///
    /// 지역 변수와 매개변수는 읽을 수 없다.
    /// </remarks>
    internal static class Readable
    {
        /// <summary>
        /// 필드를 읽지 않을 게임 밖 어셈블리.
        /// </summary>
        /// <remarks>
        /// 분석과 같이 제외 목록으로 둔다. <c>Image</c>, <c>TMP_Text</c> 등의 private 필드까지 읽으면 게임 상태가 레이아웃
        /// 값 수백 개에 묻히고, 그 값들이 바뀔 때마다 변화로 판정된다.
        ///
        /// 이름 경계(<c>.</c>)에서 맞추므로 <c>Unity</c> 는 <c>Unity.TextMeshPro</c> 를 제외하지만 <c>UnityFoo</c> 는 제외하지 않는다.
        /// </remarks>
        private static readonly string[] NotTheGames =
        {
            "UnityEngine", "UnityEditor", "Unity", "UnityPlayMcp", "System", "mscorlib", "netstandard",
            "nunit", "Newtonsoft", "Mono", "TMPro"
        };

        /// <summary>
        /// 캐시를 비우기 전까지 기억하는 타입 수.
        /// </summary>
        /// <remarks>
        /// 캐시가 끝없이 자라지 않게 한다(<see cref="Worth"/> 와 같다). 비워도 리플렉션을 다시 할 뿐 답은 틀리지 않는다.
        /// </remarks>
        private const int MaxRemembered = 2048;

        private const string BackingPrefix = "<";
        private const string BackingSuffix = ">k__BackingField";

        private static readonly Dictionary<Type, List<Watched>> Answered =
            new Dictionary<Type, List<Watched>>();

        /// <summary>
        /// 이 컴포넌트에서 읽을 멤버. <c>evidence</c> 가 이름 댄 멤버와 그 밖의 읽을 수 있는 멤버다.
        /// </summary>
        /// <param name="named">watch list 가 이 타입에 대해 가진 멤버, 또는 null.</param>
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

            if (TheGames(type))
            {
                Fields(members, taken, type);
            }

            // 분기 밖에 둔다. `Image` 를 상속한 게임 컴포넌트의 `fillAmount` 는 `GetFields` 가 기반 클래스의 private
            // 필드를 주지 않아 여기서만 나온다.
            Drawn.Add(members, taken, type);

            return members;
        }

        /// <summary>이 타입에서 되읽을 수 있는 필드들을 넣는다.</summary>
        private static void Fields(List<Watched> into, HashSet<string> taken, Type type)
        {
            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo[] fields;

            try
            {
                // 기반 클래스에 상태를 두는 behaviour 가 흔하므로 상속된 필드도 읽는다.
                fields = type.GetFields(Flags);
            }
            catch (Exception)
            {
                // 이 컴포넌트만 건너뛰고 객체의 나머지는 읽는다.
                return;
            }

            foreach (var field in fields)
            {
                if (taken.Contains(field.Name) || Skip(field))
                {
                    continue;
                }

                taken.Add(field.Name);

                into.Add(new Watched
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
        }

        /// <summary>
        /// 읽지 않을 필드: static, 상수, 델리게이트.
        /// </summary>
        /// <remarks>
        /// 델리게이트 필드는 구독자 목록이라 게임 상태를 말하지 않고, 구독할 때마다 값이 바뀌어 변화 판정을 흔든다.
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
        /// backing field 이름에서 자동 프로퍼티 이름을 꺼낸다. backing field 가 아니면 null 이다.
        /// </summary>
        /// <remarks>
        /// <c>&lt;Instance&gt;k__BackingField</c> 이름만으로는 다른 곳의 <c>Instance</c> 와 이어지지 않는다. pulse 는
        /// 값이 있을 때만 이 이름을 싣는다.
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
