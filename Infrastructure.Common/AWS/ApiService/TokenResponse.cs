using System.Text.Json.Serialization;

namespace Infrastructure.Common.AWS.ApiService
{
    public record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string Token { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; init; } = string.Empty;

        [JsonPropertyName("error")]
        public string? ErrorMessage { get; init; }

        public bool IsSuccess { get; private set; }

        public void SetValues(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }
    }
}
