using Amazon.DynamoDBv2.DocumentModel;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Persistence.Common.AWS.Builder;
using System.Collections.Concurrent;
using System.Collections;
using System.Text.Json;
using Utils.Common.Crypto;

namespace Persistence.Common.AWS.EntityManagement
{
    public static class EntityDocumentConverter
    {
        public static ICryptoProvider? CryptoProvider { get; private set; }

        public static void Initialize(ICryptoProvider cryptoProvider)
        {
            CryptoProvider = cryptoProvider ?? throw new ArgumentNullException(nameof(cryptoProvider));
        }

        public static Document ToConfiguredDocument<TEntity>(TEntity entity) where TEntity : DocEntity
        {
            var entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<TEntity>();

            var document = DynamoDBEntityConverter.ConvertToDocument(entity);

            if (document is null)
            {
                var json = JsonSerializer.Serialize(entity, CreateJsonSerializerOptions());
                document = Document.FromJson(json);
            }

            if (entityConfiguration is not null)
            {
                ProcessNestedConfigurations(document, entityConfiguration,
                    (Action<Document, List<PropertyConfiguration>, object?>)((doc, configs, ent) => UpdateToDocument(doc, configs, ent)),
                    entity);
            }

            return document;
        }

        public static TEntity FromConfiguredDocument<TEntity>(Document document) where TEntity : DocEntity
        {
            var entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<TEntity>();

            if (entityConfiguration is not null)
            {
                ProcessNestedConfigurations<TEntity>(document, entityConfiguration,
                    (Action<Document, List<PropertyConfiguration>>)((doc, configs) => UpdateFromDocument(doc, configs)));
            }
            return DynamoDBEntityConverter.ConvertToEntity<TEntity>(document);
        }

        private static void ProcessNestedConfigurations<TEntity>(
            Document document,
            IDynamoDBEntityBuilder entityConfiguration,
            Delegate updateFunc,
            TEntity? entity = default)
        {
            var propertyConfigurations = entityConfiguration.GetPropertyConfigurations();
            if (updateFunc is Action<Document, List<PropertyConfiguration>, object?> toDocFunc)
            {
                toDocFunc(document, propertyConfigurations, entity);
            }
            else if (updateFunc is Action<Document, List<PropertyConfiguration>> fromDocFunc)
            {
                fromDocFunc(document, propertyConfigurations);
            }

            var nestedConfigurations = entityConfiguration.GetNestedEntityConfigurations();
            foreach (var nestedConfiguration in nestedConfigurations)
            {
                object? nestedEntity = !object.Equals(entity, default(TEntity)) ?
                GetNestedEntity(entity, nestedConfiguration.PropertyName, nestedConfiguration.BreakingPropertyName)
                : null;

                ProcessNestedConfiguration(document, nestedConfiguration, updateFunc, nestedEntity);
            }
        }

        private static object? GetNestedEntity(object? entity, string propertyName, string? breakingPropertyName)
        {
            var entityType = entity!.GetType();

            var nestedProperty = entityType.GetProperty(propertyName);
            var nestedEntity = nestedProperty?.GetValue(entity);

            if (nestedEntity is null && !string.IsNullOrWhiteSpace(breakingPropertyName))
            {
                var breakingProperty = entityType.GetProperty(breakingPropertyName);
                nestedEntity = breakingProperty?.GetValue(entity);
            }
            return nestedEntity;
        }

        private static void ProcessNestedConfiguration(
            Document document,
            NestedConfiguration nestedConfiguration,
            Delegate updateFunc,
            object? entity = null)
        {
            string propertyName = nestedConfiguration.PropertyName;

            if (!document.Contains(propertyName))
            {
                object? nestedEntity = entity is not null ?
                GetNestedEntity(entity, nestedConfiguration.PropertyName, nestedConfiguration.BreakingPropertyName)
                : null;

                if (nestedEntity is null)
                {
                    return;
                }
                else
                {
                    var json = JsonSerializer.Serialize(nestedEntity, CreateJsonSerializerOptions());

                    SetDocumentPropertyValue(document,
                     propertyName,
                     nestedEntity.GetType(), json);
                }
            }

