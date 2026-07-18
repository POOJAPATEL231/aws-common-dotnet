using Application.Common;
using Microsoft.Extensions.Logging;
using Polly;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Utils.Common.Utils;

namespace Infrastructure.Common.Cache
{
    public sealed class RedisCache : ICache
    {
        private static RedisCacheOptions Options { get; set; } = new();

        private static ILogger<RedisCache>? _logger;

        private static long _lastReconnectTicks = DateTimeOffset.MinValue.UtcTicks;
        private static DateTimeOffset _firstErrorTime = DateTimeOffset.MinValue;
        private static DateTimeOffset _previousErrorTime = DateTimeOffset.MinValue;

        private static readonly SemaphoreSlim _reconnectSemaphore = new(initialCount: 1, maxCount: 1);
        private static readonly SemaphoreSlim _initSemaphore = new(initialCount: 1, maxCount: 1);

        private static ConnectionMultiplexer? _connection;

        private static ConnectionMultiplexer? Connection { get { return _connection; } }

        #region Constructors

        public RedisCache(RedisCacheOptions options, ILogger<RedisCache> logger)
        {
            Options = options;
#pragma warning disable S3010 // Remove this assignment of '_logger' or initialize it statically.
            _logger = logger;
#pragma warning disable S3010 // Remove this assignment of '_logger' or initialize it statically.
        }

        #endregion

        #region IDisposable Methods

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        // https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(disposing: false);
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
            GC.SuppressFinalize(this);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
        }

        private static void Dispose(bool disposing)
        {
            if (disposing)
            {
                // dispose resources
                _connection?.Dispose();
            }
        }

        private static async ValueTask DisposeAsyncCore()
        {
            // dispose resources
            ConnectionMultiplexer? oldConnection = _connection;
            if (oldConnection is not null)
            {
                await CloseConnectionAsync(oldConnection);
            }
            _connection = null;
        }

        #endregion

        #region Private Methods

        private static async Task InitializeAsync()
        {
            // CreateConnectionAsync is idempotent (re-uses an existing connection and
            // serializes creation behind a semaphore), so it is safe to call this
            // whenever the connection is found to be null - including after disposal.
            _connection = await CreateConnectionAsync();
        }

