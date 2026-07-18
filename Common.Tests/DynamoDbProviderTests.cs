using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Moq;
using Persistence.Common.AWS;
using Utils.Common.Crypto;
using Xunit;

namespace Common.Tests
{
    public class DynamoDbTransactionExecutorTests
    {
        [Fact]
        public async Task MoreThan100Items_ThrowsInsteadOfSilentlySplitting()
        {
            // Regression: items used to be split into multiple TransactWriteItems calls,
            // breaking atomicity (an earlier batch could commit while a later one failed).
            var client = new Mock<IAmazonDynamoDB>();
            var executor = new DynamoDbTransactionExecutor(client.Object);
            var items = Enumerable.Range(0, 101).Select(_ => new TransactWriteItem()).ToList();

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteTransactionAsync(items));

            client.Verify(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpTo100Items_ExecuteAsSingleTransaction()
        {
            var client = new Mock<IAmazonDynamoDB>();
            client.Setup(c => c.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new TransactWriteItemsResponse());
            var executor = new DynamoDbTransactionExecutor(client.Object);
            var items = Enumerable.Range(0, 100).Select(_ => new TransactWriteItem()).ToList();

            await executor.ExecuteTransactionAsync(items);

            client.Verify(c => c.TransactWriteItemsAsync(
                It.Is<TransactWriteItemsRequest>(r => r.TransactItems.Count == 100),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EmptyItems_Throw()
        {
            var executor = new DynamoDbTransactionExecutor(Mock.Of<IAmazonDynamoDB>());
            await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteTransactionAsync(new List<TransactWriteItem>()));
        }
    }

    public class DynamoDbPaginationTests
    {
        [Fact]
        public async Task SecondPage_ReturnsSecondWindow_NotFirst()
        {
            // Regression: paged reads previously ignored the page argument entirely -
            // every page returned the first pageSize items.
            var client = new Mock<IAmazonDynamoDB>();

            var page1 = new ScanResponse
            {
                Items = MakeItems(1, 5),
                LastEvaluatedKey = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = "cursor" } }
            };
            var page2 = new ScanResponse
            {
                Items = MakeItems(6, 5),
                LastEvaluatedKey = new Dictionary<string, AttributeValue>()
            };

            client.SetupSequence(c => c.ScanAsync(It.IsAny<ScanRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(page1)
                  .ReturnsAsync(page2);

            var provider = new DynamoDbDocProvider<TestOrder>(client.Object, Mock.Of<ICryptoProvider>());

            var result = await provider.GetPagedItemsAsync(page: 2, pageSize: 5);

            Assert.Equal(10, result.TotalRecords);
            Assert.Equal(2, result.Page);
            var quantities = result.Items.Select(i => i.Quantity).OrderBy(q => q).ToList();
            Assert.Equal(new List<int> { 6, 7, 8, 9, 10 }, quantities);
        }

        private static List<Dictionary<string, AttributeValue>> MakeItems(int start, int count)
        {
            return Enumerable.Range(start, count)
                .Select(i => new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new AttributeValue { S = $"order:{i}" },
                    ["Quantity"] = new AttributeValue { N = i.ToString() }
                })
                .ToList();
        }
    }
}
