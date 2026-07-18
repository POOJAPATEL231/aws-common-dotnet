namespace Persistence.Common.AWS.Builder
{
    public class DynamoDBEntityBuilder : IDynamoDBEntityBuilder
    {
        private readonly List<PropertyConfiguration> _propertyConfigurations = new();
        private readonly List<NestedConfiguration> _nestedEntityConfigurations = new();
        private string _tableName = string.Empty;

        // Mark property as partition key
        public IDynamoDBEntityBuilder HasPartitionKey(string propertyName, Type propertyType)
        {
            var propertyConfiguration = GetPropertyConfiguration(propertyName);
            propertyConfiguration.IsPartitionKey = true;
            propertyConfiguration.PropertyType = propertyType;
            return this;
        }

        // Mark property as range key
        public IDynamoDBEntityBuilder HasRangeKey(string propertyName, Type propertyType)
        {
            var propertyConfiguration = GetPropertyConfiguration(propertyName);
            propertyConfiguration.IsPrimaryKey = true;
            propertyConfiguration.PropertyType = propertyType;
            return this;
        }

        // Mark property as HasEncryption
        public IDynamoDBEntityBuilder HasEncryption(string propertyName, Type propertyType)
        {
            var propertyConfiguration = GetPropertyConfiguration(propertyName);
            propertyConfiguration.PropertyType = propertyType;
            propertyConfiguration.HasEncryption = true;
            return this;
        }

        // set property converter
        public IDynamoDBEntityBuilder HasJsonConversion(string propertyName, Type propertyType, object converter)
        {
            var propertyConfiguration = GetPropertyConfiguration(propertyName);
            propertyConfiguration.PropertyType = propertyType;
            propertyConfiguration.ValueConverter = converter;
            return this;
        }

        // Mark property as DynamoDBProperty with a specific attribute name (simulates ToJsonProperty behavior)
        public IDynamoDBEntityBuilder ToJsonProperty(string propertyName, Type propertyType, string jsonPropertyName)
        {
            var propertyConfiguration = GetPropertyConfiguration(propertyName);
            propertyConfiguration.PropertyType = propertyType;
            propertyConfiguration.JsonPropertyName = jsonPropertyName;
            return this;
        }

        // Mark property to be ignored by DynamoDB
        public IDynamoDBEntityBuilder Ignore(string propertyName)
        {
            var propertyConfiguration = GetPropertyConfiguration(propertyName);
            if (!(propertyConfiguration.IsPartitionKey || propertyConfiguration.IsPrimaryKey))
            {
                propertyConfiguration.IsIgnoreEnable = true;
            }
            return this;
        }

        public IDynamoDBEntityBuilder AddBreakingProperty(string propertyName, string breakingPropertyName, Type propertyType)
        {
            var propertyConfiguration = GetPropertyConfiguration(propertyName);
            propertyConfiguration.BreakingPropertyName = breakingPropertyName;
            propertyConfiguration.PropertyType = propertyType;
            return this;
        }

        public IDynamoDBEntityBuilder ToContainer(string tableName)
        {
            _tableName = tableName;
            return this;
        }

        public PropertyConfiguration GetPropertyConfiguration(string propertyName)
        {
            var propertyConfiguration = _propertyConfigurations.Find(e => e.PropertyName == propertyName);
            // Ensure we initialize the list for the property if not already present
            if (propertyConfiguration is not null)
            {
                return propertyConfiguration;
            }
            else
            {
                var newPropertyConfiguration = new PropertyConfiguration(propertyName);
                _propertyConfigurations.Add(newPropertyConfiguration);
                return newPropertyConfiguration;
            }
        }

        public void AddNestedEntityConfigurations(string nestedPropertyName, Type propertyType, string? breakingPropertyName, List<PropertyConfiguration> nestedEntityConfigurations,
                        bool isCollection, List<NestedConfiguration> nestedChildEntityConfigurations)
        {
            _nestedEntityConfigurations.Add(new NestedConfiguration(nestedPropertyName, propertyType, breakingPropertyName, nestedEntityConfigurations, isCollection, nestedChildEntityConfigurations));
        }

        // Expose the property configurations
        public List<PropertyConfiguration> GetPropertyConfigurations() => _propertyConfigurations;

        // Expose the nested entity builders
        public List<NestedConfiguration> GetNestedEntityConfigurations() => _nestedEntityConfigurations;

        // Expose the table name
        public string GetTableName()
        {
            return _tableName;
        }

        public void SetTableName(string tableName)
        {
            throw new NotImplementedException();
        }

        public void ConfigureProperty(string propertyName, PropertyConfiguration configuration)
        {
            throw new NotImplementedException();
        }

        public void ConfigureNestedEntity(NestedConfiguration nestedConfiguration)
        {
            throw new NotImplementedException();
        }
    }
}
