using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Persistence.Common.AWS
{
    public class DynamoDbTransactionExecutor : IDynamoDbTransactionExecutor
    {
        // DynamoDB allows up to 100 items in a single TransactWriteItems call.
        private const int _maxTransactionItems = 100;
        private readonly IAmazonDynamoDB _dynamoDbClient;

        public DynamoDbTransactionExecutor(IAmazonDynamoDB dynamoDbClient)
        {
            _dynamoDbClient = dynamoDbClient;
        }

        // Method to execute the combined transaction
        public async Task ExecuteTransactionAsync(List<TransactWriteItem> transactionItems, CancellationToken cancellationToken = default)
        {
            if (transactionItems is not { Count: > 0 })
            {
                throw new ArgumentException("Transaction items must not be null or empty.");
            }

            // Splitting into multiple TransactWriteItems calls would silently break atomicity:
            // an earlier batch could commit while a later one fails. Fail fast instead so the
            // caller can either reduce the change set or opt into non-transactional batch writes.
            if (transactionItems.Count > _maxTransactionItems)
            {
                throw new InvalidOperationException(
                    $"Cannot save {transactionItems.Count} changes in a single atomic transaction; " +
                    $"DynamoDB supports at most {_maxTransactionItems} items per transaction. " +
                    "Save in smaller batches instead.");
            }

            try
            {
                var transactWriteItemsRequest = new TransactWriteItemsRequest
                {
                    TransactItems = transactionItems
                };

                await _dynamoDbClient.TransactWriteItemsAsync(transactWriteItemsRequest, cancellationToken);
            }
            catch (TransactionCanceledException ex)
            {
                var reasons = ex.CancellationReasons is { Count: > 0 }
                    ? string.Join("; ", ex.CancellationReasons
                        .Where(r => r.Code != "None")
                        .Select(r => $"{r.Code}: {r.Message}"))
                    : ex.Message;

                // A failed condition means another writer changed the item since it was
                // read (or an Add hit an existing key) - surface it as a concurrency
                // conflict so callers can re-read and retry.
                if (ex.CancellationReasons?.Any(r => r.Code == "ConditionalCheckFailed") == true)
                {
                    throw new DynamoDbConcurrencyException(
                        $"The data was modified by another process since it was read. Reasons: {reasons}", ex);
                }

                throw new InvalidOperationException($"Transaction failed. Reasons: {reasons}", ex);
            }
        }
    }
}
