using Application.Common.Event;
using Domain.Common.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Persistence.Common.AWS.Outbox
{
    public record OutboxOptions
    {
        /// <summary>Seconds between polls for unprocessed messages.</summary>
        public int PollIntervalSeconds { get; init; } = 10;

        /// <summary>Maximum messages fetched per poll.</summary>
        public int BatchSize { get; init; } = 25;

        /// <summary>Messages exceeding this many failed attempts are skipped (poison messages).</summary>
        public int MaxAttempts { get; init; } = 10;
    }

    /// <summary>
    /// Background service completing the transactional-outbox pattern: polls the
    /// outbox table for unprocessed messages, publishes each to the configured
    /// <see cref="IIntegrationEventPublisher"/> and marks it processed. Failures are
    /// retried on later polls with an attempt counter; poison messages are skipped
    /// after <see cref="OutboxOptions.MaxAttempts"/>.
    /// </summary>
    public class OutboxDispatcherService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OutboxOptions _options;
        private readonly ILogger<OutboxDispatcherService> _logger;

        public OutboxDispatcherService(IServiceScopeFactory scopeFactory, OutboxOptions options, ILogger<OutboxDispatcherService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DispatchPendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox dispatch cycle failed.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        internal async Task DispatchPendingAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var outboxSet = scope.ServiceProvider.GetRequiredService<IDynamoDbSet<OutboxMessage>>();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

            var pending = await outboxSet.GetItemsAsync(
                m => !m.Processed, limit: _options.BatchSize, cancellationToken: cancellationToken);

            foreach (var message in pending.OrderBy(m => m.OccurredOnUtc))
            {
                if (message.Attempts >= _options.MaxAttempts)
                {
                    continue; // poison message - leave for manual inspection
                }

                try
                {
                    var eventType = Type.GetType(message.EventType)
                        ?? throw new InvalidOperationException($"Cannot resolve event type '{message.EventType}'.");

                    var integrationEvent = (IntegrationEvent?)JsonSerializer.Deserialize(message.Payload, eventType)
                        ?? throw new InvalidOperationException("Outbox payload deserialized to null.");

                    await publisher.PublishAsync(integrationEvent, cancellationToken);

                    message.Processed = true;
                    message.ProcessedOnUtc = DateTime.UtcNow;
                    await outboxSet.UpdateAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish outbox message {MessageId} (attempt {Attempt}).",
                        message.Id, message.Attempts + 1);

                    message.Attempts++;
                    message.LastError = ex.Message;
                    await outboxSet.UpdateAsync(message, cancellationToken);
                }
            }
        }
    }
}
