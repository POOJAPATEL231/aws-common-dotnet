using Application.Common.Event;
using Domain.Common.Event;
using Xunit;

namespace Common.Tests
{
    public record TestEvent : IntegrationEvent;

    public class TestEventHandler : IIntegrationEventHandler<TestEvent>
    {
        public Task Handle(TestEvent integrationEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public class InMemoryEventBusSubscriptionsManagerTests
    {
        [Fact]
        public void UnknownEvent_ReturnsEmptyHandlers_InsteadOfThrowing()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();

            // Regression: previously threw KeyNotFoundException.
            var handlers = manager.GetHandlersForEvent("does-not-exist");

            Assert.Empty(handlers);
        }

        [Fact]
        public void AddSubscription_IsDiscoverable()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();

            manager.AddSubscription<TestEvent, TestEventHandler>();

            Assert.True(manager.HasSubscriptionsForEvent<TestEvent>());
            var handlers = manager.GetHandlersForEvent<TestEvent>().ToList();
            Assert.Single(handlers);
            Assert.Equal(typeof(TestEventHandler), handlers[0].HandlerType);
            Assert.Equal(typeof(TestEvent), manager.GetEventTypeByName(nameof(TestEvent)));
        }

        [Fact]
        public void DuplicateSubscription_Throws()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<TestEvent, TestEventHandler>();

            Assert.Throws<ArgumentException>(() => manager.AddSubscription<TestEvent, TestEventHandler>());
        }

        [Fact]
        public void RemoveLastSubscription_RaisesOnEventRemoved()
        {
            var manager = new InMemoryEventBusSubscriptionsManager();
            manager.AddSubscription<TestEvent, TestEventHandler>();

            string? removedEvent = null;
            manager.OnEventRemoved += (_, name) => removedEvent = name;

            manager.RemoveSubscription<TestEvent, TestEventHandler>();

            Assert.Equal(nameof(TestEvent), removedEvent);
            Assert.False(manager.HasSubscriptionsForEvent<TestEvent>());
        }
    }

    public class SubscriptionTests
    {
        [Fact]
        public void Subscription_RoundTripsHandlerType()
        {
            var subscription = Subscription.Typed(typeof(TestEventHandler));

            Assert.False(subscription.IsDynamic);
            Assert.Equal(typeof(TestEventHandler), subscription.ResolveHandlerType());
        }

        [Fact]
        public void Subscription_SurvivesJsonSerialization()
        {
            // The cache-backed manager persists subscriptions as JSON.
            var original = Subscription.Dynamic(typeof(TestEventHandler));

            var json = System.Text.Json.JsonSerializer.Serialize(original);
            var restored = System.Text.Json.JsonSerializer.Deserialize<Subscription>(json);

            Assert.NotNull(restored);
            Assert.True(restored!.IsDynamic);
            Assert.Equal(typeof(TestEventHandler), restored.ResolveHandlerType());
        }
    }
}