            var nestedEntityValue = document[propertyName];

            if (nestedEntityValue is Document nestedDocument)
            {
                UpdateNestedDocument(document, propertyName, nestedDocument, nestedConfiguration, updateFunc, entity);
            }
            else if (nestedEntityValue is DynamoDBList nestedList)
            {
                UpdateNestedList(document, propertyName, nestedList, nestedConfiguration, updateFunc, entity);
            }
        }

        private static void UpdateNestedDocument(
        DynamoDBEntry documentEntry,
        string propertyName,
        Document nestedDocument,
        NestedConfiguration nestedConfiguration,
        Delegate updateFunc,
        object? entity = null)
        {
            if (documentEntry is Document document)
            {
                // Call the correct update function based on the delegate type
                if (updateFunc is Action<Document, List<PropertyConfiguration>, object?> toDocFunc)
                {
                    toDocFunc(nestedDocument, nestedConfiguration.Configurations, entity);
                }
                else if (updateFunc is Action<Document, List<PropertyConfiguration>> fromDocFunc)
                {
                    fromDocFunc(nestedDocument, nestedConfiguration.Configurations);
                }

                foreach (var childNestedConfiguration in nestedConfiguration.NestedConfigurations)
                {
                    ProcessNestedConfiguration(nestedDocument, childNestedConfiguration, updateFunc, entity);
                }

                document[propertyName] = nestedDocument;
            }
            else
            {
                throw new InvalidOperationException($"The provided DynamoDBEntry is not of type Document.");
            }
        }

        private static void UpdateNestedList(
            DynamoDBEntry documentEntry,
            string propertyName,
            DynamoDBEntry nestedList,
            NestedConfiguration nestedConfiguration,
            Delegate updateFunc,
            object? entity = null)
        {
            if (documentEntry is Document document)
            {
                var processedList = new DynamoDBList();

                foreach (var item in nestedList.AsListOfDocument())
                {
                    if (updateFunc is Action<Document, List<PropertyConfiguration>, object?> toDocFunc)
                    {
                        toDocFunc(item, nestedConfiguration.Configurations, entity);
                    }
                    else if (updateFunc is Action<Document, List<PropertyConfiguration>> fromDocFunc)
                    {
                        fromDocFunc(item, nestedConfiguration.Configurations);
                    }

                    foreach (var childNestedConfiguration in nestedConfiguration.NestedConfigurations)
                    {
                        ProcessNestedConfiguration(item, childNestedConfiguration, updateFunc, entity);
                    }

                    processedList.Add(item);
                }

                document[propertyName] = processedList;
            }
            else
            {
                throw new InvalidOperationException($"The provided DynamoDBEntry is not of type Document.");
            }
        }

        private static void UpdateToDocument(
            Document document,
            List<PropertyConfiguration> propertyConfigurations,
            object? entity = null)
        {
            foreach (var propertyConfiguration in propertyConfigurations)
            {
                if (document.Contains(propertyConfiguration.PropertyName))
                {
                    var propertyName = propertyConfiguration.PropertyName;
                    if (!string.IsNullOrWhiteSpace(propertyConfiguration.JsonPropertyName))
                    {
                        var value = document[propertyName];
                        document.Remove(propertyName);
                        document[propertyConfiguration.JsonPropertyName] = value;
                        propertyName = propertyConfiguration.JsonPropertyName;
                    }

                    if (propertyConfiguration.IsIgnoreEnable)
                    {
                        document.Remove(propertyName);
                    }

                    if (propertyConfiguration.HasEncryption)
                    {
                        HandleEncryption(document, propertyName);
                    }

                    if (propertyConfiguration.ValueConverter is ValueConverter converter && entity != null)
                    {
                        HandleToJsonConversion(document, propertyName, converter, entity);
                    }
                }
                else if (!propertyConfiguration.IsIgnoreEnable && entity is not null)
                {
                    HandleBrakingProperty(document, propertyConfiguration, entity);
                }
            }
        }

