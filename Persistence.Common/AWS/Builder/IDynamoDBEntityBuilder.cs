namespace Persistence.Common.AWS.Builder
{
    public interface IDynamoDBEntityBuilder
    {
        // Mark property as partition key
        IDynamoDBEntityBuilder HasPartitionKey(string propertyName, Type propertyType);

        // Mark property as range key
        IDynamoDBEntityBuilder HasRangeKey(string propertyName, Type propertyType);

        // Mark property as having encryption
        IDynamoDBEntityBuilder HasEncryption(string propertyName, Type propertyType);

        //set property converter
        IDynamoDBEntityBuilder HasJsonConversion(string propertyName, Type propertyType, object converter);

        // Mark property as DynamoDBProperty with a specific attribute name
        IDynamoDBEntityBuilder ToJsonProperty(string propertyName, Type propertyType, string jsonPropertyName);

        // Mark property to be ignored by DynamoDB
        IDynamoDBEntityBuilder Ignore(string propertyName);

        // Specify the table name for the entity
        IDynamoDBEntityBuilder ToContainer(string tableName);

        // Add Breaking property
        IDynamoDBEntityBuilder AddBreakingProperty(string propertyName, string breakingPropertyName, Type propertyType);

        // Get the list of property configurations
        List<PropertyConfiguration> GetPropertyConfigurations();

        // Get the list of nested entity configurations
        List<NestedConfiguration> GetNestedEntityConfigurations();

        // Get the table name
        string GetTableName();

        // Add nested entity configurations
        void AddNestedEntityConfigurations(string nestedPropertyName, Type propertyType, string? breakingPropertyName, List<PropertyConfiguration> nestedEntityConfigurations,
                 bool isCollection, List<NestedConfiguration> nestedChildEntityConfigurations);
    }
}
