using System;
using System.Collections.Generic;
using System.Reflection;
using Artel.Domain;
using UnityEngine;

namespace Artel.Tracking
{
    internal sealed class StateReader
    {
        private readonly Dictionary<Type, IReadOnlyList<MemberAccessor>> accessorsByType =
            new Dictionary<Type, IReadOnlyList<MemberAccessor>>();

        private readonly SerializedFieldReader serializedFieldReader = new SerializedFieldReader();

        public IReadOnlyList<TrackedState> Read(Component component)
        {
            return Read(component, false);
        }

        /// <summary>
        /// Reads the component's tracked state, optionally widened to every field Unity would
        /// serialize on it.
        /// </summary>
        /// <remarks>
        /// Tagged members win: a field carrying <see cref="ArtelStateAttribute"/> keeps its tag and
        /// is not read a second time as a plain serialized field.
        /// </remarks>
        public IReadOnlyList<TrackedState> Read(Component component, bool includeSerializedFields)
        {
            var states = new List<TrackedState>();
            var taggedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var accessor in GetAccessors(component.GetType()))
            {
                taggedNames.Add(accessor.Name);
                try
                {
                    states.Add(new TrackedState(accessor.Tag, accessor.Name, accessor.TypeName, accessor.Read(component)));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Artel] Failed to read state " + accessor.Name + ": " + exception.Message);
                }
            }

            if (!includeSerializedFields)
            {
                return states;
            }

            foreach (var field in serializedFieldReader.GetSerializedFields(component.GetType()))
            {
                if (taggedNames.Contains(field.Name))
                {
                    continue;
                }

                try
                {
                    states.Add(new TrackedState(
                        string.Empty,
                        field.Name,
                        NormalizeType(field.FieldType),
                        serializedFieldReader.ReadValue(field, component)));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Artel] Failed to read field " + field.Name + ": " + exception.Message);
                }
            }

            states.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            return states;
        }

        public bool HasTrackedState(Type componentType)
        {
            return GetAccessors(componentType).Count > 0;
        }

        private IReadOnlyList<MemberAccessor> GetAccessors(Type type)
        {
            if (accessorsByType.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var accessors = new List<MemberAccessor>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var field in type.GetFields(flags))
            {
                var attribute = field.GetCustomAttribute<ArtelStateAttribute>(true);
                if (attribute != null)
                {
                    accessors.Add(new MemberAccessor(attribute.Tag, field.Name, NormalizeType(field.FieldType), field.GetValue));
                }
            }

            foreach (var property in type.GetProperties(flags))
            {
                var attribute = property.GetCustomAttribute<ArtelStateAttribute>(true);
                if (attribute != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    accessors.Add(new MemberAccessor(
                        attribute.Tag, property.Name, NormalizeType(property.PropertyType), property.GetValue));
                }
            }

            accessors.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            accessorsByType[type] = accessors;
            return accessors;
        }

        private static string NormalizeType(Type type)
        {
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            return type.IsEnum ? "enum" : type.FullName;
        }

        private sealed class MemberAccessor
        {
            private readonly Func<object, object> read;

            public string Tag { get; }
            public string Name { get; }
            public string TypeName { get; }

            public MemberAccessor(string tag, string name, string typeName, Func<object, object> read)
            {
                Tag = tag;
                Name = name;
                TypeName = typeName;
                this.read = read;
            }

            public object Read(object target) => read(target);
        }
    }
}