        private static void HandleBrakingProperty(Document document,
            PropertyConfiguration propertyConfiguration,
            object entity)
        {
            var propertyName = propertyConfiguration.PropertyName;
            object? entityValue;

            var property = entity.GetType().GetProperty(propertyName);
            if (property == null)
            {
                return;
            }

            entityValue = property.GetValue(entity);

            if (entityValue is null && !string.IsNullOrWhiteSpace(propertyConfiguration.BreakingPropertyName))
            {
                var breakingProperty = entity.GetType().GetProperty(propertyConfiguration.BreakingPropertyName);
                if (breakingProperty == null)
                {
                    return;
                }

                entityValue = breakingProperty.GetValue(entity);
            }

            if (entityValue is null)
            {
                return;
            }

            if (propertyConfiguration.ValueConverter is ValueConverter converter)
            {
                var convertedValue = converter.ConvertToProvider.Invoke(entityValue);

                if (convertedValue == null)
                {
                    return;
                }

                document[propertyName] = new Primitive(convertedValue.ToString());
            }
            else
            {
                var json = JsonSerializer.Serialize(entityValue, CreateJsonSerializerOptions());

                if (IsJson(json))
                {
                    SetDocumentPropertyValue(document,
                    propertyConfiguration.JsonPropertyName ?? propertyName,
                    propertyConfiguration.PropertyType, json);
                }
                else
                {
                    var dynamoDBEntry = ConvertJsonElementToDynamoDBEntry(JsonDocument.Parse(json).RootElement);
                    document[propertyName] = dynamoDBEntry;
                }

                if (propertyConfiguration.HasEncryption)
                {
                    HandleEncryption(document, propertyName);
                }
            }
        }

        private static void HandleEncryption(DynamoDBEntry documentEntry, string propertyName)
        {
            if (documentEntry is Document document)
            {
                var value = document[propertyName];
                if (value != null && !(value is DynamoDBNull))
                {
                    var encryptedValue = CryptoProvider?.EncryptString(value);
                    document[propertyName] = new Primitive(encryptedValue);
                }
            }
        }

        private static void HandleToJsonConversion(
            DynamoDBEntry documentEntry,
            string propertyName,
            ValueConverter converter,
            object entity)
        {
            if (documentEntry is not Document document)
            {
                return;
            }

            var value = document[propertyName];
            if (value == null || value is DynamoDBNull)
            {
                return;
            }

            var property = entity.GetType().GetProperty(propertyName);
            if (property == null)
            {
                return;
            }

            var entityValue = property.GetValue(entity);
            var convertedValue = converter.ConvertToProvider.Invoke(entityValue);
            if (convertedValue == null)
            {
                return;
            }

            document.Remove(propertyName);
            document[propertyName] = new Primitive(convertedValue.ToString());
        }

        private static void UpdateFromDocument(
            Document document,
            IEnumerable<PropertyConfiguration> propertyConfigurations)
        {
            foreach (var propertyConfiguration in propertyConfigurations
                     .Where(e => !string.IsNullOrWhiteSpace(e.JsonPropertyName)
                                 || e.HasEncryption
                                 || e.ValueConverter is not null))
            {
                var propertyName = propertyConfiguration.JsonPropertyName ?? propertyConfiguration.PropertyName;

                if (document.Contains(propertyName))
                {
                    if (!string.IsNullOrWhiteSpace(propertyConfiguration.JsonPropertyName))
                    {
                        var value = document[propertyConfiguration.JsonPropertyName];
                        document.Remove(propertyConfiguration.JsonPropertyName);
                        document[propertyConfiguration.PropertyName] = value;
                        propertyName = propertyConfiguration.PropertyName;
                    }

                    if (propertyConfiguration.HasEncryption)
                    {
                        HandleDecryption(document, propertyName);
                    }

                    if (propertyConfiguration.ValueConverter is not null)
                    {
                        HandleFromJsonConversion(document, propertyName, propertyConfiguration.PropertyType);
                    }
                }
            }
        }

