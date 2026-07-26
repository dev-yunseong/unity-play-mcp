using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Artel.Tracking
{
    /// <summary>
    /// Reads the fields Unity itself would serialize on a component, and lowers their values to
    /// primitives, strings, lists and dictionaries.
    /// </summary>
    /// <remarks>
    /// The lowering is not a convenience. Handing a Unity value type straight to Newtonsoft
    /// recurses forever — <c>Vector3.normalized</c> is another <c>Vector3</c> — so every value that
    /// reaches the codec is built here from fields only, never from property getters.
    /// </remarks>
    internal sealed class SerializedFieldReader
    {
        /// <summary>
        /// Nesting allowed inside a field's value. Unity truncates serialization at a similar
        /// depth; reflection would not truncate at all.
        /// </summary>
        private const int MaxDepth = 5;

        /// <summary>Elements read from an array or list. The rest are dropped.</summary>
        private const int MaxElements = 64;

        private const int MaxStringLength = 1024;

        private readonly Dictionary<Type, IReadOnlyList<FieldInfo>> fieldsByType =
            new Dictionary<Type, IReadOnlyList<FieldInfo>>();

        /// <summary>
        /// The serialized fields of <paramref name="type"/> and its non-engine base classes, sorted
        /// by name so a snapshot of the same object always reads the same way.
        /// </summary>
        public IReadOnlyList<FieldInfo> GetSerializedFields(Type type)
        {
            if (fieldsByType.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var fields = new List<FieldInfo>();
            var declaredNames = new HashSet<string>(StringComparer.Ordinal);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var current = type; IsWalkable(current); current = current.BaseType)
            {
                foreach (var field in current.GetFields(flags))
                {
                    // A derived class can shadow a base field of the same name. The most-derived
                    // one is what the object presents, and it is the one already added.
                    if (!IsSerialized(field) || !declaredNames.Add(field.Name))
                    {
                        continue;
                    }

                    fields.Add(field);
                }
            }

            fields.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            fieldsByType[type] = fields;
            return fields;
        }

        /// <summary>
        /// Reads one field off <paramref name="target"/> and lowers it.
        /// </summary>
        public object ReadValue(FieldInfo field, object target)
        {
            return Lower(field.GetValue(target), 0, new HashSet<object>(ReferenceComparer.Instance));
        }

        private static bool IsWalkable(Type type)
        {
            // Stops before the engine's own fields: MonoBehaviour and everything above it belongs
            // to Unity, not to the game. Plain nested classes stop at object.
            return type != null && type != typeof(object) && !IsEngineType(type);
        }

        private static bool IsEngineType(Type type)
        {
            return type == typeof(MonoBehaviour) ||
                   type == typeof(Behaviour) ||
                   type == typeof(Component) ||
                   type == typeof(ScriptableObject) ||
                   type == typeof(UnityEngine.Object);
        }

        private static bool IsSerialized(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized)
            {
                return false;
            }

            // Native handles carried by engine types (AnimationCurve and friends) say nothing and
            // serialize as a meaningless address.
            if (field.FieldType == typeof(IntPtr) || field.FieldType == typeof(UIntPtr))
            {
                return false;
            }

            return field.IsPublic || field.GetCustomAttribute<SerializeField>(true) != null;
        }

        private object Lower(object value, int depth, HashSet<object> path)
        {
            if (value == null)
            {
                return null;
            }

            // Unity's destroyed objects compare equal to null while still being a live reference.
            if (value is UnityEngine.Object unityObject)
            {
                return unityObject == null ? null : Describe(unityObject);
            }

            if (value is string text)
            {
                return text.Length > MaxStringLength ? text.Substring(0, MaxStringLength) + "…" : text;
            }

            var type = value.GetType();
            if (type.IsEnum)
            {
                return value.ToString();
            }

            if (type.IsPrimitive)
            {
                return value is char character ? character.ToString() : value;
            }

            if (value is decimal number)
            {
                return (double)number;
            }

            // Unity serializes arrays and List<T>, both of which are IList. Anything else
            // enumerable it would not have serialized either.
            if (value is IList list)
            {
                return LowerElements(list, depth, path);
            }

            if (depth >= MaxDepth || !type.IsSerializable)
            {
                return null;
            }

            return LowerFields(value, type, depth, path);
        }

        private List<object> LowerElements(IList list, int depth, HashSet<object> path)
        {
            var count = Math.Min(list.Count, MaxElements);
            var elements = new List<object>(count);
            for (var index = 0; index < count; index++)
            {
                elements.Add(Lower(list[index], depth + 1, path));
            }

            return elements;
        }

        private Dictionary<string, object> LowerFields(
            object value,
            Type type,
            int depth,
            HashSet<object> path)
        {
            // Value types cannot form a cycle; references can, and reflection follows them where
            // Unity's serializer would have stopped.
            if (!type.IsValueType && !path.Add(value))
            {
                return null;
            }

            try
            {
                var lowered = new Dictionary<string, object>();
                foreach (var field in GetSerializedFields(type))
                {
                    lowered[field.Name] = Lower(field.GetValue(value), depth + 1, path);
                }

                return lowered;
            }
            finally
            {
                if (!type.IsValueType)
                {
                    path.Remove(value);
                }
            }
        }

        private static Dictionary<string, object> Describe(UnityEngine.Object unityObject)
        {
            // A reference, never its contents: one GameObject field would otherwise drag the whole
            // scene back into the payload.
            return new Dictionary<string, object>
            {
                { "instanceId", unityObject.GetInstanceID() },
                { "name", unityObject.name },
                { "type", unityObject.GetType().FullName }
            };
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            bool IEqualityComparer<object>.Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
