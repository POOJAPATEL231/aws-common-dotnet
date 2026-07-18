using System;

namespace Application.Common.Event
{
    /// <summary>
    /// In-memory subscription entry used by <see cref="InMemoryEventBusSubscriptionsManager"/>.
    /// Holds the live handler <see cref="Type"/>.
    /// </summary>
    public class SubscriptionInfo
    {
        public bool IsDynamic { get; }
        public Type HandlerType { get; }

        private SubscriptionInfo(bool isDynamic, Type handlerType)
        {
            IsDynamic = isDynamic;
            HandlerType = handlerType;
        }

        public static SubscriptionInfo Dynamic(Type handlerType) => new(true, handlerType);
        public static SubscriptionInfo Typed(Type handlerType) => new(false, handlerType);
    }
}
