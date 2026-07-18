using Application.Common;
using Microsoft.Extensions.Caching.Distributed;
using Utils.Common.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.Cache
{
    public sealed class Cache : ICache
    {
        private readonly IDistributedCache _cache;
        private readonly ConcurrentDictionary<string, bool> _keys = new();

        #region Constructors

        public Cache(IDistributedCache cache)
        {
            _cache = cache;
        }

        #endregion

        #region IDisposable Methods

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
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
            }
        }

        private static ValueTask DisposeAsyncCore()
        {
            // dispose resources
            return ValueTask.CompletedTask;
        }

        #endregion

        #region ICache Methods

        public async Task<T?> GetAsync<T>(string key)
        {
            T? item = default;
            var i = await _cache.GetAsync(key);
            if (i != null)
            {
                try
                {
                    item = i.FromByteArray<T>();
                }
                catch
                {
                    // ignore
                }
            }
            return item;
        }

        public async Task SetAsync<T>(string key, T item)
        {
            AddKey(key);
            await _cache.SetAsync(key, item?.ToByteArray() ?? Array.Empty<byte>());
        }

        public async Task SetAsync<T>(string key, T item, TimeSpan absoluteExpirationRelativeToNow)
        {
            AddKey(key);
            await _cache.SetAsync(key, item?.ToByteArray() ?? Array.Empty<byte>(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow });
        }

        public async Task SetAsync<T>(string key, T item, DateTimeOffset absoluteExpiration)
        {
            AddKey(key);
            await _cache.SetAsync(key, item?.ToByteArray() ?? Array.Empty<byte>(),
                new DistributedCacheEntryOptions { AbsoluteExpiration = absoluteExpiration });
        }

        public async Task SetSlidingAsync<T>(string key, T item, TimeSpan slidingExpiration)
        {
            AddKey(key);
            await _cache.SetAsync(key, item?.ToByteArray() ?? Array.Empty<byte>(),
                new DistributedCacheEntryOptions { SlidingExpiration = slidingExpiration });
        }

        public async Task RemoveAsync(string key)
        {
            RemoveKey(key);
            await _cache.RemoveAsync(key);
        }

        public async Task RemoveAllAsync(string keyMatchPattern)
        {
            foreach (var key in _keys.Keys)
            {
                if (key.Contains(keyMatchPattern, StringComparison.InvariantCultureIgnoreCase))
                {
                    await RemoveAsync(key);
                }
            }
        }

        #endregion

        #region Private Methods

        private void AddKey(string key)
        {
            _keys.TryAdd(key, true);
        }

        private void RemoveKey(string key)
        {
            _keys.TryRemove(key, out bool value);
        }

        #endregion

    }
}
