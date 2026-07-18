using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common.Settings
{
    public record CognitoAuthSettings
    {
        public string BaseUrl { get; init; } = string.Empty;

        public string Authority { get; init; } = string.Empty;

        public string ClientId { get; init; } = string.Empty;

        public string ClientSecret { get; init; } = string.Empty;

        public string Scope { get; init; } = string.Empty;

        public int TimeoutSeconds { get; init; } = 90;
    }
}
