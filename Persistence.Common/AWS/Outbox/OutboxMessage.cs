using Domain.Common.Entities;

namespace Persistence.Common.AWS.Outbox
{
    /// <summary>
    /// A pending integration event stored transactionally alongside the domain change
    /// that produced it (transactional outbox pattern). The
    /// <see cref="OutboxDispatcherService"/> publishes unprocessed messages to the
    /// event bus and marks them processed.
    /// </summary>
    public class OutboxMessage : DocEntity
    {
        public override string PartitionKey { get; set; } = "outbox";

        /// <summary>Assembly-qualified CLR type of the serialized integration event.</summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>The integration event serialized as JSON.</summary>
        public string Payload { get; set; } = string.Empty;

        public DateTime OccurredOnUtc { get; set; }

        /// <summary>
        /// Dispatch state. A boolean is used (rather than a nullable timestamp) so the
        /// dispatcher can filter on it - DynamoDB stores nulls as a NULL-typed
        /// attribute, which attribute_not_exists() would not match.
        /// </summary>
        public bool Processed { get; set; }

        public DateTime? ProcessedOnUtc { get; set; }

        /// <summary>Number of failed publish attempts so far.</summary>
        public int Attempts { get; set; }

        /// <summary>Last publish error, for diagnostics.</summary>
        public string? LastError { get; set; }
    }
}
