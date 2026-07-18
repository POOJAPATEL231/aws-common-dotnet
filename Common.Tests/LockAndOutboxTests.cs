using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Application.Common.Event;
using Domain.Common.Event;
using Infrastructure.Common.AWS.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Persistence.Common.AWS;
using Persistence.Common.AWS.Locking;
using Persistence.Common.AWS.Outbox;
using System.Linq.Expressions;
using Xunit;

namespace Common.Tests
{
    public class CognitoIdentityServiceTests
    {
        [Fact]
        public async Task GetUser_NotFound_ReturnsNull()
        {
            var client = new Mock<IAmazonCognitoIdentityProvider>();
            client.Setup(c => c.AdminGetUserAsync(It.IsAny<AdminGetUserRequest>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new UserNotFoundException("nope"));

            var service = new CognitoIdentityService(client.Object, new CognitoIdentityOptions { UserPoolId = "pool-1" });

            Assert.Null(await service.GetUserAsync("ghost"));
        }

        [Fact]
        public async Task CreateUser_Suppressed_SetsMessageActionAndEmail()
        {
            AdminCreateUserRequest? captured = null;
            var client = new Mock<IAmazonCognitoIdentityProvider>();
            client.Setup(c => c.AdminCreateUserAsync(It.IsAny<AdminCreateUserRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<AdminCreateUserRequest, CancellationToken>((r, _) => captured = r)
                  .ReturnsAsync(new AdminCreateUserResponse
                  {
                      User = new UserType { Username = "alice", Enabled = true, UserStatus = UserStatusType.FORCE_CHANGE_PASSWORD }
                  });

            var service = new CognitoIdentityService(client.Object, new CognitoIdentityOptions { UserPoolId = "pool-1" });
            var user = await service.CreateUserAsync("alice", "alice@example.com", suppressInviteMessage: true);

            Assert.Equal("alice", user.UserName);
            Assert.Equal(MessageActionType.SUPPRESS, captured!.MessageAction);
            Assert.Contains(captured.UserAttributes, a => a.Name == "email" && a.Value == "alice@example.com");
            Assert.Equal("pool-1", captured.UserPoolId);
        }
    }

    public class DynamoDbDistributedLockTests
    {
        private static Mock<IAmazonDynamoDB> ClientWithTable()
        {
            var client = new Mock<IAmazonDynamoDB>();
            client.Setup(c => c.DescribeTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new DescribeTableResponse());
            return client;
        }

        [Fact]
        public async Task Acquire_Succeeds_AndReleaseDeletesConditionally()
        {
            var client = ClientWithTable();
            PutItemRequest? put = null;
            DeleteItemRequest? delete = null;
            client.Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<PutItemRequest, CancellationToken>((r, _) => put = r)
                  .ReturnsAsync(new PutItemResponse());
            client.Setup(c => c.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<DeleteItemRequest, CancellationToken>((r, _) => delete = r)
                  .ReturnsAsync(new DeleteItemResponse());

            var distributedLock = new DynamoDbDistributedLock(client.Object, new DynamoDbLockOptions());

            var handle = await distributedLock.TryAcquireAsync("nightly-job", TimeSpan.FromMinutes(5));

            Assert.NotNull(handle);
            Assert.Contains("attribute_not_exists", put!.ConditionExpression);
            Assert.Contains("ExpiresAtEpochMs < :now", put.ConditionExpression);

            await handle!.DisposeAsync();
            Assert.NotNull(delete);
            Assert.Equal("OwnerId = :owner", delete!.ConditionExpression);
            Assert.Equal(handle.OwnerId, delete.ExpressionAttributeValues[":owner"].S);
        }

        [Fact]
        public async Task Acquire_WhenHeld_ReturnsNull()
        {
            var client = ClientWithTable();
            client.Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new ConditionalCheckFailedException("held"));

            var distributedLock = new DynamoDbDistributedLock(client.Object, new DynamoDbLockOptions());

            Assert.Null(await distributedLock.TryAcquireAsync("nightly-job", TimeSpan.FromMinutes(5)));
        }
    }

    public class OutboxTests
    {
        [Fact]
        public async Task Dispatch_PublishesPendingEvent_AndMarksProcessed()
        {
            var message = new OutboxMessage
            {
                EventType = typeof(TestEvent).AssemblyQualifiedName!,
                Payload = System.Text.Json.JsonSerializer.Serialize(new TestEvent()),
                OccurredOnUtc = DateTime.UtcNow,
                Processed = false
            };
            message.SetId("outbox:1");

            var outboxSet = new Mock<IDynamoDbSet<OutboxMessage>>();
            outboxSet.Setup(s => s.GetItemsAsync(It.IsAny<Expression<Func<OutboxMessage, bool>>?>(),
                    It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OutboxMessage> { message });
            outboxSet.Setup(s => s.UpdateAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OutboxMessage m, CancellationToken _) => m);

            var publisher = new Mock<IIntegrationEventPublisher>();
            publisher.Setup(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var services = new ServiceCollection();
            services.AddScoped(_ => outboxSet.Object);
            services.AddScoped(_ => publisher.Object);
            var provider = services.BuildServiceProvider();

            var dispatcher = new OutboxDispatcherService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new OutboxOptions(),
                NullLogger<OutboxDispatcherService>.Instance);

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            publisher.Verify(p => p.PublishAsync(It.Is<IntegrationEvent>(e => e is TestEvent), It.IsAny<CancellationToken>()), Times.Once);
            Assert.True(message.Processed);
            Assert.NotNull(message.ProcessedOnUtc);
            outboxSet.Verify(s => s.UpdateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Dispatch_PublishFailure_IncrementsAttempts_WithoutMarkingProcessed()
        {
            var message = new OutboxMessage
            {
                EventType = typeof(TestEvent).AssemblyQualifiedName!,
                Payload = System.Text.Json.JsonSerializer.Serialize(new TestEvent()),
                Processed = false
            };
            message.SetId("outbox:2");

            var outboxSet = new Mock<IDynamoDbSet<OutboxMessage>>();
            outboxSet.Setup(s => s.GetItemsAsync(It.IsAny<Expression<Func<OutboxMessage, bool>>?>(),
                    It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OutboxMessage> { message });
            outboxSet.Setup(s => s.UpdateAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OutboxMessage m, CancellationToken _) => m);

            var publisher = new Mock<IIntegrationEventPublisher>();
            publisher.Setup(p => p.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("bus down"));

            var services = new ServiceCollection();
            services.AddScoped(_ => outboxSet.Object);
            services.AddScoped(_ => publisher.Object);
            var provider = services.BuildServiceProvider();

            var dispatcher = new OutboxDispatcherService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new OutboxOptions(),
                NullLogger<OutboxDispatcherService>.Instance);

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            Assert.False(message.Processed);
            Assert.Equal(1, message.Attempts);
            Assert.Equal("bus down", message.LastError);
        }
    }
}
