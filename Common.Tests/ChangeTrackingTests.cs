using Domain.Common.Dates;
using Domain.Common.Identity;
using MediatR;
using Moq;
using Persistence.Common.AWS;
using Persistence.Common.AWS.EntityManagement;
using Xunit;

namespace Common.Tests
{
    public class ChangeTrackingTests
    {
        [Fact]
        public void NoMutation_ReportsNoChanges()
        {
            var order = NewOrder();
            var entry = new EntityEntry(order, EntityState.Unchanged);

            Assert.False(entry.HasChanges());
        }

        [Fact]
        public void SimplePropertyMutation_IsDetected()
        {
            var order = NewOrder();
            var entry = new EntityEntry(order, EntityState.Unchanged);

            order.Quantity = 99;

            Assert.True(entry.HasChanges());
        }

        [Fact]
        public void ComplexPropertyContentMutation_IsDetected()
        {
            // Regression: an inverted JSON comparison plus snapshot aliasing previously
            // made every content change to a complex property invisible (lost updates).
            var order = NewOrder();
            var entry = new EntityEntry(order, EntityState.Unchanged);

            order.ShippingAddress!.City = "Mumbai";

            Assert.True(entry.HasChanges());
        }

        [Fact]
        public void CollectionMutation_IsDetected()
        {
            var order = NewOrder();
            var entry = new EntityEntry(order, EntityState.Unchanged);

            order.Tags.Add("new-tag");

            Assert.True(entry.HasChanges());
        }

        [Fact]
        public void ResetAfterSave_ClearsPendingChanges()
        {
            var order = NewOrder();
            var entry = new EntityEntry(order, EntityState.Unchanged);
            order.Quantity = 99;
            Assert.True(entry.HasChanges());

            entry.ResetAfterSave();

            Assert.Equal(EntityState.Unchanged, entry.State);
            Assert.False(entry.HasChanges());
        }

        [Fact]
        public void TwoNewEntities_AreTrackedSeparately()
        {
            // Regression: BaseEntity's Id-based Equals/GetHashCode made two new entities
            // (both with an empty Id) collide in the change tracker, silently dropping one.
            var context = new TestContext(
                Mock.Of<IServiceProvider>(),
                Mock.Of<ICurrentUser>(),
                Mock.Of<IDateTime>(),
                Mock.Of<IMediator>());

            var first = NewOrder();
            var second = NewOrder();

            context.Add(first);
            context.Add(second);

            var tracked = context.GetTrackedEntities<TestOrder>();
            Assert.Equal(2, tracked.Count);
            Assert.Contains(first, tracked);
            Assert.Contains(second, tracked);
        }

        private static TestOrder NewOrder() => new()
        {
            CustomerName = "alice",
            Quantity = 1,
            Tags = new List<string> { "a" },
            ShippingAddress = new Address { Street = "1 Main St", City = "Pune", Zip = "411001" }
        };

        /// <summary>Minimal concrete context: no set properties, used only for tracking tests.</summary>
        private sealed class TestContext : BaseDynamoDbContext
        {
            public TestContext(IServiceProvider serviceProvider, ICurrentUser currentUser, IDateTime dateTime, IMediator mediator)
                : base(serviceProvider, currentUser, dateTime, mediator)
            {
            }
        }
    }
}