        // This method may return null if it fails to acquire the semaphore in time.
        // Use the return value to update the "connection" field
        private static async Task<ConnectionMultiplexer?> CreateConnectionAsync()
        {
            if (_connection != null)
            {
                // If we already have a good connection, let's re-use it
                return _connection;
            }

            try
            {
                await _initSemaphore.WaitAsync(TimeSpan.FromSeconds(Options.RestartConnectionTimeoutSeconds));
            }
            catch (Exception ex)
            {
                // We failed to enter the semaphore in the given amount of time. Connection will either be null, or have a value that was created by another thread.
                _logger?.LogError(ex, "We failed to enter the semaphore in the given amount of time. Connection will either be null, or have a value that was created by another thread.");
                return _connection;
            }

            // We entered the semaphore successfully.
            try
            {
                // Otherwise, we really need to create a new connection.
                return await ConnectionMultiplexer.ConnectAsync(Options.ConnectionString);
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        private static async Task CloseConnectionAsync(ConnectionMultiplexer oldConnection)
        {
            if (oldConnection == null)
            {
                return;
            }
            try
            {
                await oldConnection.CloseAsync();
            }
            catch (Exception ex)
            {
                // Ignore any errors from the oldConnection
                _logger?.LogError(ex, "Ignore any errors from the oldConnection.");
            }
        }

        /// <summary>
        /// Force a new ConnectionMultiplexer to be created.
        /// NOTES:
        ///     1. Users of the ConnectionMultiplexer MUST handle ObjectDisposedExceptions, which can now happen as a result of calling ForceReconnectAsync().
        ///     2. Call ForceReconnectAsync() for RedisConnectionExceptions and RedisSocketExceptions. You can also call it for RedisTimeoutExceptions,
        ///         but only if you're using generous ReconnectMinInterval and ReconnectErrorThreshold. Otherwise, establishing new connections can cause
        ///         a cascade failure on a server that's timing out because it's already overloaded.
        ///     3. The code will:
        ///         a. wait to reconnect for at least the "ReconnectErrorThreshold" time of repeated errors before actually reconnecting
        ///         b. not reconnect more frequently than configured in "ReconnectMinInterval"
        /// </summary>
        private static async Task ForceReconnectAsync()
        {
            var utcNow = DateTimeOffset.UtcNow;
            long previousTicks = Interlocked.Read(ref _lastReconnectTicks);
            var previousReconnectTime = new DateTimeOffset(previousTicks, TimeSpan.Zero);
            TimeSpan elapsedSinceLastReconnect = utcNow - previousReconnectTime;

            // In general, let StackExchange.Redis handle most reconnects,
            // so limit the frequency of how often ForceReconnect() will
            // actually reconnect.

            // If multiple threads call ForceReconnectAsync at the same time, we only want to honor one of them.
            if (elapsedSinceLastReconnect < TimeSpan.FromSeconds(Options.ReconnectMinIntervalSeconds))
            {
                return;
            }

            try
            {
                await _reconnectSemaphore.WaitAsync(TimeSpan.FromSeconds(Options.RestartConnectionTimeoutSeconds));
            }
            catch (Exception ex)
            {
                // If we fail to enter the semaphore, then it is possible that another thread has already done so.
                // ForceReconnectAsync() can be retried while connectivity problems persist.
                _logger?.LogError(ex, "If we fail to enter the semaphore, then it is possible that another thread has already done so.");
                return;
            }

            try
            {
                utcNow = DateTimeOffset.UtcNow;
                elapsedSinceLastReconnect = utcNow - previousReconnectTime;

                if (_firstErrorTime == DateTimeOffset.MinValue)
                {
                    // We haven't seen an error since last reconnect, so set initial values.
                    _firstErrorTime = utcNow;
                    _previousErrorTime = utcNow;
                    return;
                }

                if (elapsedSinceLastReconnect < TimeSpan.FromSeconds(Options.ReconnectMinIntervalSeconds))
                {
                    return; // Some other thread made it through the check and the lock, so nothing to do.
                }

                TimeSpan elapsedSinceFirstError = utcNow - _firstErrorTime;
                TimeSpan elapsedSinceMostRecentError = utcNow - _previousErrorTime;

                // If errors continue for longer than the below threshold, then the
                // multiplexer seems to not be reconnecting, so ForceReconnect() will
                // re-create the multiplexer.

                bool shouldReconnect =
                    elapsedSinceFirstError >= TimeSpan.FromSeconds(Options.ReconnectErrorThresholdSeconds) // Make sure we gave the multiplexer enough time to reconnect on its own if it could.
                    && elapsedSinceMostRecentError <= TimeSpan.FromSeconds(Options.ReconnectErrorThresholdSeconds); // Make sure we aren't working on stale data
                                                                                                                    // (e.g. if there was a gap in errors, don't reconnect yet).

                // Update the previousErrorTime timestamp to be now (e.g. this reconnect request).
                _previousErrorTime = utcNow;

                if (!shouldReconnect)
                {
                    return;
                }

                _firstErrorTime = DateTimeOffset.MinValue;
                _previousErrorTime = DateTimeOffset.MinValue;

                ConnectionMultiplexer? oldConnection = _connection;
                if (oldConnection is not null)
                {
                    await CloseConnectionAsync(oldConnection);
                }
                _connection = null;
                _connection = await CreateConnectionAsync();
                Interlocked.Exchange(ref _lastReconnectTicks, utcNow.UtcTicks);
            }
            finally
            {
                _reconnectSemaphore.Release();
            }
        }

        private static async Task<T> PollyRetryAsync<T>(Func<Task<T>> func)
        {
            var policy = Policy
                .Handle<RedisTimeoutException>()
                .RetryAsync(Options.RetryMaxAttempts,
                    onRetry: async (ex, retryCount) =>
                    {
                        _logger?.LogError(ex, "RedisCache.PollyRetryAsync() failure. Func: {Func}. RetryCount: {RetryCount}", nameof(func), retryCount);
                        if (ex is not ObjectDisposedException)
                        {
                            await ForceReconnectAsync();
                        }
                    });

            return await policy.ExecuteAsync<T>(() => func());
        }

        private static Task<IDatabase> GetDatabaseAsync()
        {
            var policy = Policy
                .Handle<RedisConnectionException>()
                .Or<SocketException>()
                .Or<ObjectDisposedException>()
                .RetryAsync(Options.RetryMaxAttempts,
                    onRetry: async (ex, retryCount) =>
                    {
                        _logger?.LogError(ex, "RedisCache.GetDatabaseAsync() failure. RetryCount: {RetryCount}", retryCount);
                        if (ex is not ObjectDisposedException)
                        {
                            await ForceReconnectAsync();
                        }
                    });

            return policy.ExecuteAsync<IDatabase>(() => Task.FromResult(Connection!.GetDatabase()));
        }

        private static async Task AddSlidingExpirationAsync(string key, TimeSpan slidingExpiration)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            await PollyRetryAsync(() => cache.StringSetAsync("sliding-ttl-" + key, slidingExpiration.TotalMilliseconds));
        }

        private static async Task DeleteSlidingExpirationAsync(string key)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            await PollyRetryAsync(() => cache.KeyDeleteAsync("sliding-ttl-" + key));
        }

