using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.ApiService
{
    public class CognitoHttpClient : ICognitoHttpClient
    {
        private readonly HttpClient _client;

        public CognitoHttpClient(HttpClient client)
        {
            _client = client;
        }

        public async Task<TokenResponse?> GetTokenAsync(string clientId, string clientSecret, string scope, CancellationToken cancellationToken)
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", GetBasicToken(clientId, clientSecret));
            var content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
            {
                new("grant_type", "client_credentials"),
                new("scope",scope),
            });
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var response = await _client.PostAsync(string.Empty, content, cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
            tokenResponse?.SetValues(response.IsSuccessStatusCode);
            return tokenResponse;
        }

        private static string GetBasicToken(string clientId, string clientSecret)
        {
            var tokenString = $"{clientId}:{clientSecret}";
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(tokenString));
        }
    }
}
