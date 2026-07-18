using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Application.Common.Locking;

namespace Persistence.Common.AWS.Locking
{
    public record DynamoDbLockOptions
    {
        public string TableName { get; init; } = "distributed-locks";

        /// <summary>When true (default) the lock table is created on first use if missing.</summary>
        public bool AutoCreateTable { get; init; } = true;
    }

    /// <summary>
    /// <see cref="IDistributedLock"/> built on DynamoDB conditional writes: a lock is a
    /// single item keyed by name with an owner id and a lease expiry. Acquisition uses
    /// a conditional PutItem (item absent OR lease expired); release uses a conditional
    /// DeleteItem (still owned by us). Crashed holders are healed by lease expiry.
    /// </summary>
    public class DynamoDbDistributedLock : IDistributedLock
    {
        private const string _lockNameAttribute = "LockName";
        private const string _ownerAttribute = "OwnerId";
        private const string _expiresAttribute = "ExpiresAtEpochMs";

        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly DynamoDbLockOptions _options;
        private readonly SemaphoreSlim _tableCheckLock = new(1, 1);
        private bool _tableChecked;

        public DynamoDbDistributedLock(IAmazonDynamoDB dynamoDbClient, DynamoDbLockOptions options)
        {
            _dynamoDbClient = dynamoDbClient;
            _options = options;
        }

        public async Task<IDistributedLockHandle?> TryAcquireAsync(string lockName, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            await EnsureTableExistsAsync(cancellationToken);

            var ownerId = Guid.NewGuid().ToString("N");
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var expiresAt = DateTimeOffset.UtcNow.Add(leaseDuration);

            try
            {
                await _dynamoDbClient.PutItemAsync(new PutItemRequest
                {
                    TableName = _options.TableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        [_lockNameAttribute] = new AttributeValue { S = lockName },
                        [_ownerAttribute] = new AttributeValue { S = ownerId },
                        [_expiresAttribute] = new AttributeValue { N = expiresAt.ToUnixTimeMilliseconds().ToString() }
                    },
                    // Succeed only when no lock exists or the previous lease has expired.
                    ConditionExpression = $"attribute_not_exists({_lockNameAttribute}) OR {_expiresAttribute} < :now",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":now"] = new AttributeValue { N = nowMs.ToString() }
                    }
                }, cancellationToken);

                return new LockHandle(this, lockName, ownerId, expiresAt);
            }
            catch (ConditionalCheckFailedException)
            {
                return null; // held by someone else with an unexpired lease
            }
        }

        private async Task ReleaseAsync(string lockName, string ownerId)
        {
            try
            {
                await _dynamoDbClient.DeleteItemAsync(new DeleteItemRequest
                {
                    TableName = _options.TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        [_lockNameAttribute] = new AttributeValue { S = lockName }
                    },
                    // Only delete if we still own it - never release someone else's lock.
                    ConditionExpression = $"{_ownerAttribute} = :owner",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":owner"] = new AttributeValue { S = ownerId }
                    }
                });
            }
            catch (ConditionalCheckFailedException)
            {
                // Our lease expired and another owner took over - nothing to release.
            }
        }

        private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
        {
            if (_tableChecked || !_options.AutoCreateTable)
            {
                return;
            }

            await _tableCheckLock.WaitAsync(cancellationToken);
            try
            {
                if (_tableChecked)
                {
                    return;
                }

                try
                {
                    await _dynamoDbClient.DescribeTableAsync(_options.TableName, cancellationToken);
                }
                catch (ResourceNotFoundException)
                {
                    await _dynamoDbClient.CreateTableAsync(new CreateTableRequest
                    {
                        TableName = _options.TableName,
                        KeySchema = new List<KeySchemaElement> { new(_lockNameAttribute, KeyType.HASH) },
                        AttributeDefinitions = new List<AttributeDefinition> { new(_lockNameAttribute, ScalarAttributeType.S) },
                        BillingMode = BillingMode.PAY_PER_REQUEST
                    }, cancellationToken);
                }

                _tableChecked = true;
            }
            finally
            {
                _tableCheckLock.Release();
            }
        }

        private sealed class LockHandle : IDistributedLockHandle
        {
            private readonly DynamoDbDistributedLock _owner;
            private bool _released;

            public LockHandle(DynamoDbDistributedLock owner, string lockName, string ownerId, DateTimeOffset leaseExpiresAtUtc)
            {
                _owner = owner;
                LockName = lockName;
                OwnerId = ownerId;
                LeaseExpiresAtUtc = leaseExpiresAtUtc;
            }

            public string LockName { get; }
            public string OwnerId { get; }
            public DateTimeOffset LeaseExpiresAtUtc { get; }

            public async ValueTask DisposeAsync()
            {
                if (!_released)
                {
                    _released = true;
                    await _owner.ReleaseAsync(LockName, OwnerId);
                }
            }
        }
    }
}
