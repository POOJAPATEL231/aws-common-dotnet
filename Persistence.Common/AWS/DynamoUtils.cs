using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;
using Domain.Common.Entities;
using Persistence.Common.AWS.Builder;
using System.Reflection;

namespace Persistence.Common.AWS
{
    public static class DynamoUtils
    {
        public static string GenerateId(DocEntity entity) => $"{entity.PartitionKey}:{Guid.NewGuid()}";

        public static string GenerateId(string partitionKey) => $"{partitionKey}:{Guid.NewGuid()}";

        public static string ResolvePartitionKey(string entityId) =>
            entityId.Contains(':') ? entityId[..entityId.LastIndexOf(':')] : "";

        public static string ResolveIdFromPartitionKey(string partitionKey) => partitionKey.Split(':')[^1];

        internal static DynamoDBContext CreateDynamoDBContext(this IAmazonDynamoDB amazonDynamoDB)
        {
            // With V1 BOOL getting stored as integers ,so useing V2 to Proper handling of bool types, which are now stored as DynamoDB's native BOOL type instead of being converted to integers.
            // https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/DynamoDBv2/TDynamoDBEntryConversion.html
            var dynamoDBContextConfig = new DynamoDBContextConfig
            {
                Conversion = DynamoDBEntryConversion.V2,
                IgnoreNullValues = true,
                RetrieveDateTimeInUtc = true
            };

            var dBContext = new DynamoDBContext(amazonDynamoDB, dynamoDBContextConfig);

            return dBContext;
        }

        internal static string GetTableName<T>() where T : DocEntity
        {
            var entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<T>();
            if (entityConfiguration is not null)
            {
                return entityConfiguration.GetTableName();
            }

            var tableAttribute = typeof(T).GetCustomAttribute<DynamoDBTableAttribute>();
            return tableAttribute != null ? tableAttribute.TableName : typeof(T).Name;
        }

        internal static Dictionary<string, AttributeValue> GetKeyAttributeValueDictionary<T>(this T document) where T : DocEntity
        {
            var keyAttributeValueDictionary = new Dictionary<string, AttributeValue>();

            var hashKeyProperty = ResolveHashKeyProperty<T>();

            if (hashKeyProperty.Property is null || string.IsNullOrWhiteSpace(hashKeyProperty.PropertyName))
            {
                throw new ArgumentException("Provided object is missing key attribute");
            }

            var hashValue = document.GetPropertyValue(hashKeyProperty.Property.Name);

            if (hashValue is not null)
            {
                keyAttributeValueDictionary[hashKeyProperty.PropertyName] = new AttributeValue { S = hashValue.ToString() };
            }

            var rangeKeyProperty = ResolveRangeKeyProperty<T>();

            if (rangeKeyProperty.Property is not null && !string.IsNullOrWhiteSpace(rangeKeyProperty.PropertyName))
            {
                var rangeValue = document.GetPropertyValue(rangeKeyProperty.Property.Name);
                if (rangeValue is not null)
                {
                    keyAttributeValueDictionary[rangeKeyProperty.PropertyName] = new AttributeValue { S = rangeValue.ToString() };
                }
            }

            return keyAttributeValueDictionary;
        }

        internal static Dictionary<string, AttributeValue> GetKeyAttributeValueDictionary<T>(string hash, string? range = null) where T : DocEntity
        {
            var keyAttributeValueDictionary = new Dictionary<string, AttributeValue>();

            var hashKeyPropertyName = ResolveHashKeyPropertyName<T>();

            if (string.IsNullOrWhiteSpace(hashKeyPropertyName))
            {
                throw new ArgumentException("Provided object is missing key attribute");
            }

            keyAttributeValueDictionary[hashKeyPropertyName] = new AttributeValue { S = hash };

            if (!string.IsNullOrWhiteSpace(range))
            {
                var rangeKeyPropertyName = ResolveRangeKeyPropertyName<T>();

                if (!string.IsNullOrWhiteSpace(rangeKeyPropertyName))
                {
                    keyAttributeValueDictionary[rangeKeyPropertyName] = new AttributeValue { S = range };
                }
            }
            return keyAttributeValueDictionary;
        }

