using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 어셈블리 리소스에 담긴 `evidence`. 한 번 읽어 캐시한다.
    /// </summary>
    /// <remarks>
    /// 어셈블리당 압축 리소스 하나에 전부 담고, attribute 는 anchor 만 가리킨다.
    /// 스트리핑은 attribute 를 없애고 난독화는 타입 이름을 바꾸므로 anchor 로 먼저 찾고 이름으로 물러선다.
    /// 같은 타입의 인스턴스가 많으므로 어셈블리별, 타입별로 캐시한다.
    /// </remarks>
    internal static class AffordanceCatalog
    {
        private const string ResourceName = "dev.yunseong.unityplaymcp.affordance.evidence";

        private sealed class Carried
        {
            internal readonly Dictionary<int, string> ByAnchor = new Dictionary<int, string>();

            internal readonly Dictionary<string, string> ByName =
                new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static readonly Dictionary<Assembly, Carried> Opened =
            new Dictionary<Assembly, Carried>();

        private static readonly Dictionary<Type, string> Known = new Dictionary<Type, string>();

        /// <summary>타입의 `evidence` 배열 JSON 텍스트. 없으면 null.</summary>
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

            // 대부분의 컴포넌트는 `evidence` 가 없으므로 null 도 캐시한다.
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
                // 리소스를 열지 못한 어셈블리는 건너뛰고 씬 읽기는 계속한다.
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
                // 이름 매칭으로 물러선다.
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

                // 한 줄에 타입 하나: anchor, 이름, 배열이 탭으로 구분된다. 배열은 스키마를 분석기와 읽는 쪽이 정하므로
                // 파싱하지 않고 그대로 통과시킨다.
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
        /// 로드된 모든 어셈블리에서 `evidence` 를 가진 모든 타입.
        /// </summary>
        /// <remarks>
        /// 런타임에 인스턴스화되는 프리팹의 behaviour 는 씬 스캔에서 보이지 않으므로, 리포트가 그 차이를 밝히도록 전체 목록을 만든다.
        /// 만난 타입이 없는 어셈블리가 핵심이므로 모든 어셈블리를 연다.
        /// 키는 컴파일 시점 이름이고 난독화 후 이름과 다를 수 있으므로, 호출자는 키가 아니라 문서를 비교해야 한다.
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
                    // 동적 어셈블리이거나 리소스를 열지 못한 어셈블리는 건너뛴다.
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