        private static async Task<TimeSpan> GetSlidingExpirationAsync(string key)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            var skey = "sliding-ttl-" + key;
            double? ttlms = default;
            if (await PollyRetryAsync(() => cache.KeyExistsAsync(skey)))
            {
                ttlms = (double?)await PollyRetryAsync(() => cache.StringGetAsync(skey));
            }
            return ttlms.HasValue ? TimeSpan.FromMilliseconds(ttlms.Value)
                : default;
        }

        #endregion

        #region ICache Methods

        public async Task<T?> GetAsync<T>(string key)
        {
            T? item = default;

            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            if (await PollyRetryAsync(() => cache.KeyExistsAsync(key)))
            {
                var ttl = await GetSlidingExpirationAsync(key);
                var val = await PollyRetryAsync(() => cache.StringGetAsync(key));
                if (val.HasValue)
                {
                    _logger?.LogTrace("RedisCache.GetAsync() Hit Found: {Key}", key);
                    try
                    {
                        item = ((byte[])val!).FromByteArray<T>();
                        if (item is not null && ttl.TotalMilliseconds > 0)
                        {
                            await PollyRetryAsync(() => cache.KeyExpireAsync(key, ttl));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error retrieving {Key} from redis cache.", key);
                        // ignore, default value will be returned
                    }
                }
            }

            return item;
        }

        public async Task SetAsync<T>(string key, T item)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            await PollyRetryAsync(() => cache.StringSetAsync(key, item?.ToByteArray()));
        }

        public async Task SetAsync<T>(string key, T item, TimeSpan absoluteExpirationRelativeToNow)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            await PollyRetryAsync(() => cache.StringSetAsync(key, item?.ToByteArray(), absoluteExpirationRelativeToNow));
        }

        public async Task SetAsync<T>(string key, T item, DateTimeOffset absoluteExpiration)
        {
            var expiryTimeSpan = absoluteExpiration.UtcDateTime.Subtract(DateTime.UtcNow);
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            await PollyRetryAsync(() => cache.StringSetAsync(key, item?.ToByteArray(), expiryTimeSpan));
        }

        public async Task SetSlidingAsync<T>(string key, T item, TimeSpan slidingExpiration)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            await PollyRetryAsync(() => cache.StringSetAsync(key, item?.ToByteArray(), slidingExpiration));
            await AddSlidingExpirationAsync(key, slidingExpiration);
        }

        public async Task RemoveAsync(string key)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }
            IDatabase cache = await GetDatabaseAsync();
            await PollyRetryAsync(() => cache.KeyDeleteAsync(key));
            await DeleteSlidingExpirationAsync(key);
            _logger?.LogTrace("RedisCache.RemoveAsync(): {Key}", key);
        }

        public async Task RemoveAllAsync(string keyMatchPattern)
        {
            if (Connection == null)
            {
                await InitializeAsync();
            }

            if (Connection is not null)
            {
                var servers = Connection.GetServers();
                if (servers?.Length > 0)
                {
                    foreach (var server in servers)
                    {
                        await foreach (var key in server.KeysAsync(pattern: keyMatchPattern))
                        {
                            await RemoveAsync(key.ToString());
                        }
                    }
                }
            }
        }

        #endregion

    }
}
