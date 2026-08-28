using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 근거를 기록마다 attribute 하나가 아니라 어셈블리 안의 압축된 blob 하나로 나른다.
    /// </summary>
    /// <remarks>
    /// 기록마다 attribute 를 두는 방식은 엉뚱한 것 위에 증가분을 얹었다. 프로젝트 셋에서 실측하니
    /// 어셈블리가 제 크기의 3~8배로 나왔고, 어느 쪽이 얼마가 되는지는 게임 크기와 아무 상관이 없었다 —
    /// 분기가 촘촘한 작은 게임이 다섯 배 큰 게임보다 비쌌다. 그 증가분의 98%가 JSON 텍스트였고, 대부분은
    /// 같은 메서드 시그니처를 몇 번이고 다시 쓴 것이었다.
    ///
    /// 리소스는 메타데이터가 아니다. 타입 로드가 걷는 테이블을 키우지 않고, 무언가 요청하기 전까지
    /// 파싱되지 않으며, 압축된다 — attribute 로 322KB 였던 같은 텍스트가 여기서는 13KB 다.
    ///
    /// gzip 이 아니라 deflate 인 것은, gzip 이 여기서 쓸데없는 헤더를 쓰고 그 필드 하나가 타임스탬프이기
    /// 때문이다. 같은 어셈블리를 두 번 분석하면 같은 바이트가 나와야 하는데, 출력 안의 시계는 그것을
    /// 조용히 깨뜨린다.
    /// </remarks>
    internal static class EvidenceResource
    {
        /// <summary>
        /// 어셈블리 안에서 이 리소스가 불리는 이름.
        /// </summary>
        /// <remarks>
        /// 게임이 이미 나르는 리소스와 부딪치지 않도록 이 패키지의 이름을 따랐다. 스캔은 이 이름으로 그것을
        /// 찾는데, 난독화가 앗아갈 수 있는 것은 여기서 이것 하나다 — attribute 는 제 타입에 붙어 있으므로
        /// 이름이 바뀌어도 살아남지만, 리소스는 붙어 있을 것이 없다.
        /// </remarks>
        internal const string ResourceName = "kr.artel.affordance.evidence";

        /// <summary>
        /// 어셈블리 안에서 watch list 가 불리는 이름.
        /// </summary>
        /// <remarks>
        /// 근거 blob 안의 또 한 줄이 아니라 제 리소스로 둔다. 둘은 서로 다른 코드가 서로 다른 순간에 읽는다 —
        /// 근거는 스캔이 어떤 타입을 만났을 때, watch list 는 폴링이 시작되기 전 한 번. 한쪽을 원하는 독자가
        /// 다른 쪽을 풀어헤치고 건너뛸 일은 없어야 하는데, 실제 게임에서 그 차이는 두 자릿수다.
        ///
        /// 떨어져 있으면 무너지는 방식도 옳다. 새 어셈블리를 만난 옛 런타임은 그저 이것을 요청하지 않고 근거를
        /// 늘 하던 대로 읽는다. 같은 문서에 접어 넣었다면 모든 독자가 제게 쓸모없는 한 줄에 대해 합의해야
        /// 했을 것이다.
        /// </remarks>
        internal const string WatchResourceName = "kr.artel.affordance.watch";

        /// <summary>모듈 위의 blob 을 갈아 끼우고, 몇 바이트였는지 말한다.</summary>
        internal static int Attach(ModuleDefinition module, string json)
        {
            return Attach(module, ResourceName, json);
        }

        /// <summary>모듈 위의 watch list 를 갈아 끼우고, 몇 바이트였는지 말한다.</summary>
        internal static int AttachWatch(ModuleDefinition module, string json)
        {
            return Attach(module, WatchResourceName, json);
        }

        private static int Attach(ModuleDefinition module, string name, string json)
        {
            Detach(module, name);

            if (string.IsNullOrEmpty(json))
            {
                return 0;
            }

            var packed = Deflate(Encoding.UTF8.GetBytes(json));

            module.Resources.Add(
                new EmbeddedResource(name, ManifestResourceAttributes.Public, packed));

            return packed.Length;
        }

        /// <summary>
        /// 앞선 패스가 남긴 blob 을 걷어낸다.
        /// </summary>
        /// <remarks>
        /// 파이프라인은 갓 컴파일된 어셈블리를 건네주므로 여기서 찾을 것은 없어야 한다. 여기를 두 번 지난
        /// 어셈블리는 두 세대를 한꺼번에 나르게 되고, 옛 것은 새 것과 구분되지 않으면서 조용히 그것과 어긋난
        /// 말을 한다.
        /// </remarks>
        internal static void Detach(ModuleDefinition module)
        {
            Detach(module, ResourceName);
            Detach(module, WatchResourceName);
        }

        private static void Detach(ModuleDefinition module, string name)
        {
            for (var index = module.Resources.Count - 1; index >= 0; index--)
            {
                if (string.Equals(module.Resources[index].Name, name, StringComparison.Ordinal))
                {
                    module.Resources.RemoveAt(index);
                }
            }
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                // 일부러 레벨을 고정한다. 기본값도 이미 결정적이지만, 이름을 적어 두는 것이 프레임워크 업그레이드가
                // 이 파일을 아무도 건드리지 않은 채 바이트 동일 검사 밑의 바이트를 바꾸는 일을 막는다.
                using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    compressor.Write(raw, 0, raw.Length);
                }

                return output.ToArray();
            }
        }
    }
}
