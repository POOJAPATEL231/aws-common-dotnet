using Domain.Common.Settings;
using Serilog.Sinks.AwsCloudWatch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS
{
    public class AwsInfraSettings
    {
        public TimeSpan ConfigurationReloadTimeSpan { get; init; }

        public string EventDispatcherFunctionName { get; init; } = "QueueEventDispatcher";

        public CloudWatchOptions CloudWatchSettings { get; init; } = new();

        public CognitoAuthSettings CognitoAuthSettings { get; init; } = new();

        public Dictionary<string, AwsEventBusQueueOptions> Queues { get; init; } = new();
        public int DefaultVisibilityTimeoutInSeconds { get; internal set; }
    }

    public class CloudWatchOptions
    {
        public LogGroupRetentionPolicy LogGroupRetentionPolicy { get; init; }
    }
}
