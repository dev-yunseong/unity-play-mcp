using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;
using UnityPlayMcp.Play;

namespace UnityPlayMcp
{
    internal sealed class RegisteredActionDto
    {
        public RegisteredActionDto(UnityActionAttribute metadata)
        {
            Name = metadata.Name;
            Description = metadata.Description;
            Parameters = metadata.Parameters;
        }

        [JsonProperty("name")] public string Name { get; }
        [JsonProperty("description")] public string Description { get; }
        [JsonProperty("parameters")] public string Parameters { get; }
    }

    internal sealed class ActionRegistry
    {
        public const string ProjectMethod = "unity_action";

        private static readonly Regex ActionName = new Regex(@"\A[a-z][a-z0-9_]{0,63}\z");
        private static readonly Lazy<Type[]> ActionTypes = new Lazy<Type[]>(DiscoverActionTypes);

        private readonly Dictionary<string, IUnityAction> builtIns =
            new Dictionary<string, IUnityAction>(StringComparer.Ordinal);
        private readonly Dictionary<string, IUnityAction> projectActions =
            new Dictionary<string, IUnityAction>(StringComparer.Ordinal);

        public ActionRegistry(ActionContext context)
        {
            var entries = ActionTypes.Value.Select(type => (Type: type, Metadata: type.GetCustomAttribute<UnityActionAttribute>()))
                .Where(entry => entry.Metadata != null)
                .ToArray();

            foreach (var entry in entries.Where(entry => typeof(BuiltInUnityAction).IsAssignableFrom(entry.Type)))
            {
                var action = (BuiltInUnityAction)Activator.CreateInstance(entry.Type, true);
                action.Context = context;
                builtIns.Add(entry.Metadata.Name, action);
            }

            var descriptions = new List<RegisteredActionDto>();
            var projects = entries.Where(entry => !typeof(BuiltInUnityAction).IsAssignableFrom(entry.Type));
            foreach (var group in projects.GroupBy(entry => entry.Metadata.Name, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var entry = group.First();
                var name = entry.Metadata.Name;
                if (string.IsNullOrEmpty(name) || !ActionName.IsMatch(name)
                    || string.IsNullOrWhiteSpace(entry.Metadata.Description)
                    || string.IsNullOrWhiteSpace(entry.Metadata.Parameters)
                    || name == ProjectMethod || name == "runtime_activity" || PlayApi.Handles(name)
                    || builtIns.ContainsKey(name) || group.Count() != 1)
                {
                    Debug.LogWarning("[Unity Play MCP] Invalid, reserved or duplicate Unity action: " + name);
                    continue;
                }

                try
                {
                    var action = (IUnityAction)Activator.CreateInstance(entry.Type, true);
                    projectActions.Add(name, action);
                    descriptions.Add(new RegisteredActionDto(entry.Metadata));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Unity Play MCP] Could not create Unity action " + name + ": "
                        + exception.GetBaseException().Message);
                }
            }

            ProjectActions = descriptions;
        }

        public IReadOnlyList<RegisteredActionDto> ProjectActions { get; }

        public bool TryGetBuiltIn(string name, out IUnityAction action) =>
            builtIns.TryGetValue(name ?? string.Empty, out action);

        public bool TryGetProject(string name, out IUnityAction action) => projectActions.TryGetValue(name, out action);

        private static Type[] DiscoverActionTypes()
        {
            var runtime = typeof(IUnityAction).Assembly;
            var runtimeName = runtime.GetName().Name;
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && (assembly == runtime
                    || assembly.GetReferencedAssemblies().Any(reference => reference.Name == runtimeName)))
                .SelectMany(LoadableTypes)
                .Where(type => type != null && type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters
                    && !typeof(UnityEngine.Object).IsAssignableFrom(type) && typeof(IUnityAction).IsAssignableFrom(type))
                .ToArray();
        }

        private static Type[] LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types;
            }
        }
    }
}
