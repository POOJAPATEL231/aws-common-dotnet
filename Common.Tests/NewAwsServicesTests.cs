using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Application.Common.Email;
using Infrastructure.Common.AWS.Email;
using Infrastructure.Common.AWS.Eventbus;
using Infrastructure.Common.AWS.Metrics;
using Infrastructure.Common.AWS.Scheduling;
using Infrastructure.Common.AWS.Streaming;
using Infrastructure.Common.AWS.Workflow;
using Moq;
using System.Text.Json;
using Xunit;

namespace Common.Tests
{
    public class SesEmailServiceTests
    {
        [Fact]
        public async Task Send_MapsAllFields_AndReturnsMessageId()
        {
            SendEmailRequest? captured = null;
            var ses = new Mock<IAmazonSimpleEmailServiceV2>();
            ses.Setup(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
               .Callback<SendEmailRequest, CancellationToken>((r, _) => captured = r)
               .ReturnsAsync(new SendEmailResponse { MessageId = "msg-1" });

            var service = new SesEmailService(ses.Object);
            var messageId = await service.SendAsync(new EmailMessage
            {
                From = "noreply@example.com",
                To = { "alice@example.com" },
                Cc = { "bob@example.com" },
                Subject = "Hello",
                HtmlBody = "<b>Hi</b>",
                TextBody = "Hi",
                ReplyTo = "support@example.com"
            });

            Assert.Equal("msg-1", messageId);
            Assert.NotNull(captured);
            Assert.Equal("noreply@example.com", captured!.FromEmailAddress);
            Assert.Equal("alice@example.com", captured.Destination.ToAddresses.Single());
            Assert.Equal("Hello", captured.Content.Simple.Subject.Data);
            Assert.Equal("<b>Hi</b>", captured.Content.Simple.Body.Html.Data);
            Assert.Equal("support@example.com", captured.ReplyToAddresses.Single());
        }

        [Fact]
        public async Task Send_WithoutRecipients_Throws()
        {
            var service = new SesEmailService(Mock.Of<IAmazonSimpleEmailServiceV2>());
            await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(new EmailMessage { From = "a@b.c" }));
        }
    }

    public class EmfMetricsTests
    {
        [Fact]
        public void Count_EmitsValidEmfJson()
        {
            using var writer = new StringWriter();
            var metrics = new EmfMetrics(new EmfMetricsOptions
            {
                Namespace = "MyApp",
                DefaultDimensions = new Dictionary<string, string> { ["Service"] = "orders" }
            }, writer);

            metrics.Count("OrdersCreated", 3);

            using var json = JsonDocument.Parse(writer.ToString());
            var root = json.RootElement;
            var metricDirective = root.GetProperty("_aws").GetProperty("CloudWatchMetrics")[0];

            Assert.Equal("MyApp", metricDirective.GetProperty("Namespace").GetString());
            Assert.Equal("OrdersCreated", metricDirective.GetProperty("Metrics")[0].GetProperty("Name").GetString());
            Assert.Equal(3, root.GetProperty("OrdersCreated").GetDouble());
            Assert.Equal("orders", root.GetProperty("Service").GetString());
        }

        [Fact]
        public async Task TimeAsync_RecordsDuration_AndReturnsResult()
        {
            using var writer = new StringWriter();
            var metrics = new EmfMetrics(new EmfMetricsOptions(), writer);

            var result = await metrics.TimeAsync("Op", () => Task.FromResult(42));

            Assert.Equal(42, result);
            using var json = JsonDocument.Parse(writer.ToString());
            Assert.True(json.RootElement.GetProperty("Op").GetDouble() >= 0);
        }
    }

    public class EventBridgeEventBusTests
    {
        [Fact]
        public async Task Publish_MapsSourceDetailTypeAndPayload()
        {
            PutEventsRequest? captured = null;
            var client = new Mock<IAmazonEventBridge>();
            client.Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<PutEventsRequest, CancellationToken>((r, _) => captured = r)
                  .ReturnsAsync(new PutEventsResponse { FailedEntryCount = 0, Entries = new List<PutEventsResultEntry>() });

            var bus = new EventBridgeEventBus(client.Object, new EventBridgeOptions { Source = "com.myco.orders", EventBusName = "orders-bus" });
            await bus.PublishAsync(new TestEvent());

            var entry = Assert.Single(captured!.Entries);
            Assert.Equal("com.myco.orders", entry.Source);
            Assert.Equal(nameof(TestEvent), entry.DetailType);
            Assert.Equal("orders-bus", entry.EventBusName);
            Assert.Contains("\"Id\"", entry.Detail); // serialized event payload
        }

