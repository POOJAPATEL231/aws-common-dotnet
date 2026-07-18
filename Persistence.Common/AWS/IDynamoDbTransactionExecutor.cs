using Amazon.DynamoDBv2.Model;

namespace Persistence.Common.AWS
{
    public interface IDynamoDbTransactionExecutor
    {
        Task ExecuteTransactionAsync(List<TransactWriteItem> transactionItems, CancellationToken cancellationToken = default);
    }
}