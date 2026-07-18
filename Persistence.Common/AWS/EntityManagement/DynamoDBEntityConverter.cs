using Amazon.DynamoDBv2.DocumentModel;
using Persistence.Common.AWS.Builder;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Persistence.Common.AWS.EntityManagement
{
    public static class DynamoDBEntityConverter
    {
        // Cache for storing properties for each type
        // ConcurrentDictionary: this static cache is read and written from concurrent
        // requests; a plain Dictionary corrupts under concurrent mutation.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _propertyCache = new();

        public static T ConvertToEntity<T>(Document document) where T : class
        {
            // Create an instance of the entity using the default parameterless constructor
            var entityType = typeof(T);

            // Find the most suitable constructor and match parameters
            var constructor = FindMatchingConstructor(entityType, document, out var constructorParamNames, out var constructorParams);

            T entity = constructor != null
                        ? (T)constructor.Invoke(constructorParams)
                        : (T)CreateInstance(entityType);

            // Get properties for the entity type from the cache
            var properties = GetPropertiesForType(entityType);

            // Set properties directly from the cached dictionary
            foreach (var kvp in document)
            {
                var attributeName = kvp.Key;
                var entryValue = kvp.Value;

                if (entryValue == null || entryValue is DynamoDBNull)
                {
                    continue; // Skip setting this property
                }

                // Check if the property exists in the cached dictionary
                if (properties.TryGetValue(attributeName, out var property))
                {
                    // If the property was set via the constructor, skip setting it again
                    if (constructorParamNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Convert the value to the appropriate type and set it
                    var convertedValue = ConvertValue(property.PropertyType, entryValue);

                    // Set the property value using reflection
                    if (convertedValue is not null)
                    {
                        SetValueWithAccessHandling(entity, property, convertedValue);
                    }
                }
            }

            return entity;
        }

        public static Document? ConvertToDocument<T>(T entity) where T : class
        {
            if (entity is not null)
            {
                var entityType = typeof(T);

                var properties = GetPropertiesForType(entityType);

                if (properties is { Count: > 0 })
                {
                    var jsonDocument = ConvertEntityToJsonWithSelectedProperties(entity, properties.Select(e => e.Value).ToList());
                    return Document.FromJson(jsonDocument);
                }
            }
            return null;
        }

        public static string ConvertEntityToJsonWithSelectedProperties(object entity, List<PropertyInfo> selectedProperties)
        {
            // Construct a dictionary of selected properties
            var selectedValues = new Dictionary<string, object?>();
            foreach (var property in selectedProperties)
            {
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0) // Skip indexers
                {
                    var value = property.GetValue(entity);
                    selectedValues[property.Name] = value;
                }
            }

            // Serialize the dictionary to JSON using System.Text.Json
            var options = new JsonSerializerOptions
            {
                WriteIndented = true, // Pretty print
            };

            return JsonSerializer.Serialize(selectedValues, options);
        }

        private static Dictionary<string, PropertyInfo> GetPropertiesForType(Type type)
        {
            // Check if the type is already in the cache
            if (_propertyCache.TryGetValue(type, out var cachedProperties))
            {
                return cachedProperties;
            }

            var entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration(type);
            return GetPropertiesForType(type, entityConfiguration?.GetPropertyConfigurations(), entityConfiguration?.GetNestedEntityConfigurations());
        }

        private static Dictionary<string, PropertyInfo> GetPropertiesForType(Type type,
                IEnumerable<PropertyConfiguration>? propertyConfigurations,
                IEnumerable<NestedConfiguration>? nestedConfigurations)
        {
            // Check if the type is already in the cache
            if (_propertyCache.TryGetValue(type, out var cachedProperties))
            {
                return cachedProperties;
            }

            // Gather all properties from the type and its base types
            var properties = new Dictionary<string, PropertyInfo>();


            //var entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<T>();
            var (configuredProperties, ignoreProperties) = GetConfiguredAndIgnoredProperties(propertyConfigurations, nestedConfigurations);

            // Traverse the type hierarchy to get properties from base classes as well
            for (var currentType = type; currentType != null && currentType != typeof(object); currentType = currentType.BaseType)
            {
                var typeProperties = currentType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                foreach (var property in typeProperties)
                {
                    // Add the property to the dictionary if it's not already added (to avoid duplicates from base classes)
                    if (ShouldIncludeProperty(property, configuredProperties, ignoreProperties) && !properties.ContainsKey(property.Name))
                    {
                        properties[property.Name] = property;
                    }
                }
            }

            // Cache the properties for future use
            _propertyCache[type] = properties;

            if (nestedConfigurations is not null)
            {
                foreach (var nestedEntityConfigurations in nestedConfigurations)
                {
                    var nestedEntityType = nestedEntityConfigurations.PropertyType.IsGenericType
                                ? nestedEntityConfigurations.PropertyType.GetGenericArguments()[0]
                                    : nestedEntityConfigurations.PropertyType;

                    GetPropertiesForType(nestedEntityType, nestedEntityConfigurations.Configurations, nestedEntityConfigurations.NestedConfigurations);
                }
            }

            return properties;
        }

        private static bool ShouldIncludeProperty(PropertyInfo property,
                                                  ICollection<string> configuredProperties,
                                                  ICollection<string> ignoreProperties)
        {
            // 1. Ignore properties marked Ignore
            if (ignoreProperties.Contains(property.Name))
            {
                return false;
            }

            // 2. Add property if its in  configured Properties list
            if (configuredProperties.Contains(property.Name))
            {
                return true;
            }

            // 3. Ignore static properties
            if (property.GetMethod?.IsStatic == true)
            {
                return false;
            }

            // 4. Ignore properties without a getter
            if (property.GetMethod == null)
            {
                return false;
            }

            // 5. Ignore properties without a setter
            if (property.SetMethod == null)
            {
                return false;
            }

            return true;
        }

        private static object? ConvertValue(Type targetType, DynamoDBEntry entry)
        {
            if (entry is Primitive primitive)
            {
                return ConvertPrimitive(targetType, primitive);
            }
            if (entry is DynamoDBBool dynamoDBBool)
            {
                return ConvertDynamoDBBool(targetType, dynamoDBBool);
            }
            if (entry is Document entryMap && IsDictionaryType(targetType))
            {
                return ConvertDictionary(entryMap, targetType);
            }
            if (entry is Document doc)
            {
                if (targetType.FullName == "System.Object")
                {
                    string json = doc.ToJson();

                    // Deserialize JSON into a generic object
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var obj = JsonSerializer.Deserialize<object>(json);
                        return obj;
                    }
                    else
                    {
                        return new object();
                    }
                }
                return ConvertDocumentToEntity(targetType, doc);
            }
            if (entry is DynamoDBList dynamoDBList)
            {
                return ConvertList(targetType, dynamoDBList);
            }

            // If no suitable conversion found, return null
            return null;
        }

        private static object? ConvertPrimitive(Type targetType, Primitive primitive)
        {
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (targetType.IsEnum)
            {
                if (Enum.TryParse(targetType, primitive.Value.ToString(), out var enumValue))
                {
                    return enumValue;
                }
                throw new ArgumentException($"Unable to convert '{primitive.Value}' to enum type {targetType.Name}");
            }

            if (targetType == typeof(DateOnly) &&
                DateOnly.TryParse(primitive.Value.ToString(),
                new CultureInfo("en-US"), out DateOnly date))
            {
                return date;
            }


            if (targetType == typeof(DateTime) && DateTime.TryParse(primitive.Value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            {
                return dateTime;
            }

            // Types that do not implement IConvertible would make Convert.ChangeType throw
            // InvalidCastException even though they serialize fine as strings on the write side.
            var stringValue = primitive.Value?.ToString();

            if (targetType == typeof(Guid) && Guid.TryParse(stringValue, out var guid))
            {
                return guid;
            }

            if (targetType == typeof(DateTimeOffset) &&
                DateTimeOffset.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
            {
                return dateTimeOffset;
            }

            if (targetType == typeof(TimeSpan) && TimeSpan.TryParse(stringValue, CultureInfo.InvariantCulture, out var timeSpan))
            {
                return timeSpan;
            }

            if (targetType == typeof(TimeOnly) && TimeOnly.TryParse(stringValue, CultureInfo.InvariantCulture, out var timeOnly))
            {
                return timeOnly;
            }

            return Convert.ChangeType(primitive.Value, targetType, CultureInfo.CreateSpecificCulture("en-US"));
        }

        private static object? ConvertDynamoDBBool(Type targetType, DynamoDBEntry dynamoDBBool)
        {
            if (targetType == typeof(bool) || targetType == typeof(bool?))
            {
                return dynamoDBBool.AsBoolean();
            }

            throw new ArgumentException($"Unable to convert '{dynamoDBBool}' to non-boolean type.");
        }

        private static bool IsDictionaryType(Type targetType)
        {
            return typeof(IDictionary).IsAssignableFrom(targetType) && targetType.IsGenericType;
        }

        private static object? ConvertDictionary(Document entryMap, Type targetType)
        {
            var keyType = targetType.GetGenericArguments()[0];
            var valueType = targetType.GetGenericArguments()[1];
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
            var dictionary = Activator.CreateInstance(dictionaryType) as IDictionary;

            foreach (var kvp in entryMap)
            {
                try
                {
                    var key = Convert.ChangeType(kvp.Key, keyType);
                    var value = ConvertValue(valueType, kvp.Value);
                    dictionary?.Add(key, value);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to convert dictionary key '{kvp.Key}' to {keyType.Name}", ex);
                }
            }

            return dictionary;
        }

        private static object? ConvertDocumentToEntity(Type targetType, Document doc)
        {
            var method = typeof(DynamoDBEntityConverter)
                .GetMethod(nameof(ConvertToEntity), BindingFlags.Static | BindingFlags.Public)
                ?.MakeGenericMethod(targetType);

            return method?.Invoke(null, new object[] { doc });
        }

        private static object? ConvertList(Type targetType, DynamoDBList dynamoDBList)
        {
            if (targetType.IsArray)
            {
                return ConvertArray(targetType, dynamoDBList);
            }
            else if (typeof(IEnumerable).IsAssignableFrom(targetType))
            {
                return ConvertEnumerable(targetType, dynamoDBList);
            }

            return null;
        }

        private static object? ConvertArray(Type targetType, DynamoDBList dynamoDBList)
        {
            var elementType = targetType.GetElementType();
            var array = Array.CreateInstance(elementType!, dynamoDBList.Entries.Count);

            for (int i = 0; i < dynamoDBList.Entries.Count; i++)
            {
                array.SetValue(ConvertValue(elementType!, dynamoDBList[i]), i);
            }

            return array;
        }

        private static object? ConvertEnumerable(Type targetType, DynamoDBList dynamoDBList)
        {
            var elementType = targetType.IsGenericType
                ? targetType.GetGenericArguments()[0]
                : typeof(object);

            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = Activator.CreateInstance(listType) as IList;

            if (list != null)
            {
                foreach (var item in dynamoDBList.Entries)
                {
                    list.Add(ConvertValue(elementType, item));
                }
            }

            return list;
        }

        private static ConstructorInfo? FindMatchingConstructor(
            Type entityType,
            Document document,
            out List<string> constructorParamNames,
            out object?[] constructorParams)
        {
            var constructors = entityType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            constructorParamNames = new List<string>();
            constructorParams = Array.Empty<object>();

            // Convert the document into a case-insensitive dictionary
            var caseInsensitiveDoc = document.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var constructor in constructors)
            {
                if (TryMatchConstructor(constructor, caseInsensitiveDoc, out var paramNames, out var paramValues))
                {
                    constructorParams = paramValues;
                    constructorParamNames = paramNames;
                    return constructor;
                }
            }

            return null;
        }

        private static bool TryMatchConstructor(
            MethodBase constructor,
            IDictionary<string, DynamoDBEntry> caseInsensitiveDoc,
            out List<string> paramNames,
            out object?[] paramValues)
        {
            var parameters = constructor.GetParameters();
            paramValues = new object?[parameters.Length];
            paramNames = new List<string>();

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var paramName = param.Name?.ToUpperInvariant(); // Case-insensitive comparison

                if (!string.IsNullOrWhiteSpace(paramName) &&
                    caseInsensitiveDoc.TryGetValue(paramName, out var entryValue))
                {
                    if (!TryConvertParameter(param, entryValue, out var convertedValue))
                    {
                        return false;
                    }

                    paramValues[i] = convertedValue;
                    paramNames.Add(paramName);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(paramName) && IsNullable(param))
                    {
                        var convertedValue = GetDefaultValue(param.ParameterType);
                        paramValues[i] = convertedValue;
                    }
                    else
                    {
                        return false; // No match found for this parameter
                    }
                }
            }

            return true;
        }

        private static bool TryConvertParameter(
            ParameterInfo param,
            DynamoDBEntry entryValue,
            out object? convertedValue)
        {
            convertedValue = ConvertValue(param.ParameterType, entryValue);

            if (convertedValue != null)
            {
                return true;
            }

            if (IsNullable(param))
            {
                convertedValue = GetDefaultValue(param.ParameterType);
                return true;
            }

            return false;
        }

        private static bool IsNullable(ParameterInfo parameter)
        {
            var nullabilityContext = new NullabilityInfoContext();
            var nullabilityInfo = nullabilityContext.Create(parameter);
            return nullabilityInfo.WriteState == NullabilityState.Nullable;
        }

        private static void SetValueWithAccessHandling<T>(T entity, PropertyInfo property, object value)
        {
            // Get the setter method, allowing access to private setters if necessary
            var setMethod = property.GetSetMethod(true);

            if (setMethod != null)
            {
                setMethod.Invoke(entity, new[] { value });
            }
            else if (!TryInvokeCustomSetMethod(entity, property, value))
            {
                TrySetConventionalBackingField(entity, property, value);
            }
        }

        private static bool TryInvokeCustomSetMethod<T>(T entity, MemberInfo property, object value)
        {
            // Try to find a method with the name pattern "Set<PropertyName>", e.g., "SetId"
            var setMethodName = $"Set{property.Name}";
            var setMethodViaMethodName = typeof(T).GetMethod(setMethodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (setMethodViaMethodName != null && setMethodViaMethodName.GetParameters().Length == 1)
            {
                // Invoke the found method to set the value
                setMethodViaMethodName.Invoke(entity, new[] { value });
                return true;
            }

            return false;
        }

        private static void TrySetConventionalBackingField<T>(T entity, MemberInfo property, object value)
        {
            // Fallback to naming convention-based backing field lookup
            var backingFieldName = $"_{char.ToLower(property.Name[0])}{property.Name.Substring(1)}";
            var conventionalBackingField = typeof(T).GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (conventionalBackingField != null)
            {
                // Set the value using the found backing field
                conventionalBackingField.SetValue(entity, value);
            }
        }

        private static object CreateInstance(Type targetType)
        {
            try
            {
                return TryCreatePublicInstance(targetType)
                    ?? TryCreateNonPublicInstance(targetType)
                    ?? TryCreateInstanceWithParameters(targetType)
                    ?? throw new InvalidOperationException($"Unable to create an instance of {targetType}.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to create an instance of {targetType}: {ex.Message}", ex);
            }
        }

        private static object? TryCreatePublicInstance(Type targetType)
        {
            try
            {
                return Activator.CreateInstance(targetType);
            }
            catch (MissingMethodException)
            {
                return null;
            }
        }

        private static object? TryCreateNonPublicInstance(Type targetType)
        {
            try
            {
                return Activator.CreateInstance(targetType, true);
            }
            catch
            {
                return null;
            }
        }

        private static object? TryCreateInstanceWithParameters(Type targetType)
        {
            var constructors = targetType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                                          .OrderByDescending(c => c.GetParameters().Length)
                                          .ToArray();

            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                var parameterValues = CreateParameterValues(parameters);

                var obj = constructor.Invoke(parameterValues);
                if (obj != null)
                {
                    return obj;
                }
            }

            return null;
        }

        private static object?[] CreateParameterValues(ParameterInfo[] parameters)
        {
            var parameterValues = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parameterValues[i] = GetDefaultValue(parameters[i].ParameterType);
            }

            return parameterValues;
        }

        private static object? GetDefaultValue(Type type)
        {
            // Provide default values based on the type, for now returning null for reference types, or default(T)
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static (List<string> ConfiguredProperties, List<string> IgnoreProperties) GetConfiguredAndIgnoredProperties(
        IEnumerable<PropertyConfiguration>? propertyConfigurations,
        IEnumerable<NestedConfiguration>? nestedEntityConfigurations)
        {
            var configuredProperties = new List<string>();
            var ignoreProperties = new List<string>();

            if (propertyConfigurations is not null)
            {
                ignoreProperties = propertyConfigurations
                    .Where(e => e.IsIgnoreEnable)
                    .Select(e => e.PropertyName)
                    .ToList();

                configuredProperties = propertyConfigurations
                    .Where(e => !e.IsIgnoreEnable)
                    .Select(e => e.PropertyName)
                    .ToList();

                configuredProperties.AddRange(propertyConfigurations
                    .Where(e => !e.IsIgnoreEnable && !string.IsNullOrWhiteSpace(e.BreakingPropertyName))
                    .Select(e => e.BreakingPropertyName!)
                    .ToList());
            }

            if (nestedEntityConfigurations is not null)
            {
                configuredProperties.AddRange(nestedEntityConfigurations
                    .Select(e => e.PropertyName)
                    .ToList());

                configuredProperties.AddRange(nestedEntityConfigurations
                    .Where(e => !string.IsNullOrWhiteSpace(e.BreakingPropertyName))
                    .Select(e => e.BreakingPropertyName!)
                    .ToList());
            }

            return (configuredProperties, ignoreProperties);
        }
    }
}
