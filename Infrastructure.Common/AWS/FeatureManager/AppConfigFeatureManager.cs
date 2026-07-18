using Amazon.AppConfigData;
using Amazon.AppConfigData.Model;
using System.Text.Json;

namespace Infrastructure.Common.AWS.FeatureManager
{
    public record AppConfigOptions
    {
        public string ApplicationIdentifier { get; init; } = string.Empty;
        public string EnvironmentIdentifier { get; init; } = string.Empty;
        public string ConfigurationProfileIdentifier { get; init; } = string.Empty;

        /// <summary>Minimum seconds between polls to AppConfig (AppConfig enforces >= 15).</summary>
        public int PollIntervalSeconds { get; init; } = 60;
    }

    /// <summary>
    /// <see cref="IFeatureManager"/> backed by AWS AppConfig feature flags - supports
    /// deployment strategies, gradual rollouts and instant rollback, unlike raw SSM
    /// parameters. Expects the standard AppConfig feature-flag JSON shape:
    /// <c>{"my-flag":{"enabled":true}, ...}</c>.
    /// </summary>
    public class AppConfigFeatureManager : IFeatureManager
    {
        private readonly IAmazonAppConfigData _appConfigClient;
        private readonly AppConfigOptions _options;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private string? _configurationToken;
        private Dictionary<string, bool> _flags = new();
        private DateTimeOffset _lastPollUtc = DateTimeOffset.MinValue;

        public AppConfigFeatureManager(IAmazonAppConfigData appConfigClient, AppConfigOptions options)
        {
            _appConfigClient = appConfigClient;
            _options = options;
        }

        public async IAsyncEnumerable<string> GetFeatureNamesAsync()
        {
            await RefreshIfDueAsync();
            foreach (var name in _flags.Keys)
            {
                yield return name;
            }
        }

        public async Task<bool> IsEnabledAsync(string feature)
        {
            await RefreshIfDueAsync();
            return _flags.TryGetValue(feature, out var enabled) && enabled;
        }

        public Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
        {
            // Context-specific targeting is not supported by this implementation.
            return IsEnabledAsync(feature);
        }

        private async Task RefreshIfDueAsync()
        {
            if (DateTimeOffset.UtcNow - _lastPollUtc < TimeSpan.FromSeconds(_options.PollIntervalSeconds))
            {
                return;
            }

            await _refreshLock.WaitAsync();
            try
            {
                if (DateTimeOffset.UtcNow - _lastPollUtc < TimeSpan.FromSeconds(_options.PollIntervalSeconds))
                {
                    return; // another caller refreshed while we waited
                }

                if (_configurationToken is null)
                {
                    var session = await _appConfigClient.StartConfigurationSessionAsync(new StartConfigurationSessionRequest
                    {
                        ApplicationIdentifier = _options.ApplicationIdentifier,
                        EnvironmentIdentifier = _options.EnvironmentIdentifier,
                        ConfigurationProfileIdentifier = _options.ConfigurationProfileIdentifier,
                        RequiredMinimumPollIntervalInSeconds = Math.Max(15, _options.PollIntervalSeconds)
                    });
                    _configurationToken = session.InitialConfigurationToken;
                }

                var response = await _appConfigClient.GetLatestConfigurationAsync(new GetLatestConfigurationRequest
                {
                    ConfigurationToken = _configurationToken
                });
                _configurationToken = response.NextPollConfigurationToken;
                _lastPollUtc = DateTimeOffset.UtcNow;

                // An empty payload means "unchanged since last poll" - keep current flags.
                if (response.Configuration is { Length: > 0 })
                {
                    _flags = ParseFlags(response.Configuration);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private static Dictionary<string, bool> ParseFlags(Stream configuration)
        {
            using var json = JsonDocument.Parse(configuration);
            var flags = new Dictionary<string, bool>();

            foreach (var flag in json.RootElement.EnumerateObject())
            {
                if (flag.Value.ValueKind == JsonValueKind.Object &&
                    flag.Value.TryGetProperty("enabled", out var enabled))
                {
                    flags[flag.Name] = enabled.ValueKind == JsonValueKind.True;
                }
            }

            return flags;
        }
    }
}
