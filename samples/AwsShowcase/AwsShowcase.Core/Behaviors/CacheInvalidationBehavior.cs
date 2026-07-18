using Application.Common;
using AwsShowcase.Core.Caching;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AwsShowcase.Core.Behaviors;

/// <summary>
/// After a command implementing <see cref="ICacheInvalidatingRequest"/> succeeds,
/// removes its listed cache keys so stale reads cannot be served.
/// </summary>
public class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICache _cache;
    private readonly ILogger<CacheInvalidationBehavior<TRequest, TResponse>> _logger;

    public CacheInvalidationBehavior(ICache cache, ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        if (request is ICacheInvalidatingRequest invalidating)
        {
            foreach (var key in invalidating.CacheKeysToInvalidate)
            {
                await _cache.RemoveAsync(key);
                _logger.LogDebug("Cache invalidated {CacheKey} by {Request}", key, typeof(TRequest).Name);
            }
        }

        return response;
    }
}
