using System;

namespace Application.Common.Event
{
    /// <summary>
    /// Cache-serializable subscription entry used by <see cref="EventBusSubscriptionsManager"/>.
    /// Stores the handler type by assembly-qualified name so it can round-trip
    /// through a distributed cache (e.g. Redis) as JSON.
    /// </summary>
    public class Subscription
    {
        public bool IsDynamic { get; set; }
        public string HandlerType { get; set; } = string.Empty;

        public Subscription() { }

        private Subscription(bool isDynamic, string handlerType)
        {
            IsDynamic = isDynamic;
            HandlerType = handlerType;
        }

        public static Subscription Dynamic(Type handlerType) => new(true, handlerType.AssemblyQualifiedName!);
        public static Subscription Typed(Type handlerType) => new(false, handlerType.AssemblyQualifiedName!);

        /// <summary>Resolves the stored assembly-qualified name back to a <see cref="Type"/>, or null if it cannot be loaded.</summary>
        public Type? ResolveHandlerType() => Type.GetType(HandlerType);
    }
}
