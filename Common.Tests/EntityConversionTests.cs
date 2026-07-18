using Persistence.Common.AWS.EntityManagement;
using Xunit;

namespace Common.Tests
{
    public class EntityConversionTests
    {
        private static TestOrder CreateOrder()
        {
            var order = new TestOrder
            {
                CustomerName = "alice",
                Quantity = 7,
                Price = 12.34,
                IsExpress = true,
                OrderRef = Guid.Parse("6f1c8a7e-2b3d-4c5e-9f0a-1b2c3d4e5f6a"),
                OrderedAtUtc = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                PlacedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(5.5)),
                ProcessingTime = TimeSpan.FromMinutes(90),
                Status = OrderStatus.Shipped,
                Tags = new List<string> { "fragile", "priority" },
                ShippingAddress = new Address { Street = "1 Main St", City = "Pune", Zip = "411001" }
            };
            order.SetId("order:test-1");
            return order;
        }

        [Fact]
        public void RoundTrip_PreservesScalarProperties()
        {
            var original = CreateOrder();

            var document = EntityDocumentConverter.ToConfiguredDocument(original);
            var restored = EntityDocumentConverter.FromConfiguredDocument<TestOrder>(document);

            Assert.Equal(original.CustomerName, restored.CustomerName);
            Assert.Equal(original.Quantity, restored.Quantity);
            Assert.Equal(original.Price, restored.Price);
            Assert.Equal(original.IsExpress, restored.IsExpress);
            Assert.Equal(original.Status, restored.Status);
        }

        [Fact]
        public void RoundTrip_PreservesNonIConvertibleScalars()
        {
            // Regression: Guid / DateTimeOffset / TimeSpan do not implement IConvertible
            // and previously threw InvalidCastException on read.
            var original = CreateOrder();

            var document = EntityDocumentConverter.ToConfiguredDocument(original);
            var restored = EntityDocumentConverter.FromConfiguredDocument<TestOrder>(document);

            Assert.Equal(original.OrderRef, restored.OrderRef);
            Assert.Equal(original.PlacedAt, restored.PlacedAt);
            Assert.Equal(original.ProcessingTime, restored.ProcessingTime);
            Assert.Equal(original.OrderedAtUtc, restored.OrderedAtUtc);
        }

        [Fact]
        public void RoundTrip_PreservesCollectionsAndNestedObjects()
        {
            var original = CreateOrder();

            var document = EntityDocumentConverter.ToConfiguredDocument(original);
            var restored = EntityDocumentConverter.FromConfiguredDocument<TestOrder>(document);

            Assert.Equal(original.Tags, restored.Tags);
            Assert.NotNull(restored.ShippingAddress);
            Assert.Equal(original.ShippingAddress!.Street, restored.ShippingAddress!.Street);
            Assert.Equal(original.ShippingAddress.City, restored.ShippingAddress.City);
            Assert.Equal(original.ShippingAddress.Zip, restored.ShippingAddress.Zip);
        }

        [Fact]
        public void RoundTrip_PreservesIdAndPartitionKey()
        {
            var original = CreateOrder();

            var document = EntityDocumentConverter.ToConfiguredDocument(original);
            var restored = EntityDocumentConverter.FromConfiguredDocument<TestOrder>(document);

            Assert.Equal(original.Id, restored.Id);
            Assert.Equal(original.PartitionKey, restored.PartitionKey);
        }
    }
}
