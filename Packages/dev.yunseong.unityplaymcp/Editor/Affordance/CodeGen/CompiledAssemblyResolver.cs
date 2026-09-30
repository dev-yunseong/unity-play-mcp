using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace UnityPlayMcp.Affordances.CodeGen
{
    /// <summary>
    /// 게임 어셈블리가 참조하는 어셈블리를 컴파일러가 건넨 경로에서 찾는다.
    /// </summary>
    /// <remarks>
    /// behaviour 판정은 기반 타입을 따라 다른 어셈블리(최소한 <c>UnityEngine.CoreModule</c>)에 닿는다. 분석 대상이
    /// 메모리 스트림이라 Cecil 이 옆 디렉터리를 뒤질 수 없다.
    ///
    /// 모듈의 <c>AssemblyReferences</c> 는 실제로 쓴 것만 담아 더 작으므로 컴파일러의 참조 경로를 쓴다.
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

                // 어셈블리 참조가 담는 것이 파일 이름이므로 그것을 키로 쓴다. 컴파일러는 한 이름에 두 경로를 주지 않는다.
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

            // 실패도 캐시한다. 그러지 않으면 그 참조를 거쳐 상속하는 타입마다 다시 찾는다.
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
                // 읽히지 않는 참조는 평범한 입력이다. null 이면 기반 타입 하나를 잃고, 던지면 어셈블리 전체를 잃는다.
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
