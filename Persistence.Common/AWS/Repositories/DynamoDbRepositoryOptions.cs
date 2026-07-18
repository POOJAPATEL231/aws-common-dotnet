namespace Persistence.Common.AWS.Repositories
{
    public class DynamoDbRepositoryOptions
    {
        public long ReadCapacityUnits { get; init; } = 100;

        public long WriteCapacityUnits { get; init; } = 100;

        public IList<DynamoDbDocEntitySetting>? DocEntitySettings { get; init; }
    }

    public record DynamoDbDocEntitySetting(string EntityName, int DefaultTtlDays, long ReadCapacityUnits = 100, long WriteCapacityUnits = 100);
}
