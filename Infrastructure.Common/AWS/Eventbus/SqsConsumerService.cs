using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Common.AWS.Eventbus
{
    public record SqsConsumerOptions
    {
        /// <summary>Master switch; when false the service starts and immediately idles.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// Integration-event CLR names to consume; queue names are derived by the bus
        /// convention (OrderCreatedIntegrationEvent -> "ordercreated_Queue.fifo").
        /// </summary>
        public List<string> EventNames { get; init; } = new();

        /// <summary>Explicit queue names to poll, for queues outside the naming convention.</summary>
        public List<string> QueueNames { get; init; } = new();

        /// <summary>SQS long-poll wait per receive call (max 20).</summary>
        public int WaitTimeSeconds { get; init; } = 10;

        /// <summary>Messages fetched per receive call (max 10).</summary>
        public int MaxMessagesPerPoll { get; init; } = 10;

        /// <summary>Pause after an infrastructure error (missing queue, network) before retrying.</summary>
        public int ErrorBackoffSeconds { get; init; } = 30;

        public IReadOnlyList<string> ResolveQueueNames()
        {
            var fromEvents = EventNames.Select(eventName =>
                $"{eventName.Replace("IntegrationEvent", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}_Queue.fifo");
            return QueueNames.Concat(fromEvents).Distinct().ToList();
        }
    }

    /// <summary>
    /// In-process alternative to the QueueEventDispatcher Lambda: long-polls the
    /// event-bus queues from inside the host application and hands each message to
    /// <see cref="ISqsMessageDispatcher"/>. Successful (and unroutable) messages are
    /// deleted; handler failures are left for SQS redelivery and eventually the DLQ.
    /// Ideal for local development (works with plain LocalStack, no Lambda) and for
    /// ECS/EC2/container deployments; use the Lambda for serverless environments.
    /// </summary>
    public class SqsConsumerService : BackgroundService
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SqsConsumerOptions _options;
        private readonly ILogger<SqsConsumerService> _logger;
        private readonly Dictionary<string, string> _queueUrlCache = new();

        public SqsConsumerService(IAmazonSQS sqsClient, IServiceScopeFactory scopeFactory,
            SqsConsumerOptions options, ILogger<SqsConsumerService> logger)
        {
            _sqsClient = sqsClient;
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queueNames = _options.ResolveQueueNames();
            if (!_options.Enabled || queueNames.Count == 0)
            {
                _logger.LogInformation("SQS consumer disabled or no queues configured; idling.");
                return;
            }

            _logger.LogInformation("SQS consumer polling {QueueCount} queue(s): {Queues}",
                queueNames.Count, string.Join(", ", queueNames));

            while (!stoppingToken.IsCancellationRequested)
            {
                var hadInfrastructureError = false;

                foreach (var queueName in queueNames)
                {
                    try
                    {
                        await PollQueueOnceAsync(queueName, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        hadInfrastructureError = true;
                        _queueUrlCache.Remove(queueName); // re-resolve after outages/re-creation
                        _logger.LogWarning(ex, "Polling {Queue} failed; backing off {Backoff}s.",
                            queueName, _options.ErrorBackoffSeconds);
                    }
                }

                if (hadInfrastructureError)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_options.ErrorBackoffSeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>One receive/dispatch/delete cycle for a queue; returns messages received.</summary>
        internal async Task<int> PollQueueOnceAsync(string queueName, CancellationToken cancellationToken)
        {
            var queueUrl = await GetQueueUrlAsync(queueName, cancellationToken);

            var response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = Math.Clamp(_options.MaxMessagesPerPoll, 1, 10),
                WaitTimeSeconds = Math.Clamp(_options.WaitTimeSeconds, 0, 20)
            }, cancellationToken);

            if (response.Messages is not { Count: > 0 })
            {
                return 0;
            }

            foreach (var message in response.Messages)
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<ISqsMessageDispatcher>();

                try
                {
                    // False = unroutable/no handlers: the dispatcher logged it - delete
                    // anyway so a misrouted message cannot poison the queue forever.
                    await dispatcher.DispatchAsync(message.Body, cancellationToken);
                    await _sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Leave the message: SQS redelivers after the visibility timeout and
                    // parks it in the DLQ once the redrive policy's retry count is hit.
                    _logger.LogError(ex, "Handler failed for message {MessageId} on {Queue}; leaving for redelivery.",
                        message.MessageId, queueName);
                }
            }

            return response.Messages.Count;
        }

        private async Task<string> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken)
        {
            if (_queueUrlCache.TryGetValue(queueName, out var cachedUrl))
            {
                return cachedUrl;
            }

            var response = await _sqsClient.GetQueueUrlAsync(queueName, cancellationToken);
            _queueUrlCache[queueName] = response.QueueUrl;
            return response.QueueUrl;
        }
    }
}
