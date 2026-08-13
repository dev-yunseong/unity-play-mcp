using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Artel.Tracking;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.CodeGen
{
    public sealed class ArtelILPostProcessor : ILPostProcessor
    {
        private const string RuntimeAssemblyName = "Artel.Runtime";
        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name == RuntimeAssemblyName || compiledAssembly.Name == "Unity.Artel.CodeGen")
            {
                return false;
            }

            return compiledAssembly.References.Any(reference =>
                string.Equals(Path.GetFileNameWithoutExtension(reference), RuntimeAssemblyName, StringComparison.Ordinal));
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            using (var resolver = new CompiledAssemblyResolver(compiledAssembly.References))
            using (var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData))
            using (var pdbStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData ?? Array.Empty<byte>()))
            {
                var hasSymbols = pdbStream.Length > 0;
                var reader = new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Immediate,
                    ReadSymbols = hasSymbols,
                    SymbolStream = hasSymbols ? pdbStream : null,
                    SymbolReaderProvider = hasSymbols ? new PortablePdbReaderProvider() : null
                };

                using (var assembly = AssemblyDefinition.ReadAssembly(peStream, reader))
                {
                    // WillProcess가 통과시켰다고 해서 위빙할 게 있다는 뜻은 아니다. 거기서 보는
                    // 컴파일러 참조 목록에는 autoReferenced 때문에 Artel.Runtime이 항상 들어 있고,
                    // 실제로 SDK 타입을 쓰는지는 IL 메타데이터를 열어 봐야 안다.
                    var actionWeaver = ActionMethodWeaver.TryCreate(assembly.MainModule, diagnostics);
                    var inputWeaver = InputMethodWeaver.TryCreate(assembly.MainModule);
                    if (actionWeaver == null && inputWeaver == null)
                    {
                        return new ILPostProcessResult(null, diagnostics);
                    }

                    var changed = actionWeaver != null && actionWeaver.Process();
                    changed |= inputWeaver != null && inputWeaver.Process();
                    if (!changed)
                    {
                        return new ILPostProcessResult(null, diagnostics);
                    }

                    using (var outputPe = new MemoryStream())
                    using (var outputPdb = new MemoryStream())
                    {
                        assembly.Write(outputPe, new WriterParameters
                        {
                            WriteSymbols = hasSymbols,
                            SymbolStream = hasSymbols ? outputPdb : null,
                            SymbolWriterProvider = hasSymbols ? new PortablePdbWriterProvider() : null
                        });

                        return new ILPostProcessResult(
                            new InMemoryAssembly(outputPe.ToArray(), hasSymbols ? outputPdb.ToArray() : null),
                            diagnostics);
                    }
                }
            }
        }
    }
}
