using AwsShowcase.Core.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AwsShowcase.Core;

public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR with the pipeline: logging -> resilience (Polly retry) ->
    /// caching (read-through) -> cache invalidation -> handler. Order matters:
    /// retries wrap the cache so a transient miss can still be retried, and
    /// invalidation runs only after the handler succeeded.
    /// </summary>
    public static IServiceCollection AddShowcaseCore(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ResilienceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        return services;
    }
}
