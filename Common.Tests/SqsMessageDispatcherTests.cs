using Application.Common;
using Application.Common.Event;
using Infrastructure.Common.AWS.Eventbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Common.Tests
{
    /// <summary>Typed handler that records what it received.</summary>
    public class RecordingOrderHandler : IIntegrationEventHandler<TestEvent>
    {
        public List<TestEvent> Received { get; } = new();

        public Task Handle(TestEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            Received.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>Dynamic handler that records raw payloads.</summary>
    public class RecordingDynamicHandler : IDynamicIntegrationEventHandler
    {
        public List<string> Payloads { get; } = new();

        public Task Handle(JsonDocument eventData, CancellationToken cancellationToken = default)
        {
            Payloads.Add(eventData.RootElement.ToString());
            return Task.CompletedTask;
        }
    }

    public class SqsMessageDispatcherTests
    {
        private static (SqsMessageDispatcher Dispatcher, IAsyncEventBusSubscriptionsManager Subscriptions,
            RecordingOrderHandler TypedHandler, RecordingDynamicHandler DynamicHandler) CreateHarness()
        {
            var typedHandler = new RecordingOrderHandler();
            var dynamicHandler = new RecordingDynamicHandler();

            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            services.AddScoped<ICache, Infrastructure.Common.Cache.Cache>();
            services.AddScoped<IAsyncEventBusSubscriptionsManager, EventBusSubscriptionsManager>();
            services.AddSingleton(typedHandler);
            services.AddSingleton(dynamicHandler);
            var provider = services.BuildServiceProvider();

            var subscriptions = provider.GetRequiredService<IAsyncEventBusSubscriptionsManager>();
            var dispatcher = new SqsMessageDispatcher(subscriptions, provider, NullLogger<SqsMessageDispatcher>.Instance);
            return (dispatcher, subscriptions, typedHandler, dynamicHandler);
        }

        private static string SnsEnvelope(string eventName, string messageJson) => JsonSerializer.Serialize(new
        {
            Type = "Notification",
            MessageId = Guid.NewGuid().ToString(),
            TopicArn = "arn:aws:sns:us-east-1:000000000000:demo.fifo",
            Message = messageJson,
            MessageAttributes = new Dictionary<string, object>
            {
                ["Subject"] = new { Type = "String", Value = eventName }
            }
        });

        [Fact]
        public async Task TypedEvent_IsDeserializedAndDelivered_ToSubscribedHandler()
        {
            var (dispatcher, subscriptions, typedHandler, _) = CreateHarness();
            await subscriptions.AddSubscriptionAsync<TestEvent, RecordingOrderHandler>();

            var integrationEvent = new TestEvent();
            var delivered = await dispatcher.DispatchAsync(SnsEnvelope(nameof(TestEvent), JsonSerializer.Serialize(integrationEvent)));

            Assert.True(delivered);
            var received = Assert.Single(typedHandler.Received);
            Assert.Equal(integrationEvent.Id, received.Id);
        }

        [Fact]
        public async Task DynamicEvent_DeliversRawPayload()
        {
            var (dispatcher, subscriptions, _, dynamicHandler) = CreateHarness();
            await subscriptions.AddDynamicSubscriptionAsync<RecordingDynamicHandler>("price-changed");

            var delivered = await dispatcher.DispatchAsync(SnsEnvelope("price-changed", "{\"sku\":\"A1\",\"price\":9.99}"));

            Assert.True(delivered);
            Assert.Contains("A1", Assert.Single(dynamicHandler.Payloads));
        }

        [Fact]
        public async Task UnsubscribedEvent_IsSkipped_NotAnError()
        {
            var (dispatcher, _, typedHandler, _) = CreateHarness();

            var delivered = await dispatcher.DispatchAsync(SnsEnvelope("NobodyListensToThis", "{}"));

            Assert.False(delivered);
            Assert.Empty(typedHandler.Received);
        }

        [Fact]
        public async Task NonSnsBody_IsRejectedGracefully()
        {
            var (dispatcher, _, _, _) = CreateHarness();

            Assert.False(await dispatcher.DispatchAsync("{\"just\":\"some json\"}"));
            Assert.False(await dispatcher.DispatchAsync("not json at all"));
        }
    }
}
