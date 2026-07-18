using Amazon.SQS;
using Amazon.SQS.Model;
using Infrastructure.Common.AWS.Eventbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Common.Tests
{
    public class SqsConsumerServiceTests
    {
        private static (SqsConsumerService Service, Mock<IAmazonSQS> Sqs, Mock<ISqsMessageDispatcher> Dispatcher)
            CreateHarness(params Message[] messages)
        {
            var sqs = new Mock<IAmazonSQS>();
            sqs.Setup(c => c.GetQueueUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = "http://sqs/queue" });
            sqs.Setup(c => c.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ReceiveMessageResponse { Messages = messages.ToList() });
            sqs.Setup(c => c.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DeleteMessageResponse());

            var dispatcher = new Mock<ISqsMessageDispatcher>();
            var services = new ServiceCollection();
            services.AddScoped(_ => dispatcher.Object);
            var provider = services.BuildServiceProvider();

            var service = new SqsConsumerService(sqs.Object, provider.GetRequiredService<IServiceScopeFactory>(),
                new SqsConsumerOptions(), NullLogger<SqsConsumerService>.Instance);

            return (service, sqs, dispatcher);
        }

        [Fact]
        public void QueueNames_DeriveFromEventNames_ByBusConvention()
        {
            var options = new SqsConsumerOptions
            {
                EventNames = { "OrderCreatedIntegrationEvent" },
                QueueNames = { "custom-queue.fifo" }
            };

            var queues = options.ResolveQueueNames();

            Assert.Contains("ordercreated_Queue.fifo", queues); // convention: strip suffix, lowercase
            Assert.Contains("custom-queue.fifo", queues);
        }

        [Fact]
        public async Task SuccessfulDispatch_DeletesTheMessage()
        {
            var (service, sqs, dispatcher) = CreateHarness(
                new Message { MessageId = "m1", Body = "{}", ReceiptHandle = "r1" });
            dispatcher.Setup(d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var received = await service.PollQueueOnceAsync("ordercreated_Queue.fifo", CancellationToken.None);

            Assert.Equal(1, received);
            dispatcher.Verify(d => d.DispatchAsync("{}", It.IsAny<CancellationToken>()), Times.Once);
            sqs.Verify(c => c.DeleteMessageAsync("http://sqs/queue", "r1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandlerFailure_LeavesMessageForRedelivery()
        {
            var (service, sqs, dispatcher) = CreateHarness(
                new Message { MessageId = "ok", Body = "good", ReceiptHandle = "r-ok" },
                new Message { MessageId = "bad", Body = "boom", ReceiptHandle = "r-bad" });
            dispatcher.Setup(d => d.DispatchAsync("good", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            dispatcher.Setup(d => d.DispatchAsync("boom", It.IsAny<CancellationToken>()))
                      .ThrowsAsync(new InvalidOperationException("handler exploded"));

            var received = await service.PollQueueOnceAsync("ordercreated_Queue.fifo", CancellationToken.None);

            Assert.Equal(2, received);
            // Only the successful message is deleted; the failed one stays for SQS
            // redelivery and eventually the DLQ.
            sqs.Verify(c => c.DeleteMessageAsync(It.IsAny<string>(), "r-ok", It.IsAny<CancellationToken>()), Times.Once);
            sqs.Verify(c => c.DeleteMessageAsync(It.IsAny<string>(), "r-bad", It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UnroutableMessage_IsStillDeleted_ToAvoidPoisonLoops()
        {
            var (service, sqs, dispatcher) = CreateHarness(
                new Message { MessageId = "m1", Body = "unroutable", ReceiptHandle = "r1" });
            dispatcher.Setup(d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await service.PollQueueOnceAsync("ordercreated_Queue.fifo", CancellationToken.None);

            sqs.Verify(c => c.DeleteMessageAsync(It.IsAny<string>(), "r1", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
