namespace Domain.Common.Settings
{
    public record AwsTagsConfigSettings
    {
        public List<AwsTag> DefaultTags { get; init; } = new();
    }

    public record AwsTag
    {
        public string Key { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
