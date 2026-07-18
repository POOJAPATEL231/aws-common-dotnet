using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common
{
    public static class Constant
    {
        public static readonly string HandlersKeyPrefix = "EventBus:IntegrationEventHandlers:";

        public static readonly string EventTypesKey = "EventBus:IntegrationEventTypes";

        public static readonly string QueueNameWithLambdaSubscription = "QueueNameWithLambdaSubscription";
    }
}