        public static object? GetPropertyValue<T>(this T document, string name) where T : DocEntity
        {
            var propertyInfo = document.GetType().GetProperty(name);
            return propertyInfo?.GetValue(document);
        }

        public static (PropertyInfo? Property, string? PropertyName) ResolveHashKeyProperty<T>() where T : DocEntity
        {
            var entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<T>();
            PropertyInfo? hashKeyPropertyName = null;
            string? jsonPropertyName = null;
            if (entityConfiguration is not null)
            {
                var propertyConfigurations = entityConfiguration.GetPropertyConfigurations();
                var propertyConfiguration = propertyConfigurations.Find(e => e.IsPartitionKey);
                var propertyName = propertyConfiguration?.PropertyName;
                jsonPropertyName = propertyConfiguration?.JsonPropertyName;
                if (propertyName is not null)
                {
                    hashKeyPropertyName = typeof(T).GetProperty(propertyName);
                }
            }

            hashKeyPropertyName ??= typeof(T).GetProperty(nameof(DocEntity.PartitionKey));

            var hashKeyProperty = Array.Find(typeof(T).GetProperties(), p => Attribute.IsDefined(p, typeof(DynamoDBHashKeyAttribute)))
                                ?? hashKeyPropertyName;

            return (hashKeyProperty, jsonPropertyName ?? hashKeyProperty?.Name);
        }

        public static string? ResolveHashKeyPropertyName<T>() where T : DocEntity
        {
            var (_, propertyName) = ResolveHashKeyProperty<T>();
            return propertyName;
        }

        public static string? ResolveRangeKeyPropertyName<T>() where T : DocEntity
        {
            var (_, propertyName) = ResolveRangeKeyProperty<T>();
            return propertyName;
        }

        public static (PropertyInfo? Property, string? PropertyName) ResolveRangeKeyProperty<T>() where T : DocEntity
        {
            var entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<T>();
            PropertyInfo? rangeKeyPropertyName = null;
            string? jsonPropertyName = null;

            if (entityConfiguration is not null)
            {
                var propertyConfigurations = entityConfiguration.GetPropertyConfigurations();
                var propertyConfiguration = propertyConfigurations.Find(e => e.IsPrimaryKey);
                var propertyName = propertyConfiguration?.PropertyName;
                jsonPropertyName = propertyConfiguration?.JsonPropertyName;
                if (propertyName is not null)
                {
                    rangeKeyPropertyName = typeof(T).GetProperty(propertyName);
                }
            }

            rangeKeyPropertyName ??= typeof(T).GetProperty(nameof(DocEntity.Id));

            var rangeKeyProperty = Array.Find(typeof(T).GetProperties(), p => Attribute.IsDefined(p, typeof(DynamoDBRangeKeyAttribute)))
                                ?? rangeKeyPropertyName;

            return (rangeKeyProperty, jsonPropertyName ?? rangeKeyProperty?.Name);
        }

        public static (string KeyConditionExpression, Dictionary<string, AttributeValue> ExpressionAttributeValues) CreateKeyFilter<T>(string hashKeyValue) where T : DocEntity
        {
            var hashKeyPropertyName = ResolveHashKeyPropertyName<T>();

            if (string.IsNullOrWhiteSpace(hashKeyPropertyName))
            {
                throw new InvalidOperationException("Hash key property not found.");
            }

            var keyConditionExpression = $"{hashKeyPropertyName} = :hashKeyValue";
            var expressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":hashKeyValue", new AttributeValue { S = hashKeyValue } }
            };

            return (keyConditionExpression, expressionAttributeValues);
        }
    }
}
