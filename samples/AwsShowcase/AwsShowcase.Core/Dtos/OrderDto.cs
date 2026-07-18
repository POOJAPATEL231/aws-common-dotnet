using AwsShowcase.Entity;

namespace AwsShowcase.Core.Dtos;

/// <summary>
/// Transport shape for orders - never expose entities over the wire. Keeping the
/// DTO separate lets the entity evolve without breaking API consumers.
/// </summary>
public record OrderDto
{
    public string Id { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public double Price { get; init; }
    public string Status { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
    public DateTime? CreatedUtc { get; init; }
    public DateTime? ModifiedUtc { get; init; }

    public static OrderDto FromEntity(Order order) => new()
    {
        Id = order.Id,
        CustomerEmail = order.CustomerEmail,
        ProductName = order.ProductName,
        Quantity = order.Quantity,
        Price = order.Price,
        Status = order.Status.ToString(),
        Tags = order.Tags,
        CreatedUtc = order.CreateDateTimeUtc,
        ModifiedUtc = order.ModifyDateTimeUtc
    };
}

/// <summary>Paged envelope so paging metadata travels with the items.</summary>
public record PagedResultDto<T>(IEnumerable<T> Items, int Page, int PageSize, int TotalRecords, int TotalPages);
