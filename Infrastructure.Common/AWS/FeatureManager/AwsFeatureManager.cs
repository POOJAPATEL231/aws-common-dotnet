using Amazon.SimpleSystemsManagement.Model;
using Amazon.SimpleSystemsManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.FeatureManager
{
    public class AwsFeatureManager : IFeatureManager
    {
        private readonly IAmazonSimpleSystemsManagement _ssmClient;
        private readonly string _parameterStorePrefix = "/Features/";
        private List<AppFeature>? _cachedData;

        public AwsFeatureManager(IAmazonSimpleSystemsManagement ssmClient)
        {
            _ssmClient = ssmClient;
        }

        public async IAsyncEnumerable<string> GetFeatureNamesAsync()
        {
            if (_cachedData is null)
            {
                await GetDataFromStoreAsync();
            }

            foreach (var flag in _cachedData!)
            {
                yield return flag.Name;
            }
        }

        public async Task<bool> IsEnabledAsync(string feature)
        {
            if (_cachedData is null)
            {
                await GetDataFromStoreAsync();
            }

            var isEnabled = _cachedData!.Where(x => x.Name == feature).Select(x => x.IsEnabled).FirstOrDefault();
            return isEnabled;
        }

        // Not required for current implementation.
        public async Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
        {
            if (_cachedData is null)
            {
                await GetDataFromStoreAsync();
            }

            var isEnabled = _cachedData!.Where(x => x.Name == feature).Select(x => x.IsEnabled).FirstOrDefault();
            return isEnabled;
        }

        private async Task GetDataFromStoreAsync()
        {
            var response = await _ssmClient.GetParametersByPathAsync(new GetParametersByPathRequest
            {
                Path = _parameterStorePrefix,
                Recursive = false,
                WithDecryption = true
            }, CancellationToken.None);

            _cachedData = response.Parameters.Select(x => new AppFeature(x.Name.Substring(_parameterStorePrefix.Length), Convert.ToBoolean(x.Value))).ToList();
        }
    }
}
