using Persistence.Common.AWS.Builder;
using System.Collections.Concurrent;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Linq.Expressions;

namespace Persistence.Common.AWS.EntityManagement
{
    public class PropertyValues
    {
        private static readonly ConcurrentDictionary<Type, PropertyMetadata[]> _propertyCache = new();
        private static readonly ConcurrentDictionary<Type, Action<object, object?[]>> _captureDelegates = new();

        private readonly Type _entityType;
        private readonly PropertyMetadata[] _properties;
        private readonly object?[] _values;
        private readonly List<int> _simplePropertyIndices;
        private readonly List<int> _complexPropertyIndices;

        public PropertyValues(Type entityType)
        {
            _entityType = entityType;
            _properties = GetProperties(entityType);
            _values = new object?[_properties.Length];
            BuildOrGetCaptureDelegate(entityType);
            // Separate the indices of simple and complex properties
            _simplePropertyIndices = _properties
                .Select((prop, index) => new { prop, index })
                .Where(p => p.prop.IsSimpleType)
                .Select(p => p.index)
                .ToList();

            _complexPropertyIndices = _properties
                .Select((prop, index) => new { prop, index })
                .Where(p => !p.prop.IsSimpleType)
                .Select(p => p.index)
                .ToList();
        }

        // Access values by index
        public object? this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }

        // Method to capture property values using a precompiled delegate
        public void Capture(object entity, List<PropertyConfiguration>? propertyConfigurations, List<NestedConfiguration>? nestedEntityConfigurations)
        {
            // Use the delegate built for the DECLARED type (matching _properties/_values shape).
            // Indexing by entity.GetType() would throw KeyNotFoundException for derived
            // instances and, worse, use a delegate whose property layout doesn't match.
            var captureDelegate = _captureDelegates[_entityType];
            captureDelegate(entity, _values);

            // For complex properties, store only the identifier or reference identity
            foreach (var index in _complexPropertyIndices)
            {
                CaptureComplexProperty(propertyConfigurations, nestedEntityConfigurations, index);
            }
        }

        private void CaptureComplexProperty(List<PropertyConfiguration>? propertyConfigurations, List<NestedConfiguration>? nestedEntityConfigurations, int index)
        {
            var originalValue = _values[index];
            var properly = _properties[index];
            if (originalValue is not null)
            {
                if (propertyConfigurations?.Exists(e => e.PropertyName == properly.Name && e.IsIgnoreEnable) == true)
                {
                    _values[index] = null;
                }
                else if (nestedEntityConfigurations?.Exists(e => e.PropertyName == properly.Name) == true)
                {
                    var nestedConfigurations = nestedEntityConfigurations.Find(e => e.PropertyName == properly.Name);
                    _values[index] = CaptureChild(nestedConfigurations, index, originalValue);
                }
                else
                {
                    _values[index] = ExtractIdentifierOrReference(originalValue);
                }
            }
        }

        private object? CaptureChild(NestedConfiguration? nestedConfigurations, int index, object originalValue)
        {
            if (nestedConfigurations?.IsCollection == true && originalValue is IEnumerable enumerable)
            {
                var propertyType = _properties[index].PropertyType.IsGenericType
                    ? _properties[index].PropertyType.GetGenericArguments()[0]
                    : typeof(object);
                var propertyValuesList = new List<PropertyValues>();
                foreach (var value in enumerable)
                {
                    var propertyValues = new PropertyValues(propertyType); // Create a new PropertyValues for complex properties
                    propertyValues.Capture(value, nestedConfigurations.Configurations, nestedConfigurations.NestedConfigurations); // Recursively capture the state
                    propertyValuesList.Add(propertyValues);
                }
                return propertyValuesList;
            }
            else
            {
                var propertyType = _properties[index].PropertyType;
                var propertyValues = new PropertyValues(propertyType); // Create a new PropertyValues for complex properties
                propertyValues.Capture(originalValue, nestedConfigurations?.Configurations, nestedConfigurations?.NestedConfigurations); // Recursively capture the state
                return propertyValues;
            }
        }

        private static object? ExtractIdentifierOrReference(object reference)
        {
            var type = reference.GetType();

            // Attempt to get an ID or unique identifier
            var idProperty = type.GetProperty("ID") ?? type.GetProperty("Id");
            if (idProperty != null)
            {
                return idProperty.GetValue(reference);
            }

            // For collections, create a list of identifiers
            if (reference is IEnumerable enumerable)
            {
                return enumerable.Cast<object>()
                                 .Select(item => ExtractIdentifierOrReference(item) ?? item)
                                 .ToList();
            }

