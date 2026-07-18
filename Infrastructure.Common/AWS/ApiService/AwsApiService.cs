using Application.Common.Identity;
using Application.Common.Settings;
using Domain.Common.Converter;
using Domain.Common.Settings;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Utils.Common.Utils;

namespace Infrastructure.Common.AWS.ApiService
{
    public class AwsApiService : IApiService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly IHttpContextAccessor _context;
        private readonly CognitoAuthSettings _settings;
        private readonly AppSettings _appSettings;
        private readonly ICognitoHttpClient _cognitoHttpClient;
        private bool _isDevTokenRequest;
        private readonly ILogger<AwsApiService> _logger;
        private readonly IHostEnvironment _hostEnvironment;

        public AwsApiService(IHttpClientFactory clientFactory, IHttpContextAccessor context,
            IOptions<CognitoAuthSettings> settingsOptions, IOptions<AppSettings> appSettingsOptions, ICognitoHttpClient cognitoHttpClient, ILogger<AwsApiService> logger
            , IHostEnvironment hostEnvironment)
        {
            _clientFactory = clientFactory;
            _context = context;
            _settings = settingsOptions.Value;
            _appSettings = appSettingsOptions.Value;
            _cognitoHttpClient = cognitoHttpClient;
            _logger = logger;
            _hostEnvironment = hostEnvironment;
        }

        public async Task<TResponse?> GetAsync<TResponse>(string serviceType, string url, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }
            await ValidateResponseAsync(response, cancellationToken);
            return await ReadContentAsync<TResponse>(response, cancellationToken);
        }

        public async Task<TResponse?> GetAsync<TResponse>(string serviceType, string url, Dictionary<string, object?> queryParameters, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var parameters = queryParameters.ToDictionary(p => p.Key, p => p.Value?.ToString());
            url = QueryHelpers.AddQueryString(url, parameters);
            var response = await client.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }
            await ValidateResponseAsync(response, cancellationToken);
            return await ReadContentAsync<TResponse>(response, cancellationToken);
        }

        public async Task PostAsync<TRequest>(
            string serviceType, string url, TRequest request, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.PostAsJsonAsync(url, request, cancellationToken);
            await ValidateResponseAsync(response, cancellationToken);
        }

        public async Task<TResponse?> PostAsync<TRequest, TResponse>(
            string serviceType, string url, TRequest request, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.PostAsJsonAsync(url, request, cancellationToken);
            await ValidateResponseAsync(response, cancellationToken);
            return await ReadContentAsync<TResponse>(response, cancellationToken);
        }

        public async Task<TResponse?> PostContentAsync<TResponse>(
          string serviceType, string url, MultipartFormDataContent content, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.PostAsync(url, content, cancellationToken);
            await ValidateResponseAsync(response, cancellationToken);
            return await ReadContentAsync<TResponse>(response, cancellationToken);
        }

        public async Task PutAsync<TRequest>(
            string serviceType, string url, TRequest request, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.PutAsJsonAsync(url, request, cancellationToken);
            await ValidateResponseAsync(response, cancellationToken);
        }

        public async Task<TResponse?> PutAsync<TRequest, TResponse>(
            string serviceType, string url, TRequest request, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.PutAsJsonAsync(url, request, cancellationToken);
            await ValidateResponseAsync(response, cancellationToken);
            return await ReadContentAsync<TResponse>(response, cancellationToken);
        }

        public async Task DeleteAsync(string serviceType, string url, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.DeleteAsync(url, cancellationToken);
            await ValidateResponseAsync(response, cancellationToken);
        }

        public async Task<TResponse?> DeleteAsync<TResponse>(
            string serviceType, string url, CancellationToken cancellationToken = default)
        {
            var client = await GetHttpClientAsync(serviceType, cancellationToken);
            var response = await client.DeleteAsync(url, cancellationToken);
            await ValidateResponseAsync(response, cancellationToken);
            return await ReadContentAsync<TResponse>(response, cancellationToken);
        }

        private async Task<HttpClient> GetHttpClientAsync(string serviceType, CancellationToken cancellationToken)
        {
            var client = _clientFactory.CreateClient(serviceType);
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            // forward auth header
            client.DefaultRequestHeaders.Authorization = _context.HttpContext?.GetAuthenticationHeaderValue();

            // if app using client credentials, acquire auth token
            if (_appSettings.AuthSettings.Type == ApiAuthType.ClientCredentials)
            {
                if (_settings is not null && !string.IsNullOrEmpty(_settings.ClientId) && !string.IsNullOrEmpty(_settings.ClientSecret)
                     && !string.IsNullOrEmpty(_settings.Scope))
                {
                    var result = await _cognitoHttpClient.GetTokenAsync(_settings.ClientId, _settings.ClientSecret, _settings.Scope, cancellationToken);
                    if (result is null || string.IsNullOrEmpty(result.Token))
                    {
                        throw new Exception($"Error occurred in while generating token {result?.ErrorMessage}.");
                    }
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
                }
                else if (!_isDevTokenRequest)
                {
                    // get dev token
                    var command = new ValidateDevUserCommand(_appSettings.AuthSettings.BasicAuthUserName ?? "",
                        _appSettings.AuthSettings.BasicAuthPassword ?? "");
                    _isDevTokenRequest = true;
                    try
                    {
                        var response = await PostAsync<ValidateDevUserCommand, ValidateDevUserResponse>(
                            ApiServiceTypes.Identity, "devtokens", command, cancellationToken);
                        if (response is null || string.IsNullOrEmpty(response.Token))
                        {
                            throw new InvalidOperationException("Error occurred while generating dev token: empty response.");
                        }
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.Token);
                    }
                    finally
                    {
                        _isDevTokenRequest = false;
                    }
                }
            }

            // forward API key
            var apiKey = _context.HttpContext?.GetApiKey() ?? _appSettings.AuthSettings.ApiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeaderName, apiKey);
            }

            // forward client ip address
            var clientIp = _context.HttpContext?.GetRequestIp();
            if (!string.IsNullOrEmpty(clientIp))
            {
                client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
            }

            // forward session-id
            var sessionId = _context.HttpContext?.GetHeaderValueAs<string>("Session-Id");
            if (sessionId is not null)
            {
                client.DefaultRequestHeaders.Add("Session-Id", sessionId);
            }

            // adding User-Agent header for AWS
            client.DefaultRequestHeaders.UserAgent.ParseAdd(_hostEnvironment.ApplicationName);

            // adding this just for testing purpose will remove it later.
            _logger.LogInformation("Default headers : {DefaultRequestHeaders} ", client.DefaultRequestHeaders.Serialize());
            return client;
        }

        private async Task ValidateResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            // adding this just for testing purpose will remove it later.
            _logger.LogInformation("Response headers : {Response}", response.Headers.Serialize());
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Response Content : {Content}", responseBody);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var problemDetails = await response.Content
                    .ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken: cancellationToken);
                if (problemDetails is not null)
                {
                    var errors = new List<ValidationFailure>();
                    foreach (var error in problemDetails.Errors)
                    {
                        errors.AddRange(error.Value.Select(message => new ValidationFailure(error.Key, message) { ErrorCode = "ApiError" }));
                    }
                    throw new ValidationException(errors);
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
            }

            response.EnsureSuccessStatusCode();
        }

        private static async Task<T?> ReadContentAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            return typeof(T) == typeof(string) ?
                (T)(object)await response.Content.ReadAsStringAsync(cancellationToken)
                : await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
    }
}
