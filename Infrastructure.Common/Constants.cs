using Infrastructure.Common.AWS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common
{
    public static class Constants
    {
        public static readonly string WapAlgorithm = "RSAES_OAEP_SHA_256";
        public static readonly string HandlersKeyPrefix = "EventBus:IntegrationEventHandlers:";

        public static readonly string EventTypesKey = "EventBus:IntegrationEventTypes";

        public static readonly string QueueNameWithLambdaSubscription = "QueueNameWithLambdaSubscription";

        public static readonly AwsEventBusQueueOptions DefaultAwsEventBusQueueOptions = new()
        {
            MaxConcurrentCalls = 10,
            MaxRetryCount = 5,
            MessageDelayInSeconds = 60,
            MessageRetentionPeriodInSeconds = 1_209_600,
            RetryDelaysSeconds = [30, 60, 90, 120, 180],
            DlqSetting = new AwsEventBusQueueOptions
            {
                MaxConcurrentCalls = 10,
                MaxRetryCount = 5,
                MessageDelayInSeconds = 60,
                MessageRetentionPeriodInSeconds = 1_209_600,
                RetryDelaysSeconds = [30, 60, 90, 120, 180]
            }
        };
    }

}
