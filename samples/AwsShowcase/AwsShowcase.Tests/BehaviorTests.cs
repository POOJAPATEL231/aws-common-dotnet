using Application.Common;
using AwsShowcase.Core.Behaviors;
using AwsShowcase.Core.Caching;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Persistence.Common.AWS;
using Xunit;

namespace AwsShowcase.Tests;

// Test fixtures: a cacheable query, an invalidating command and a retryable command.
public record FakeCachedQuery(string Key) : IRequest<string>, ICacheableQuery
{
    public string CacheKey => Key;
    public TimeSpan Expiration => TimeSpan.FromMinutes(1);
}

public record FakeInvalidatingCommand(params string[] Keys) : IRequest<string>, ICacheInvalidatingRequest
{
    public IEnumerable<string> CacheKeysToInvalidate => Keys;
}

public record FakeRetryableCommand : IRequest<string>, IRetryableRequest;

public class CachingBehaviorTests
{
    [Fact]
    public async Task CacheHit_ReturnsCachedValue_WithoutInvokingHandler()
    {
        var cache = new Mock<ICache>();
        cache.Setup(c => c.GetAsync<string>("k1")).ReturnsAsync("cached-value");
        var behavior = new CachingBehavior<FakeCachedQuery, string>(cache.Object, NullLogger<CachingBehavior<FakeCachedQuery, string>>.Instance);

        var handlerInvoked = false;
        var result = await behavior.Handle(new FakeCachedQuery("k1"),
            () => { handlerInvoked = true; return Task.FromResult("fresh-value"); }, CancellationToken.None);

        Assert.Equal("cached-value", result);
        Assert.False(handlerInvoked); // read-through: handler must be skipped on a hit
    }

    [Fact]
    public async Task CacheMiss_InvokesHandler_AndStoresResult()
    {
        var cache = new Mock<ICache>();
        cache.Setup(c => c.GetAsync<string>("k1")).ReturnsAsync((string?)null);
        var behavior = new CachingBehavior<FakeCachedQuery, string>(cache.Object, NullLogger<CachingBehavior<FakeCachedQuery, string>>.Instance);

        var result = await behavior.Handle(new FakeCachedQuery("k1"),
            () => Task.FromResult("fresh-value"), CancellationToken.None);

        Assert.Equal("fresh-value", result);
        cache.Verify(c => c.SetAsync("k1", "fresh-value", TimeSpan.FromMinutes(1)), Times.Once);
    }

    [Fact]
    public async Task NonCacheableRequest_BypassesCacheEntirely()
    {
        var cache = new Mock<ICache>(MockBehavior.Strict); // any cache call would throw
        var behavior = new CachingBehavior<FakeRetryableCommand, string>(cache.Object, NullLogger<CachingBehavior<FakeRetryableCommand, string>>.Instance);

        var result = await behavior.Handle(new FakeRetryableCommand(), () => Task.FromResult("direct"), CancellationToken.None);

        Assert.Equal("direct", result);
    }
}

public class CacheInvalidationBehaviorTests
{
    [Fact]
    public async Task AfterHandlerSucceeds_ListedKeysAreRemoved()
    {
        var cache = new Mock<ICache>();
        var behavior = new CacheInvalidationBehavior<FakeInvalidatingCommand, string>(cache.Object, NullLogger<CacheInvalidationBehavior<FakeInvalidatingCommand, string>>.Instance);

        await behavior.Handle(new FakeInvalidatingCommand("orders:id:1", "orders:customer:a@b.c"),
            () => Task.FromResult("ok"), CancellationToken.None);

        cache.Verify(c => c.RemoveAsync("orders:id:1"), Times.Once);
        cache.Verify(c => c.RemoveAsync("orders:customer:a@b.c"), Times.Once);
    }

    [Fact]
    public async Task WhenHandlerThrows_NothingIsInvalidated()
    {
        var cache = new Mock<ICache>();
        var behavior = new CacheInvalidationBehavior<FakeInvalidatingCommand, string>(cache.Object, NullLogger<CacheInvalidationBehavior<FakeInvalidatingCommand, string>>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new FakeInvalidatingCommand("orders:id:1"),
                () => throw new InvalidOperationException("boom"), CancellationToken.None));

        cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
    }
}

public class ResilienceBehaviorTests
{
    [Fact]
    public async Task TransientFailures_AreRetried_UntilSuccess()
    {
        var behavior = new ResilienceBehavior<FakeRetryableCommand, string>(NullLogger<ResilienceBehavior<FakeRetryableCommand, string>>.Instance);

        var attempts = 0;
        var result = await behavior.Handle(new FakeRetryableCommand(), () =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new DynamoDbConcurrencyException("conflict - retry me");
            }
            return Task.FromResult("succeeded-after-retries");
        }, CancellationToken.None);

        Assert.Equal("succeeded-after-retries", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task NonTransientFailure_IsNotRetried()
    {
        var behavior = new ResilienceBehavior<FakeRetryableCommand, string>(NullLogger<ResilienceBehavior<FakeRetryableCommand, string>>.Instance);

        var attempts = 0;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            behavior.Handle(new FakeRetryableCommand(), () =>
            {
                attempts++;
                throw new ArgumentException("permanent");
            }, CancellationToken.None));

        Assert.Equal(1, attempts);
    }
}
