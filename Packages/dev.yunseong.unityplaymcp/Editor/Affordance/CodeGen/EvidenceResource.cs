using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Mono.Cecil;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// `evidence` 를 기록마다 attribute 로 두지 않고 어셈블리 안의 압축된 resource 하나로 나른다.
    /// </summary>
    /// <remarks>
    /// 기록마다 attribute 를 두면 metadata 가 커져 어셈블리가 몇 배로 불어난다. resource 는 타입 로드에
    /// 영향이 없고, 요청할 때까지 파싱되지 않으며, 압축된다.
    ///
    /// gzip 헤더에는 타임스탬프가 들어가 같은 입력에서 같은 바이트가 나오지 않으므로 deflate 를 쓴다.
    /// </remarks>
    internal static class EvidenceResource
    {
        /// <summary>
        /// 어셈블리 안의 `evidence` resource 이름.
        /// </summary>
        /// <remarks>
        /// 게임의 resource 와 겹치지 않도록 패키지 이름을 접두어로 쓴다. 스캔은 이 이름으로 찾으므로
        /// 난독화가 resource 이름을 바꾸면 찾지 못한다.
        /// </remarks>
        internal const string ResourceName = "dev.yunseong.unityplaymcp.affordance.evidence";

        /// <summary>
        /// 어셈블리 안의 watch list resource 이름.
        /// </summary>
        /// <remarks>
        /// `evidence` 와 따로 둔다. 둘은 다른 코드가 다른 시점에 읽으므로 한쪽만 필요할 때 다른 쪽을 풀지
        /// 않아도 된다. 이 resource 를 모르는 옛 런타임은 요청하지 않을 뿐 `evidence` 는 그대로 읽는다.
        /// </remarks>
        internal const string WatchResourceName = "dev.yunseong.unityplaymcp.affordance.watch";

        /// <summary>모듈의 `evidence` resource 를 교체하고 압축된 바이트 수를 돌려준다.</summary>
        internal static int Attach(ModuleDefinition module, string json)
        {
            return Attach(module, ResourceName, json);
        }

        /// <summary>모듈의 watch list resource 를 교체하고 압축된 바이트 수를 돌려준다.</summary>
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
        /// 앞선 패스가 남긴 resource 를 제거한다.
        /// </summary>
        /// <remarks>
        /// 보통은 없어야 한다. 두 번 처리된 어셈블리에 옛 resource 가 남으면 새 것과 구분할 수 없다.
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
                // 프레임워크 기본값이 바뀌어 출력 바이트가 달라지지 않도록 레벨을 명시한다.
                using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    compressor.Write(raw, 0, raw.Length);
                }

                return output.ToArray();
            }
        }
    }
}
