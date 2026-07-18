using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Domain.Common.Entities;
using Domain.Common.Settings;
using Microsoft.Extensions.Options;
using Moq;
using Persistence.Common.AWS;
using Persistence.Common.AWS.Builder;
using Utils.Common.Crypto;
using Xunit;

namespace Common.Tests
{
    /// <summary>Entity with a registered configuration including a GSI on CustomerName.</summary>
    public class GsiOrder : DocEntity
    {
        public override string PartitionKey { get; set; } = "gsiorder";
        public string CustomerName { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public class GlobalSecondaryIndexTests
    {
        public GlobalSecondaryIndexTests()
        {
            var builder = new DynamoDBEntityBuilder();
            builder.HasPartitionKey(nameof(GsiOrder.PartitionKey), typeof(string));
            builder.HasRangeKey(nameof(GsiOrder.Id), typeof(string));
            builder.HasGlobalSecondaryIndex("CustomerName-index", nameof(GsiOrder.CustomerName));
            InMemoryDynamoDBEntitiesConfiguration.AddConfiguration(typeof(GsiOrder), builder);
        }

        [Fact]
        public void EqualityOnGsiPartitionKey_PromotesToIndexQuery()
        {
            var queryBuilder = new DynamoDbQueryBuilder<GsiOrder>().WithExpression(x => x.CustomerName == "alice");
            var (keyFilter, filter, values, _) = queryBuilder.Build();

            Assert.NotNull(keyFilter);
            Assert.True(keyFilter!.CanUseKeyFilter());
            Assert.Equal("CustomerName-index", keyFilter.IndexName);
            Assert.Equal("CustomerName = :param_0", keyFilter.PartitionKeyFilter);
            Assert.Equal("alice", values[":param_0"].S);
            // The condition moved into the key filter - nothing should remain in the filter.
            Assert.True(string.IsNullOrWhiteSpace(filter));
        }

        [Fact]
        public void GsiQuery_KeepsRemainingConditionsAsFilter()
        {
            var queryBuilder = new DynamoDbQueryBuilder<GsiOrder>()
                .WithExpression(x => x.CustomerName == "alice" && x.Quantity > 5);
            var (keyFilter, filter, _, _) = queryBuilder.Build();

            Assert.NotNull(keyFilter);
            Assert.Equal("CustomerName-index", keyFilter!.IndexName);
            Assert.Contains("Quantity > :param_1", filter);
            Assert.DoesNotContain("CustomerName", filter);
        }

        [Fact]
        public void BaseTableKeyStillWins_NoIndexUsed()
        {
            var queryBuilder = new DynamoDbQueryBuilder<GsiOrder>().WithExpression(x => x.PartitionKey == "gsiorder");
            var (keyFilter, _, _, _) = queryBuilder.Build();

            Assert.NotNull(keyFilter);
            Assert.True(keyFilter!.CanUseKeyFilter());
            Assert.Null(keyFilter.IndexName);
        }

        [Fact]
        public void NonKeyPredicateWithoutGsi_StillFallsBackToScan()
        {
            // Quantity has no index - the key filter must stay unusable (scan path).
            var queryBuilder = new DynamoDbQueryBuilder<GsiOrder>().WithExpression(x => x.Quantity > 5);
            var (keyFilter, filter, _, _) = queryBuilder.Build();

            Assert.True(keyFilter is null || !keyFilter.CanUseKeyFilter());
            Assert.Contains("Quantity > :param_0", filter);
        }

        [Fact]
        public async Task CreateTable_IncludesConfiguredGsi()
        {
            CreateTableRequest? captured = null;
            var client = new Mock<IAmazonDynamoDB>();
            client.Setup(c => c.CreateTableAsync(It.IsAny<CreateTableRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<CreateTableRequest, CancellationToken>((r, _) => captured = r)
                  .ReturnsAsync(new CreateTableResponse { HttpStatusCode = System.Net.HttpStatusCode.OK });

            var provider = new DynamoDbTableProvider<GsiOrder>(client.Object, Options.Create(new AwsTagsConfigSettings()));
            await provider.CreateTableAsync();

            Assert.NotNull(captured);
            var gsi = Assert.Single(captured!.GlobalSecondaryIndexes);
            Assert.Equal("CustomerName-index", gsi.IndexName);
            Assert.Equal("CustomerName", gsi.KeySchema[0].AttributeName);
            Assert.Equal(KeyType.HASH, gsi.KeySchema[0].KeyType);
            Assert.Contains(captured.AttributeDefinitions, a => a.AttributeName == "CustomerName" && a.AttributeType == ScalarAttributeType.S);
        }
    }

    public class OptimisticConcurrencyTests
    {
        private static DynamoDbDocProvider<TestOrder> CreateProvider() =>
            new(Mock.Of<IAmazonDynamoDB>(), Mock.Of<ICryptoProvider>());

        [Fact]
        public void Update_ConditionsOnPreviousETag_AndRotatesIt()
        {
            var provider = CreateProvider();
            var order = new TestOrder { CustomerName = "alice" };
            order.SetId("order:1");
            order.SetETag("etag-v1");

            var item = provider.GetUpdateTransactWriteItem(order);

            Assert.Equal("#etag_attr = :etag_expected", item.Update.ConditionExpression);
            Assert.Equal("etag-v1", item.Update.ExpressionAttributeValues[":etag_expected"].S);
            Assert.Equal(nameof(DocEntity.ETag), item.Update.ExpressionAttributeNames["#etag_attr"]);
            // The entity must now carry a fresh stamp for the next round.
            Assert.NotEqual("etag-v1", order.ETag);
            Assert.False(string.IsNullOrEmpty(order.ETag));
        }

        [Fact]
        public void Update_WithoutPreviousETag_RequiresAttributeAbsent()
        {
            var provider = CreateProvider();
            var order = new TestOrder { CustomerName = "alice" };
            order.SetId("order:1");

            var item = provider.GetUpdateTransactWriteItem(order);

            Assert.Equal("attribute_not_exists(#etag_attr)", item.Update.ConditionExpression);
        }

        [Fact]
        public void Add_RefusesToOverwriteExistingItem()
        {
            var provider = CreateProvider();
            var order = new TestOrder { CustomerName = "alice" };
            order.SetId("order:1");

            var item = provider.GetAddTransactWriteItem(order);

            Assert.Equal("attribute_not_exists(#pk)", item.Put.ConditionExpression);
            Assert.False(string.IsNullOrEmpty(order.ETag)); // fresh stamp for later updates
        }

        [Fact]
        public void Delete_ConditionsOnETagWhenPresent()
        {
            var provider = CreateProvider();
            var order = new TestOrder();
            order.SetId("order:1");
            order.SetETag("etag-v1");

            var item = provider.GetDeleteTransactWriteItem(order);

            Assert.Equal("#etag_attr = :etag_expected", item.Delete.ConditionExpression);
            Assert.Equal("etag-v1", item.Delete.ExpressionAttributeValues[":etag_expected"].S);
        }

        [Fact]
        public async Task ConditionalCheckFailure_SurfacesAsConcurrencyException()
        {
            var client = new Mock<IAmazonDynamoDB>();
            client.Setup(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new TransactionCanceledException("canceled")
                  {
                      CancellationReasons = new List<CancellationReason>
                      {
                          new() { Code = "ConditionalCheckFailed", Message = "The conditional request failed" }
                      }
                  });

            var executor = new DynamoDbTransactionExecutor(client.Object);
            var items = new List<TransactWriteItem> { new() };

            await Assert.ThrowsAsync<DynamoDbConcurrencyException>(() => executor.ExecuteTransactionAsync(items));
        }
    }
}
