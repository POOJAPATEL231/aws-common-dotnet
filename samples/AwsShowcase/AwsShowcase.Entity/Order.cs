using Domain.Common.Entities;

namespace AwsShowcase.Entity;

public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Shipped = 2,
    Cancelled = 3
}

/// <summary>
/// Demo aggregate root persisted in DynamoDB through the EF-style persistence layer.
/// Only domain concerns live here - no persistence or transport attributes.
/// </summary>
public class Order : DocEntity
{
    public override string PartitionKey { get; set; } = "order";

    public string CustomerEmail { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public double Price { get; set; }

    public OrderStatus Status { get; set; }

    public List<string> Tags { get; set; } = new();

    public void ChangeStatus(OrderStatus newStatus)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("A cancelled order cannot change status.");
        }

        Status = newStatus;
    }
}
