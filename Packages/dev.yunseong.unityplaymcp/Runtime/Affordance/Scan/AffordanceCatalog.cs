using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 어셈블리가 나르는 근거. 한 번 읽고 쥐고 있는다.
    /// </summary>
    /// <remarks>
    /// 예전에는 타입마다 기록 하나당 attribute 하나였다. 그것이 실측한 프로젝트들에서 게임 어셈블리를 제 크기의
    /// 3~8배로 만들었고, 살아남지도 못했다: managed stripping 을 High 로 두면 커스텀 attribute 가 사라지므로 세게
    /// 스트리핑한 빌드는 아무것도 없는 게임을 보고했다.
    ///
    /// 지금은 어셈블리당 압축된 리소스 하나가 전부를 쥐고 attribute 는 가리키는 것일 뿐이다. 들어가는 길이 둘인 것은,
    /// 빌드가 받는 두 처리가 각각 다른 하나를 앗아가기 때문이다 — 스트리핑은 attribute 를 없애고 리소스를 남기며,
    /// 난독화는 타입의 이름을 바꾸고 attribute 를 남긴다. anchor 로 먼저 묻는 것은 그것이 정확하기 때문이고, 그다음이
    /// 이름이다.
    ///
    /// 어셈블리별로도 타입별로도 캐시한다. 한 씬은 적은 수의 타입의 인스턴스를 많이 쥐고 있고, 한 종류의 버튼 백 개는
    /// 같은 물음을 백 번 묻는다.
    /// </remarks>
    internal static class AffordanceCatalog
    {
        private const string ResourceName = "kr.artel.affordance.evidence";

        private sealed class Carried
        {
            internal readonly Dictionary<int, string> ByAnchor = new Dictionary<int, string>();

            internal readonly Dictionary<string, string> ByName =
                new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static readonly Dictionary<Assembly, Carried> Opened =
            new Dictionary<Assembly, Carried>();

        private static readonly Dictionary<Type, string> Known = new Dictionary<Type, string>();

        /// <summary>타입에 대한 근거 배열. 이미 그것인 문서 그대로, 또는 null.</summary>
        internal static string For(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (Known.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var found = Look(type);

            // 아무것도 찾지 못했을 때도 캐시한다. 씬의 컴포넌트 대부분은 배경이고 인스턴스마다 한 번씩 물어보게 된다.
            Known[type] = found;
            return found;
        }

        private static string Look(Type type)
        {
            Carried carried;

            try
            {
                carried = Read(type.Assembly);
            }
            catch (Exception)
            {
                // 리소스가 열리지 않는 어셈블리 하나는 리포트의 공백이지, 씬 읽기를 멈출 이유가 아니다.
                return null;
            }

            if (carried == null)
            {
                return null;
            }

            try
            {
                var attributes =
                    (AffordanceAttribute[])type.GetCustomAttributes(typeof(AffordanceAttribute), false);

                if (attributes.Length > 0 &&
                    carried.ByAnchor.TryGetValue(attributes[0].Anchor, out var byAnchor))
                {
                    return byAnchor;
                }
            }
            catch (Exception)
            {
                // 이름으로 흘러 내려간다. 두 번째 입구가 존재하는 이유가 정확히 그 경우다.
            }

            return type.FullName != null && carried.ByName.TryGetValue(type.FullName, out var byName)
                ? byName
                : null;
        }

        private static Carried Read(Assembly assembly)
        {
            if (assembly == null)
            {
                return null;
            }

            if (Opened.TryGetValue(assembly, out var already))
            {
                return already;
            }

            var carried = Parse(assembly);
            Opened[assembly] = carried;
            return carried;
        }

        private static Carried Parse(Assembly assembly)
        {
            using (var packed = assembly.GetManifestResourceStream(ResourceName))
            {
                if (packed == null)
                {
                    return null;
                }

                string text;

                using (var expanded = new DeflateStream(packed, CompressionMode.Decompress))
                using (var reader = new StreamReader(expanded, Encoding.UTF8))
                {
                    text = reader.ReadToEnd();
                }

                var carried = new Carried();

                // 한 줄에 타입 하나: anchor, 이름, 그다음 배열. 앞의 탭 둘에서만 자른다. 그 뒤에 오는 것이 문서이고, 파싱하지 않고
                // 그대로 통과시킨다 — 그 스키마는 그것을 쓴 분석기와 그것을 읽는 에이전트의 것이고, 여기서 세 번째 의견을 내면 그
                // 둘 모두와 발을 맞춰야 한다.
                foreach (var line in text.Split('\n'))
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    var firstTab = line.IndexOf('\t');
                    var secondTab = firstTab < 0 ? -1 : line.IndexOf('\t', firstTab + 1);

                    if (secondTab < 0)
                    {
                        continue;
                    }

                    var name = line.Substring(firstTab + 1, secondTab - firstTab - 1);
                    var document = line.Substring(secondTab + 1);

                    if (int.TryParse(line.Substring(0, firstTab), out var anchor))
                    {
                        carried.ByAnchor[anchor] = document;
                    }

                    carried.ByName[name] = document;
                }

                return carried;
            }
        }

        /// <summary>
        /// 로드된 어셈블리 중 무엇이든 근거를 나르는 모든 타입.
        /// </summary>
        /// <remarks>
        /// 스캔은 GameObject 위에서 만나는 타입만 서술할 수 있는데, 게임은 제 behaviour 대부분을 무슨 일이 일어나야 비로소
        /// 인스턴스화되는 프리팹에 담아 둔다. 샘플 게임에서 실측하니 분석은 behaviour 54 개를 구웠고 모든 씬을 순회해
        /// 만난 것은 21 개였다 — 나머지 33 개는 어셈블리 안에 있었고, 올바랐고, 보이지 않았다.
        ///
        /// 플레이 중에 읽으면 대부분을 되찾는다. 인스턴스화된 프리팹은 다른 것과 마찬가지로 씬 안에 있기 때문이다. 남는 것은
        /// 그 실행이 한 번도 존재하게 만들지 않은 것이고, 그것은 리포트의 결함이 아니라 진짜 한계다. 그 차이에 이름을 붙이는
        /// 일이 그것을 누군가 발견해야 하는 것에서 리포트가 말하는 것으로 바꾼다.
        ///
        /// 만난 타입이 나온 어셈블리만이 아니라 모든 어셈블리를 한 번씩 연다. 요점이 바로 만난 타입이 하나도 나오지 않은
        /// 어셈블리들이기 때문이다.
        ///
        /// 분석기가 쓴 이름으로 키를 잡는데, 그것은 타입이 컴파일될 때 가지고 있던 이름이다. 난독화기는 그 뒤에 돌므로 이것들은
        /// 원래 이름이고 스캔이 만나는 쪽은 바뀐 이름을 나른다 — 무엇이 놓이는지를 키가 아니라 문서를 비교해서 정하는
        /// 이유가 그것이다.
        /// </remarks>
        internal static Dictionary<string, string> Everything()
        {
            var named = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Carried carried;

                try
                {
                    carried = Read(assembly);
                }
                catch (Exception)
                {
                    // 동적 어셈블리이거나 리소스가 열리지 않는 어셈블리다. 건너뛰면 답이 작아지지 틀리지는 않는다.
                    continue;
                }

                if (carried == null)
                {
                    continue;
                }

                foreach (var pair in carried.ByName)
                {
                    named[pair.Key] = pair.Value;
                }
            }

            return named;
        }

        internal static void Forget()
        {
            Known.Clear();
            Opened.Clear();
        }
    }
}
