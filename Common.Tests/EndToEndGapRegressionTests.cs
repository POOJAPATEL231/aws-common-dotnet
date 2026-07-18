using Amazon.SQS;
using Amazon.SQS.Model;
using Domain.Common.Settings;
using Infrastructure.Common.AWS;
using Infrastructure.Common.AWS.Eventbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Persistence.Common.AWS;
using Persistence.Common.AWS.Repositories;
using Xunit;

namespace Common.Tests
{
    public class TableCreationCoverageTests
    {
        [Fact]
        public async Task TablesAreCreated_ForEveryRegisteredProvider_NotJustContextProperties()
        {
            // Regression: table creation previously reflected over context properties,
            // silently skipping infrastructure entities like OutboxMessage - the first
            // transactional save staging an outbox message then failed at runtime.
            var missingTableProvider = new Mock<IDynamoDbTableProvider>();
            missingTableProvider.Setup(p => p.TableExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            missingTableProvider.Setup(p => p.CreateTableAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            missingTableProvider.Setup(p => p.EnableTtlAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var existingTableProvider = new Mock<IDynamoDbTableProvider>();
            existingTableProvider.Setup(p => p.TableExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            existingTableProvider.Setup(p => p.EnableTtlAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var services = new ServiceCollection();
            services.AddSingleton(missingTableProvider.Object);
            services.AddSingleton(existingTableProvider.Object);
            var provider = services.BuildServiceProvider();

            await Persistence.Common.AWS.DependencyInjection.DependencyInjection
                .EnsureDynamoDbTablesCreatedAsync(provider, new DynamoDbRepositoryOptions());

            missingTableProvider.Verify(p => p.CreateTableAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
            existingTableProvider.Verify(p => p.CreateTableAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
            existingTableProvider.Verify(p => p.EnableTtlAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public record DirectDemoMessage(string Text);

    public class SimpleQueueServiceFifoTests
    {
        private static (SimpleQueueService Service, Mock<IAmazonSQS> Sqs) CreateHarness()
        {
            var sqs = new Mock<IAmazonSQS>();
            sqs.Setup(c => c.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = "http://sqs/queue.fifo", HttpStatusCode = System.Net.HttpStatusCode.OK });
            sqs.Setup(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new SendMessageResponse { MessageId = "m-1", HttpStatusCode = System.Net.HttpStatusCode.OK });

            var cache = new Mock<Application.Common.ICache>();
            cache.Setup(c => c.GetAsync<string>(It.IsAny<string>())).ReturnsAsync((string?)null);
            cache.Setup(c => c.GetAsync<List<string>>(It.IsAny<string>())).ReturnsAsync((List<string>?)null);

            var service = new SimpleQueueService(
                Options.Create(new AwsInfraSettings { EventDispatcherFunctionName = string.Empty }),
                Options.Create(new AwsTagsConfigSettings()),
                NullLogger<SimpleQueueService>.Instance,
                sqs.Object,
                Mock.Of<Amazon.Lambda.IAmazonLambda>(),
                cache.Object);

            return (service, sqs);
        }

        [Fact]
        public async Task UnconfiguredMessageType_FallsBackToConventionQueue_InsteadOfThrowing()
        {
            // Regression: previously threw KeyNotFoundException because the Queues config
            // was keyed by queue name (event-bus convention) but looked up by type name.
            var (service, sqs) = CreateHarness();

            var messageId = await service.SendMessageAsync(new DirectDemoMessage("hello"));

            Assert.Equal("m-1", messageId);
            sqs.Verify(c => c.GetQueueUrlAsync(
                It.Is<GetQueueUrlRequest>(r => r.QueueName == "directdemomessage_queue.fifo"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FifoQueueSend_IncludesGroupAndDeduplicationIds()
        {
            // Regression: sends to the FIFO queues this service creates were missing
            // MessageGroupId (AWS rejects such requests) and set the per-message
            // DelaySeconds FIFO queues do not allow.
            var (service, sqs) = CreateHarness();
            SendMessageRequest? captured = null;
            sqs.Setup(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
               .Callback<SendMessageRequest, CancellationToken>((r, _) => captured = r)
               .ReturnsAsync(new SendMessageResponse { MessageId = "m-1", HttpStatusCode = System.Net.HttpStatusCode.OK });

            await service.ScheduleMessageAsync(new DirectDemoMessage("hello"), TimeSpan.FromSeconds(30));

            Assert.NotNull(captured);
            Assert.Equal(nameof(DirectDemoMessage), captured!.MessageGroupId);
            Assert.False(string.IsNullOrEmpty(captured.MessageDeduplicationId));
            Assert.Equal(0, captured.DelaySeconds); // per-message delay suppressed on FIFO
        }
    }
}