        [Fact]
        public async Task Publish_FailedEntry_Throws()
        {
            var client = new Mock<IAmazonEventBridge>();
            client.Setup(c => c.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new PutEventsResponse
                  {
                      FailedEntryCount = 1,
                      Entries = new List<PutEventsResultEntry> { new() { ErrorCode = "ThrottlingException", ErrorMessage = "slow down" } }
                  });

            var bus = new EventBridgeEventBus(client.Object, new EventBridgeOptions());
            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new TestEvent()));
        }
    }

    public class EventBridgeSchedulerTests
    {
        [Fact]
        public async Task CreateOrUpdate_FallsBackToUpdate_WhenScheduleExists()
        {
            var client = new Mock<IAmazonScheduler>();
            client.Setup(c => c.CreateScheduleAsync(It.IsAny<CreateScheduleRequest>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new Amazon.Scheduler.Model.ConflictException("exists"));
            client.Setup(c => c.UpdateScheduleAsync(It.IsAny<UpdateScheduleRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new UpdateScheduleResponse());

            var scheduler = new EventBridgeScheduler(client.Object);
            await scheduler.CreateOrUpdateScheduleAsync("nightly", "cron(0 2 * * ? *)", "arn:aws:lambda:x", "arn:aws:iam:role");

            client.Verify(c => c.UpdateScheduleAsync(
                It.Is<UpdateScheduleRequest>(r => r.Name == "nightly" && r.ScheduleExpression == "cron(0 2 * * ? *)"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class StepFunctionsWorkflowClientTests
    {
        [Fact]
        public async Task Start_WithArn_StartsExecutionDirectly()
        {
            StartExecutionRequest? captured = null;
            var client = new Mock<IAmazonStepFunctions>();
            client.Setup(c => c.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<StartExecutionRequest, CancellationToken>((r, _) => captured = r)
                  .ReturnsAsync(new StartExecutionResponse { ExecutionArn = "arn:aws:states:exec:1" });

            var workflow = new StepFunctionsWorkflowClient(client.Object);
            var executionId = await workflow.StartAsync("arn:aws:states:us-east-1:1:stateMachine:Orders", new { OrderId = 7 });

            Assert.Equal("arn:aws:states:exec:1", executionId);
            Assert.Equal("arn:aws:states:us-east-1:1:stateMachine:Orders", captured!.StateMachineArn);
            Assert.Contains("\"OrderId\":7", captured.Input);
            client.Verify(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Start_WithName_ResolvesArnViaListing()
        {
            var client = new Mock<IAmazonStepFunctions>();
            client.Setup(c => c.ListStateMachinesAsync(It.IsAny<ListStateMachinesRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new ListStateMachinesResponse
                  {
                      StateMachines = new List<StateMachineListItem>
                      {
                          new() { Name = "UniqueFlow42", StateMachineArn = "arn:aws:states:resolved:42" }
                      }
                  });
            client.Setup(c => c.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new StartExecutionResponse { ExecutionArn = "arn:exec" });

            var workflow = new StepFunctionsWorkflowClient(client.Object);
            await workflow.StartAsync("UniqueFlow42", new { });

            client.Verify(c => c.StartExecutionAsync(
                It.Is<StartExecutionRequest>(r => r.StateMachineArn == "arn:aws:states:resolved:42"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class KinesisStreamPublisherTests
    {
        [Fact]
        public async Task PublishBatch_ChunksTo500_AndCountsFailures()
        {
            var calls = new List<PutRecordsRequest>();
            var client = new Mock<IAmazonKinesis>();
            client.Setup(c => c.PutRecordsAsync(It.IsAny<PutRecordsRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<PutRecordsRequest, CancellationToken>((r, _) => calls.Add(r))
                  .ReturnsAsync(new PutRecordsResponse { FailedRecordCount = 1 });

            var publisher = new KinesisStreamPublisher(client.Object);
            var succeeded = await publisher.PublishBatchAsync("clicks", Enumerable.Range(0, 600).Select(i => new { i }));

            Assert.Equal(2, calls.Count);           // 500 + 100
            Assert.Equal(500, calls[0].Records.Count);
            Assert.Equal(100, calls[1].Records.Count);
            Assert.Equal(598, succeeded);           // one failure per batch
        }
    }
}
