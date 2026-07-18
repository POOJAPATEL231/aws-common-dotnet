using Persistence.Common.Configuration;
using System.Collections.Concurrent;
using System.Reflection;

namespace Persistence.Common.AWS.Builder
{
    public static class InMemoryDynamoDBEntitiesConfiguration
    {
        // Cache for storing the configurations of all entity types
        private static readonly ConcurrentDictionary<Type, IDynamoDBEntityBuilder> _entityConfigurations = new();

        // This method initializes the cache by loading all derived configuration classes at startup
        public static void InitializeConfigurationCache(Assembly assembly)
        {
            // Get all types that derive from DocEntityConfiguration<T>
            var configurationTypes = assembly.GetTypes()
            .Where(IsValidConfigurationType)
            .ToList();

            foreach (var configType in configurationTypes)
            {
                // Get the entity type from the generic argument of DocEntityConfiguration<T>
                var entityType = configType.BaseType?.GetGenericArguments()[0];
                if (entityType is not null)
                {
                    // Create an instance of the configuration class (e.g., OrgConfiguration)
                    var configInstance = Activator.CreateInstance(configType);
                    var dynamoBuilderInstance = new DynamoDBEntityBuilder();

                    // Create the decorator that uses DynamoDB
                    var decoratorType = typeof(DynamoDbContextAdapter<>).MakeGenericType(entityType);
                    var decoratorInstance = Activator.CreateInstance(decoratorType, dynamoBuilderInstance);

                    // Invoke the "ApplyConfigurations" method on the configuration class, passing the decorator
                    var configureMethod = decoratorType.GetMethod("ApplyConfigurations");
                    if (decoratorInstance is not null)
                    {
                        configureMethod?.Invoke(decoratorInstance, new object[] { configInstance! });
                    }

                    // Store the DynamoDB builder in the cache (using the GetDynamoDBBuilder method from the decorator)
                    var getDynamoDBBuilderMethod = decoratorType.GetMethod("GetDynamoDBBuilder");
                    var dynamoBuilder = getDynamoDBBuilderMethod?.Invoke(decoratorInstance, null);
                    if (dynamoBuilder is not null)
                    {
                        _entityConfigurations[entityType] = (DynamoDBEntityBuilder)dynamoBuilder!;
                    }
                }
            }
        }

        // This method retrieves the configuration for a specific entity type
        public static IDynamoDBEntityBuilder? GetConfiguration<TEntity>()
        {
            var entityType = typeof(TEntity);

            return GetConfiguration(entityType);
        }

        public static IDynamoDBEntityBuilder? GetConfiguration(Type entityType)
        {
            if (_entityConfigurations.TryGetValue(entityType, out var config))
            {
                return config;
            }

            return default;
        }

        private static bool IsValidConfigurationType(Type t)
        {
            if (!t.IsClass || t.IsAbstract)
            {
                return false;
            }

            var baseType = t.BaseType;
            if (baseType == null || !baseType.IsGenericType)
            {
                return false;
            }

            return baseType.GetGenericTypeDefinition() == typeof(DocEntityConfiguration<>);
        }
    }
}
