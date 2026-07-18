namespace AwsShowcase.Core.Caching;

/// <summary>
/// Marks a query as cacheable: the <see cref="Behaviors.CachingBehavior{TRequest,TResponse}"/>
/// checks the cache before invoking the handler and stores the result afterwards.
/// </summary>
public interface ICacheableQuery
{
    string CacheKey { get; }

    /// <summary>How long the cached value stays valid.</summary>
    TimeSpan Expiration { get; }
}

/// <summary>
/// Marks a command as cache-invalidating: after the handler succeeds, the
/// <see cref="Behaviors.CacheInvalidationBehavior{TRequest,TResponse}"/> removes
/// the listed keys so subsequent reads see fresh data.
/// </summary>
public interface ICacheInvalidatingRequest
{
    IEnumerable<string> CacheKeysToInvalidate { get; }
}

/// <summary>
/// Marks a request for Polly retry with exponential backoff on transient failures
/// (applied by <see cref="Behaviors.ResilienceBehavior{TRequest,TResponse}"/>).
/// </summary>
public interface IRetryableRequest
{
}

/// <summary>Single place where cache keys are minted - no scattered magic strings.</summary>
public static class CacheKeys
{
    public static string OrderById(string id) => $"orders:id:{id}";

    public static string OrdersByCustomer(string email) => $"orders:customer:{email}";
}
