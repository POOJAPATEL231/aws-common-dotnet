namespace Persistence.Common.AWS.Builder
{
    public class PropertyConfiguration
    {
        public PropertyConfiguration(string propertyName)
        {
            PropertyName = propertyName;
        }

        public string PropertyName { get; }

        public string? BreakingPropertyName { get; set; }

        public Type? PropertyType { get; set; }

        public string? JsonPropertyName { get; set; }

        public bool IsPartitionKey { get; set; }

        public bool IsPrimaryKey { get; set; }

        public bool IsIgnoreEnable { get; set; }

        public bool HasEncryption { get; set; }

        public object? ValueConverter { get; set; }
    }
}
