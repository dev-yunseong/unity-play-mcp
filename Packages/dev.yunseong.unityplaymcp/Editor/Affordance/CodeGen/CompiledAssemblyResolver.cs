using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 게임 어셈블리가 무엇에 대고 컴파일됐는지를 찾아낸다.
    /// </summary>
    /// <remarks>
    /// 어떤 타입이 behaviour 인지 판단하려면 그 기반 타입들을 거슬러 올라가야 하고, 그 걸음마다 다른
    /// 어셈블리에 닿는다 — 최소한 <c>UnityEngine.CoreModule</c> 에는. 여기서 Cecil 은 그것을 스스로
    /// 따라가지 못한다. 분석 대상 어셈블리가 메모리 위의 스트림이라 옆에서 뒤질 디렉터리가 없기 때문이다.
    ///
    /// 컴파일러가 건네준 참조 경로가 답이고, 그것이 유일한 답이다: 모듈 자신의 <c>AssemblyReferences</c>
    /// 는 그 코드가 실제로 쓴 것을 적은 목록이라 다르고 더 작은 집합이다.
    /// </remarks>
    internal sealed class CompiledAssemblyResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string> _pathsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, AssemblyDefinition> _opened = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

        internal CompiledAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            var references = compiledAssembly.References;
            if (references == null)
            {
                return;
            }

            foreach (var reference in references)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                // 파일 이름으로 키를 잡는다. 어셈블리 참조가 들고 다니는 것이 그것이기 때문이다. 뒤에 온 항목이
                // 이긴다 — 컴파일러가 한 이름에 두 경로를 건네지는 않는다.
                _pathsByName[Path.GetFileNameWithoutExtension(reference)] = reference;
            }
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (name == null)
            {
                return null;
            }

            if (_opened.TryGetValue(name.Name, out var alreadyOpen))
            {
                return alreadyOpen;
            }

            var opened = Open(name.Name, parameters);

            // null 로 돌아왔을 때도 캐시한다. 찾지 못하는 참조는 그것을 거쳐 상속하는 타입마다 한 번씩 다시
            // 물어보게 되고, 실패하는 데 걸리는 시간은 성공하는 것과 같다.
            _opened[name.Name] = opened;
            return opened;
        }

        private AssemblyDefinition Open(string name, ReaderParameters parameters)
        {
            if (!_pathsByName.TryGetValue(name, out var path) || !File.Exists(path))
            {
                return null;
            }

            parameters.AssemblyResolver = this;

            try
            {
                return AssemblyDefinition.ReadAssembly(path, parameters);
            }
            catch (Exception)
            {
                // 남의 빌드를 걸을 때 읽히지 않는 참조는 예외가 아니라 평범한 입력이다. null 을 돌려주면 해석되지
                // 않은 기반 타입 하나를 잃고, 던지면 어셈블리 전체를 잃는다.
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var assembly in _opened.Values)
            {
                assembly?.Dispose();
            }

            _opened.Clear();
        }
    }
}
