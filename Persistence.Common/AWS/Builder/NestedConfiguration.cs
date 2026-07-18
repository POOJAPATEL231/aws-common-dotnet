namespace Persistence.Common.AWS.Builder
{
    public class NestedConfiguration
    {
        public string PropertyName { get; set; }
        public string? BreakingPropertyName { get; set; }
        public List<PropertyConfiguration> Configurations { get; init; }
        public bool IsCollection { get; set; }
        public Type PropertyType { get; set; }

        public List<NestedConfiguration> NestedConfigurations { get; internal set; } = new();

        public NestedConfiguration(string propertyName, Type propertyType,
                                   string? breakingPropertyName, List<PropertyConfiguration> configurations,
                                   bool isCollection, List<NestedConfiguration> nestedConfigurations)
        {
            PropertyName = propertyName;
            PropertyType = propertyType;
            Configurations = configurations;
            IsCollection = isCollection;
            if (nestedConfigurations is { Count: > 0 })
            {
                NestedConfigurations = nestedConfigurations;
            }
            if (!string.IsNullOrWhiteSpace(breakingPropertyName))
            {
                BreakingPropertyName = breakingPropertyName;
            }
        }
    }
}
