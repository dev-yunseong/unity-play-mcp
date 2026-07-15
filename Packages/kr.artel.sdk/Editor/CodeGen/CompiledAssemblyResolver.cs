using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace Artel.CodeGen
{
    internal sealed class CompiledAssemblyResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string> assemblyPaths = new Dictionary<string, string>();
        private readonly Dictionary<string, AssemblyDefinition> assemblies = new Dictionary<string, AssemblyDefinition>();

        public CompiledAssemblyResolver(IEnumerable<string> references)
        {
            foreach (var reference in references)
            {
                assemblyPaths[Path.GetFileNameWithoutExtension(reference)] = reference;
            }
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters());
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (assemblies.TryGetValue(name.Name, out var assembly))
            {
                return assembly;
            }

            if (!assemblyPaths.TryGetValue(name.Name, out var path))
            {
                throw new AssemblyResolutionException(name);
            }

            parameters.AssemblyResolver = this;
            parameters.ReadSymbols = false;
            assembly = AssemblyDefinition.ReadAssembly(path, parameters);
            assemblies[name.Name] = assembly;
            return assembly;
        }

        public void Dispose()
        {
            foreach (var assembly in assemblies.Values)
            {
                assembly.Dispose();
            }

            assemblies.Clear();
        }
    }
}
