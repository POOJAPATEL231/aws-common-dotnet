using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS
{
    public record AwsEventBusQueueOptions
    {
        public string QueueName { get; init; } = string.Empty;

        public string LambdaFunctionName { get; init; } = string.Empty;

        public int MaxConcurrentCalls { get; init; } = 10;

        public int MessageRetentionPeriodInSeconds { get; init; } = 1_209_600;

        public List<int> RetryDelaysSeconds { get; init; } = new();

        public int MaxRetryCount { get; init; }

        public bool IsDisabled { get; init; }

        public int? MessageDelayInSeconds { get; init; }

        /// <summary>
        /// SQS visibility timeout for this queue. Falls back to
        /// <see cref="AwsInfraSettings.DefaultVisibilityTimeoutInSeconds"/> when not set.
        /// </summary>
        public int? VisibilityTimeoutInSeconds { get; init; }

        public AwsEventBusQueueOptions? DlqSetting { get; init; }
    }

}
