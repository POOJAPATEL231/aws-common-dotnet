using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.Eventbus
{
    public interface IEventBusPersisterConnection : IDisposable
    {
        Task PublishAsync(string message, string eventName, CancellationToken cancellationToken = default);

        Task<string> GetOrCreateTopicWithSetupAsync(string topicName, CancellationToken cancellationToken = default);

        Task<string> CreateQueueWithDlqAndSubscribeAsync(string topicArn, string queueName, string dlqName, CancellationToken cancellationToken = default);

        Task AssociateLambdaWithQueueAsync(string queueName, int maxConcurrentCalls, CancellationToken cancellationToken = default);

        Task UnsubscribeAsync(string topicName);

        string GetTopicName(string eventName);
    }
}
