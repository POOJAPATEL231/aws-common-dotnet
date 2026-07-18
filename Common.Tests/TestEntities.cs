using Domain.Common.Entities;

namespace Common.Tests
{
    public enum OrderStatus
    {
        Pending = 0,
        Paid = 1,
        Shipped = 2
    }

    public class Address
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }

    /// <summary>
    /// Test entity exercising the scalar types, collections and nested complex
    /// objects the DynamoDB layer must round-trip. "Status" is intentionally a
    /// DynamoDB reserved word to exercise alias handling.
    /// </summary>
    public class TestOrder : DocEntity
    {
        public override string PartitionKey { get; set; } = "order";

        public string CustomerName { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public double Price { get; set; }

        public bool IsExpress { get; set; }

        public Guid OrderRef { get; set; }

        public DateTime OrderedAtUtc { get; set; }

        public DateTimeOffset PlacedAt { get; set; }

        public TimeSpan ProcessingTime { get; set; }

        public OrderStatus Status { get; set; }

        public List<string> Tags { get; set; } = new();

        public Address? ShippingAddress { get; set; }
    }
}