        private static void HandleDecryption(DynamoDBEntry documentEntry, string propertyName)
        {
            if (documentEntry is Document document)
            {
                var value = document[propertyName];
                if (value != null && !(value is DynamoDBNull))
                {
                    var decryptedValue = CryptoProvider?.DecryptString(value);
                    document[propertyName] = new Primitive(decryptedValue);
                }
            }
        }

        private static void HandleFromJsonConversion(DynamoDBEntry documentEntry, string propertyName, Type? propertyType)
        {
            if (documentEntry is Document document)
            {
                var value = document[propertyName];

                if (value is null || value is DynamoDBNull || !IsJson(value.AsString()))
                {
                    return;
                }

                SetDocumentPropertyValue(document, propertyName, propertyType, value.AsString());
            }
        }

        private static void SetDocumentPropertyValue(Document document, string propertyName, Type? propertyType, string jsonValue)
        {
            if (typeof(IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string))
            {
                var dynamoDbList = ParseJsonArrayToDynamoDBList(jsonValue);
                if (dynamoDbList is not null)
                {
                    if (document.Contains(propertyName))
                    {
                        document.Remove(propertyName);
                    }
                    document[propertyName] = dynamoDbList;
                }
            }
            else
            {
                var dynamoDbEntry = Document.FromJson(jsonValue);
                if (dynamoDbEntry is not null)
                {
                    if (document.Contains(propertyName))
                    {
                        document.Remove(propertyName);
                    }
                    document[propertyName] = dynamoDbEntry;
                }
            }
        }

        private static DynamoDBList ParseJsonArrayToDynamoDBList(string jsonArray)
        {
            var dynamoDbList = new DynamoDBList();

            try
            {
                // Parse the JSON array as an array of JsonElement
                var elements = JsonSerializer.Deserialize<JsonElement[]>(jsonArray);

                if (elements is null)
                {
                    throw new InvalidOperationException("Invalid JSON format.");
                }

                foreach (var element in elements)
                {
                    // Use the helper method to convert the JsonElement to DynamoDBEntry
                    var dynamoDbEntry = ConvertJsonElementToDynamoDBEntry(element);
                    dynamoDbList.Add(dynamoDbEntry);
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Invalid JSON format.", ex);
            }

            return dynamoDbList;
        }

        /// <summary>
        /// Converts a JsonElement to a DynamoDBEntry based on its ValueKind.
        /// </summary>
        private static DynamoDBEntry ConvertJsonElementToDynamoDBEntry(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return new Primitive(element.GetString());

                case JsonValueKind.Number:
                    return new Primitive(element.ToString(), true);

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return new DynamoDBBool(element.GetBoolean());

                case JsonValueKind.Object:
                    return Document.FromJson(element.GetRawText());

                case JsonValueKind.Array:
                    return ParseJsonArrayToDynamoDBList(element.GetRawText());

                case JsonValueKind.Null:
                    return new DynamoDBNull();

                default:
                    throw new InvalidOperationException("Unsupported JSON value type.");
            }
        }

        private static bool IsJson(string source)
        {
            try
            {
                if ((source.StartsWith('{') && source.EndsWith('}')) || (source.StartsWith('[') && source.EndsWith(']')))
                {
                    JsonDocument.Parse(source);
                    return true;
                }
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static IEnumerable<TEntity> FromConfiguredDocuments<TEntity>(IEnumerable<Document> documents) where TEntity : DocEntity
        {
            ConcurrentBag<TEntity> entities = new ConcurrentBag<TEntity>();

            foreach (var document in documents)
            {
                entities.Add(FromConfiguredDocument<TEntity>(document));
            }

            return entities;
        }

        private static JsonSerializerOptions CreateJsonSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            // Add the EnumJsonConverter or other custom converters
            // options.Converters.Add(new EnumJsonConverter());

            // Add other converters as needed here
            return options;
        }
    }
}
