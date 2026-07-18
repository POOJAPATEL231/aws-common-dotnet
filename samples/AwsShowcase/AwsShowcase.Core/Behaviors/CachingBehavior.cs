using Application.Common;
using AwsShowcase.Core.Caching;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AwsShowcase.Core.Behaviors;

/// <summary>
/// Read-through cache for queries: requests implementing <see cref="ICacheableQuery"/>
/// are answered from <see cref="ICache"/> when possible; on a miss the handler runs
/// and its result is cached with the query's expiration.
/// </summary>
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(ICache cache, ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICacheableQuery cacheable)
        {
            return await next();
        }

        var cached = await _cache.GetAsync<TResponse>(cacheable.CacheKey);
        if (cached is not null)
        {
            _logger.LogDebug("Cache HIT {CacheKey} for {Request}", cacheable.CacheKey, typeof(TRequest).Name);
            return cached;
        }

        _logger.LogDebug("Cache MISS {CacheKey} for {Request}", cacheable.CacheKey, typeof(TRequest).Name);
        var response = await next();

        if (response is not null)
        {
            await _cache.SetAsync(cacheable.CacheKey, response, cacheable.Expiration);
        }

        return response;
    }
}
