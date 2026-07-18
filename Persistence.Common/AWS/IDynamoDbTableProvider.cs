using Amazon.DynamoDBv2.Model;

namespace Persistence.Common.AWS
{
    public interface IDynamoDbTableProvider
    {
        Task<bool> CreateTableAsync(long readCapacityUnits = 5, long writeCapacityUnits = 5, CancellationToken cancellationToken = default);

        Task<bool> UpdateTableAsync(long readCapacityUnits, long writeCapacityUnits, CancellationToken cancellationToken = default);

        Task<bool> DeleteTableAsync(CancellationToken cancellationToken = default);

        Task<bool> TableExistsAsync(CancellationToken cancellationToken = default);

        Task<List<TableDescription>> ListTablesAsync(CancellationToken cancellationToken = default);

        Task<bool> EnableTtlAsync(CancellationToken cancellationToken = default);
    }
}