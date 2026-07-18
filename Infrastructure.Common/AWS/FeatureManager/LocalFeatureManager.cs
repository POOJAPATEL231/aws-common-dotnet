using Microsoft.Extensions.Configuration;

namespace Infrastructure.Common.AWS.FeatureManager
{
    public class LocalFeatureManager : IFeatureManager
    {
        private readonly IConfiguration _configuration;
        private Dictionary<string, bool>? _cachedData;

        public LocalFeatureManager(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async IAsyncEnumerable<string> GetFeatureNamesAsync()
        {
            if (_cachedData is null)
            {
                await LoadFeaturesFromConfigurationAsync();
            }

            foreach (var flag in _cachedData!.Keys)
            {
                yield return flag;
            }
        }

        public async Task<bool> IsEnabledAsync(string feature)
        {
            if (_cachedData is null)
            {
                await LoadFeaturesFromConfigurationAsync();
            }

            return _cachedData!.TryGetValue(feature, out var isEnabled) && isEnabled;
        }

        public async Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
        {
            // No context-based evaluation for local features
            return await IsEnabledAsync(feature);
        }

        private async Task LoadFeaturesFromConfigurationAsync()
        {
            // Simulate asynchronous behavior
            await Task.Delay(10);

            var featureSection = _configuration.GetSection("Features");
            _cachedData = featureSection.Exists() ?
                featureSection.GetChildren()
                    .ToDictionary(x => x.Key, x => bool.Parse(x.Value ?? "false"))
                : new Dictionary<string, bool>();
        }
    }
}
