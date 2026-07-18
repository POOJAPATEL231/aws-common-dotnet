using Domain.Common.Event;

namespace AwsShowcase.Entity;

/// <summary>
/// Published (via the transactional outbox) after an order is created, so other
/// services can react without coupling to this one.
/// </summary>
public record OrderCreatedIntegrationEvent(string OrderId, string CustomerEmail, string ProductName) : IntegrationEvent;
