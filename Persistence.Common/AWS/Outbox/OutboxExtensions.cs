using Domain.Common.Event;
using System.Text.Json;

namespace Persistence.Common.AWS.Outbox
{
    public static class OutboxExtensions
    {
        /// <summary>
        /// Stages an integration event in the outbox so it is written in the SAME
        /// atomic transaction as the other tracked changes on the next
        /// SaveChangesAsync/SaveEntitiesAsync. The <see cref="OutboxDispatcherService"/>
        /// publishes it to the event bus afterwards - guaranteeing the event is never
        /// lost when the save succeeds, and never published when the save fails.
        /// </summary>
        public static OutboxMessage AddOutboxMessage(this BaseDynamoDbContext context, IntegrationEvent integrationEvent)
        {
            // Ensure the OutboxMessage set is registered with the context so the
            // transactional save can build a write item for it.
            context.Set<OutboxMessage>();

            var message = new OutboxMessage
            {
                EventType = integrationEvent.GetType().AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
                OccurredOnUtc = DateTime.UtcNow,
                Processed = false
            };
            message.SetId(DynamoUtils.GenerateId(message));

            context.Add(message);
            return message;
        }
    }
}
