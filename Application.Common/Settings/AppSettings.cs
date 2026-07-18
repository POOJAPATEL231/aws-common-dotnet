using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Settings
{
    public record AppSettings
    {
        public string? AllowedCorsOrigins { get; init; }

        #region Auth Settings

        public AuthOptions AuthSettings { get; init; }

        #endregion

        #region EventLogSink Timer Settings

        public int? EventLogSinkTimerIntervalMS { get; init; }

        #endregion

        #region OpenApi Settings

        public OpenApiOptions OpenApiSettings { get; init; }

        #endregion

        #region Api Versioning Settings

        public ApiVersionOptions ApiVersionSettings { get; init; }

        #endregion

        #region Portal Urls

        public Dictionary<string, string> PortalUrls { get; set; } = new();

        #endregion

        public int RegexTimeoutSeconds { get; init; } = 3;

        public long? MaxRequestFileSizeInBytes { get; init; }

        public int AuditLogTTLDays { get; init; } = 30;
    }

    public enum ApiAuthType
    {
        JWT = 0,
        AzureAdB2C = 1,
        AzureAd = 2,
        ClientCredentials = 3
    }

    public readonly record struct AuthOptions(ApiAuthType Type, string Instance, string? TenantId, string? Domain,
        string? ClientId, string? SignUpSignInPolicyId, string? ResetPasswordPolicyId, string? EditProfilePolicyId,
        string? DevTokenSecretKey, string? DevTokenIssuer, string? BasicAuthUserName, string? BasicAuthPassword, string? ApiKey,
        ClientCredentials? ClientCredentials);

    public readonly record struct ClientCredentials(string? ClientId, string? ClientSecret, string? Scope, string? Authority);

    public readonly record struct OpenApiOptions(string Title, string Description, bool ShowSessionIdHeader);

    public readonly record struct ApiVersionOptions(List<ApiVersion> Versions, ApiVersion DefaultVersion);

    public readonly record struct ApiVersion(int MajorVersion = 1, int MinorVersion = 0);
}
