using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Persistence.Common.AWS.Builder
{
    public class DynamoDbContextAdapter<TEntity> where TEntity : DocEntity
    {
        private readonly IDynamoDBEntityBuilder _dynamoDbBuilder;

        public DynamoDbContextAdapter(IDynamoDBEntityBuilder dynamoDbBuilder)
        {
            _dynamoDbBuilder = dynamoDbBuilder;
        }

        public IDynamoDBEntityBuilder GetDynamoDBBuilder()
        {
            return _dynamoDbBuilder;
        }

        // Apply configurations from EntityTypeBuilder to DynamoDBEntityBuilder
        public void ApplyConfigurations(IEntityTypeConfiguration<TEntity> entityConfig)
        {
            var modelBuilder = new ModelBuilder(); // Create a mock ModelBuilder

            // Apply the existing configuration using EntityTypeBuilder
            var entityTypeBuilder = modelBuilder.Entity<TEntity>();
            entityConfig.Configure(entityTypeBuilder);

            // Adapt entity properties from EntityTypeBuilder to DynamoDBEntityBuilder
            AdaptEntityProperties(entityTypeBuilder);

            // Adapt ownerships (owned properties) dynamically
            AdaptOwnerships(entityTypeBuilder);
        }

        // Adapt entity properties
        private void AdaptEntityProperties(EntityTypeBuilder entityTypeBuilder)
        {
            var partitionKeyProperty = entityTypeBuilder.Metadata.GetPartitionKeyProperty();
            if (!string.IsNullOrWhiteSpace(partitionKeyProperty?.Name))
            {
                _dynamoDbBuilder.HasPartitionKey(partitionKeyProperty.Name, partitionKeyProperty.ClrType);
            }

            var containerColumnName = entityTypeBuilder.Metadata.GetContainer();
            if (!string.IsNullOrWhiteSpace(containerColumnName))
            {
                _dynamoDbBuilder.ToContainer(containerColumnName);
            }

            var primaryKey = entityTypeBuilder.Metadata.FindPrimaryKey();
            if (primaryKey != null && primaryKey.Properties.Count > 0)
            {
                var primaryKeyProperty = primaryKey.Properties[0];
                if (primaryKeyProperty != null)
                {
                    _dynamoDbBuilder.HasRangeKey(primaryKeyProperty.Name, primaryKeyProperty.ClrType);
                }
            }
            AddIgnorePropertyConfiguration(_dynamoDbBuilder, entityTypeBuilder.Metadata.GetIgnoredMembers());

            AddPropertiesConfiguration(_dynamoDbBuilder, entityTypeBuilder.Metadata.GetProperties());

            AddIndexConfigurations(_dynamoDbBuilder, entityTypeBuilder.Metadata.GetIndexes());
        }

        // Map EF HasIndex(...) declarations to DynamoDB Global Secondary Indexes.
        // The first index property becomes the GSI partition key; an optional second
        // property becomes the GSI sort key.
        private static void AddIndexConfigurations(IDynamoDBEntityBuilder dynamoDBEntityBuilder, IEnumerable<IMutableIndex> indexes)
        {
            foreach (var index in indexes)
            {
                if (index.Properties.Count == 0)
                {
                    continue;
                }

                var partitionKeyProperty = index.Properties[0].Name;
                var sortKeyProperty = index.Properties.Count > 1 ? index.Properties[1].Name : null;
                var indexName = index.Name ?? $"{string.Join("-", index.Properties.Select(p => p.Name))}-index";

                dynamoDBEntityBuilder.HasGlobalSecondaryIndex(indexName, partitionKeyProperty, sortKeyProperty);
            }
        }

        // Adapt ownerships (OwnsOne and OwnsMany)
        private void AdaptOwnerships(EntityTypeBuilder entityTypeBuilder)
        {
            // Start adapting ownerships from the root level
            foreach (var ownedNavigation in entityTypeBuilder.Metadata.GetNavigations())
            {
                AdaptOwnedNavigation(ownedNavigation, _dynamoDbBuilder);
            }
        }

        // Recursive method to handle nested owned properties
        private static void AdaptOwnedNavigation(IMutableNavigation ownedNavigation, IDynamoDBEntityBuilder parentBuilder)
        {
            var childBuilder = new DynamoDBEntityBuilder();

            // Get the PropertyAccessMode
            var accessMode = ownedNavigation.GetPropertyAccessMode();

            // Get the backing field name (if PropertyAccessMode.Field is set)
            var fieldName = accessMode == PropertyAccessMode.Field ? ownedNavigation.GetFieldName() : null;

            // Configure properties for this level
            AddIgnorePropertyConfiguration(childBuilder, ownedNavigation.TargetEntityType.GetIgnoredMembers());
            AddPropertiesConfiguration(childBuilder, ownedNavigation.TargetEntityType.GetProperties());

            // Recursively adapt any owned navigations within this owned type
            foreach (var nestedOwnedNavigation in ownedNavigation.TargetEntityType.GetNavigations())
            {
                if (nestedOwnedNavigation.TargetEntityType.IsOwned())
                {
                    AdaptOwnedNavigation(nestedOwnedNavigation, childBuilder);
                }
            }

            // Add this level's configurations to the parent builder
            parentBuilder.AddNestedEntityConfigurations(
                ownedNavigation.PropertyInfo!.Name,
                ownedNavigation.ClrType,
                fieldName,
                childBuilder.GetPropertyConfigurations(),
                isCollection: ownedNavigation.IsCollection,
                childBuilder.GetNestedEntityConfigurations());
        }

        private static void AddIgnorePropertyConfiguration(IDynamoDBEntityBuilder dynamoDBEntityBuilder, IEnumerable<string> ignoredMembers)
        {
            foreach (var propertyName in ignoredMembers)
            {
                dynamoDBEntityBuilder.Ignore(propertyName);
            }
        }

        private static void AddPropertiesConfiguration(IDynamoDBEntityBuilder dynamoDBEntityBuilder, IEnumerable<IMutableProperty> properties)
        {
            foreach (var property in properties)
            {
                AddPropertyConfiguration(dynamoDBEntityBuilder, property);
            }
        }

        private static void AddPropertyConfiguration(IDynamoDBEntityBuilder dynamoDBEntityBuilder, IReadOnlyPropertyBase property)
        {
            var propertyName = property.Name;

            // Get the PropertyAccessMode
            var accessMode = property.GetPropertyAccessMode();

            // Get the backing field name (if PropertyAccessMode.Field is set)
            var fieldName = accessMode == PropertyAccessMode.Field ? property.GetFieldName() : null;

            // Use fieldName if available; otherwise, fall back to propertyName
            dynamoDBEntityBuilder.AddBreakingProperty(propertyName, fieldName ?? propertyName, property.ClrType);

            foreach (var annotation in property.GetAnnotations())
            {
                ProcessAnnotation(dynamoDBEntityBuilder, propertyName, property.ClrType, annotation);
            }
        }

        private static void ProcessAnnotation(IDynamoDBEntityBuilder dynamoDBEntityBuilder, string propertyName, Type propertyType, IAnnotation annotation)
        {
            if (annotation.Name.Contains("PropertyName", StringComparison.OrdinalIgnoreCase))
            {
                var jsonPropertyName = annotation.Value?.ToString();
                if (string.IsNullOrWhiteSpace(jsonPropertyName))
                {
                    return;
                }
                dynamoDBEntityBuilder.ToJsonProperty(propertyName, propertyType, jsonPropertyName);
            }
            else if (annotation.Name.Contains("Encryption", StringComparison.OrdinalIgnoreCase))
            {
                dynamoDBEntityBuilder.HasEncryption(propertyName, propertyType);
            }
            else if (annotation.Name.Contains("ValueConverter", StringComparison.OrdinalIgnoreCase) &&
            annotation.Value is Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter converter)
            {
                dynamoDBEntityBuilder.HasJsonConversion(propertyName, propertyType, converter);
            }
        }
    }
}
