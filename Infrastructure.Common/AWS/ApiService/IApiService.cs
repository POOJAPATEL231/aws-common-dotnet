using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.ApiService
{
    public interface IApiService
    {
        Task<TResponse?> GetAsync<TResponse>(string serviceType, string url, CancellationToken cancellationToken = default);

        Task<TResponse?> GetAsync<TResponse>(string serviceType, string url, Dictionary<string, object?> queryParameters, CancellationToken cancellationToken = default);

        Task PostAsync<TRequest>(string serviceType, string url, TRequest request, CancellationToken cancellationToken = default);

        Task<TResponse?> PostAsync<TRequest, TResponse>(string serviceType, string url, TRequest request, CancellationToken cancellationToken = default);

        Task PutAsync<TRequest>(string serviceType, string url, TRequest request, CancellationToken cancellationToken = default);

        Task<TResponse?> PutAsync<TRequest, TResponse>(string serviceType, string url, TRequest request, CancellationToken cancellationToken = default);

        Task<TResponse?> DeleteAsync<TResponse>(string serviceType, string url, CancellationToken cancellationToken = default);

        Task DeleteAsync(string serviceType, string url, CancellationToken cancellationToken = default);

        Task<TResponse?> PostContentAsync<TResponse>(string serviceType, string url, MultipartFormDataContent content, CancellationToken cancellationToken = default);
    }
}