            // Fall back to a serialized snapshot. Storing the live reference here would
            // alias the original snapshot to the mutable object, making any later
            // mutation invisible to change detection.
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(reference, reference.GetType());
            }
            catch
            {
                // Non-serializable object: reference identity is the best we can do.
                return reference;
            }
        }

        // Method to compare with another PropertyValues instance
        public bool HasChanges(PropertyValues currentValues)
        {
            // Check simple properties first
            foreach (var index in _simplePropertyIndices)
            {
                if (!Equals(this[index], currentValues[index]))
                {
                    return true;
                }
            }

            // Check complex properties next
            foreach (var index in _complexPropertyIndices)
            {
                var originalValue = this[index];
                var currentValue = currentValues[index];

                if (!AreEqual(originalValue, currentValue, _properties[index].PropertyType))
                {
                    return true;
                }
            }

            return false;
        }

        // Enhanced comparison for objects with optimized handling of simple types and collections
        private static bool AreEqual(object? original, object? current, Type propertyType)
        {
            if (original == null && current == null)
            {
                return true;
            }

            if (original == null || current == null)
            {
                return false;
            }

            // For simple types, use direct equality or memory comparison
            if (PropertyMetadata.IsSimpleTypeCheck(propertyType) || IsRecordType(propertyType))
            {
                return original.Equals(current);
            }

            // If both are PropertyValues, compare them recursively
            if (original is PropertyValues originalValues && current is PropertyValues currentValues)
            {
                return !originalValues.HasChanges(currentValues);
            }

            // Handle collections (arrays, lists) with optimized comparisons
            if (typeof(IEnumerable<object>).IsAssignableFrom(propertyType))
            {
                return CompareCollections((IEnumerable<object>)original, (IEnumerable<object>)current);
            }

            // Handle other complex types by recursively comparing properties
            return CompareComplexObjects(original, current);
        }

        private static bool CompareComplexObjects(object original, object current)
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
                };

                // Serialize both entities to JSON strings
                var originalJson = System.Text.Json.JsonSerializer.Serialize(original, options);
                var currentJson = System.Text.Json.JsonSerializer.Serialize(current, options);

                // Equal JSON means the objects are equal. (This was previously negated,
                // which made every content change to a complex property invisible to
                // change tracking - silent lost updates.)
                return string.Equals(originalJson, currentJson, StringComparison.Ordinal);
            }
            catch
            {
                return original.Equals(current);
            }
        }

        private static bool CompareCollections(IEnumerable<object> original, IEnumerable<object> current)
        {
            var originalList = original.ToList();
            var currentList = current.ToList();

            if (originalList.Count != currentList.Count)
            {
                return false;
            }

            for (int i = 0; i < originalList.Count; i++)
            {
                if (!AreEqual(originalList[i], currentList[i], originalList[i]?.GetType() ?? typeof(object)))
                {
                    return false;
                }
            }
            return true;
        }

        // Build or retrieve a precompiled capture delegate for the type
        private void BuildOrGetCaptureDelegate(Type entityType)
        {
            if (!_captureDelegates.ContainsKey(entityType))
            {
                var entityParam = Expression.Parameter(typeof(object), "entity");
                var valuesParam = Expression.Parameter(typeof(object?[]), "values");

                var convertedEntity = Expression.Convert(entityParam, entityType);
                var blockExpressions = new List<Expression>();

                for (int i = 0; i < _properties.Length; i++)
                {
                    var property = _properties[i];

                    var getPropertyValue = Expression.Invoke(Expression.Constant(property.GetValue), convertedEntity);

                    var assignValue = Expression.Assign(
                        Expression.ArrayAccess(valuesParam, Expression.Constant(i)),
                        Expression.Convert(getPropertyValue, typeof(object))
                    );

                    blockExpressions.Add(assignValue);
                }

                var block = Expression.Block(blockExpressions);
                var lambda = Expression.Lambda<Action<object, object?[]>>(block, entityParam, valuesParam);

                var compiledLambda = lambda.Compile();
                _captureDelegates[entityType] = compiledLambda;
            }
        }

        [SuppressMessage("Major Code Smell",
        "S3011:Make sure that this accessibility bypass is safe here",
        Justification = "Accessing non-public properties is necessary for setting values in derived classes.")]
        private static bool IsRecordType(Type type)
        {
            // Check for the EqualityContract property, a strong indicator of a record type
            bool hasEqualityContract = type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) != null;

            // Check if Equals and GetHashCode are overridden (typical of record types)
            bool hasOverrides = type.GetMethod("Equals", new[] { typeof(object) })?.DeclaringType == type
                                && type.GetMethod("GetHashCode")?.DeclaringType == type;

            return hasEqualityContract && hasOverrides;
        }

        // Cache the properties and retrieve them efficiently
        [SuppressMessage("Major Code Smell",
        "S3011:Make sure that this accessibility bypass is safe here",
        Justification = "Accessing non-public properties is necessary for setting values in derived classes.")]
        private static PropertyMetadata[] GetProperties(Type entityType)
        {
            if (_propertyCache.TryGetValue(entityType, out var cachedProperties))
            {
                return cachedProperties;
            }

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(p => p.CanRead)
                .Select(PropertyMetadata.Create)
                .ToArray();

            _propertyCache[entityType] = properties;
            return properties;
        }
    }
}
