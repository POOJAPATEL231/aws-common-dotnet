namespace Application.Common
{
    public interface ICache : IAsyncDisposable, IDisposable
    {
        Task<T?> GetAsync<T>(string key);

        Task SetAsync<T>(string key, T item);

        Task SetAsync<T>(string key, T item, TimeSpan absoluteExpirationRelativeToNow);

        Task SetAsync<T>(string key, T item, DateTimeOffset absoluteExpiration);

        Task SetSlidingAsync<T>(string key, T item, TimeSpan slidingExpiration);

        Task RemoveAsync(string key);

        Task RemoveAllAsync(string keyMatchPattern);
    }
}
