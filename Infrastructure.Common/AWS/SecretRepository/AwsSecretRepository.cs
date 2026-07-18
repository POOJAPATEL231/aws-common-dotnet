using Amazon.KeyManagementService.Model;
using Amazon.KeyManagementService;
using Amazon.SecretsManager.Model;
using Amazon.SecretsManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Utils.Common.Crypto;

namespace Infrastructure.Common.AWS.SecretRepository
{
    public class AwsSecretRepository : ISecretRepository
    {
        private readonly string _wrapAlgorithm = Constants.WapAlgorithm;
        private readonly IAmazonSecretsManager _secretsManager;
        private readonly IAmazonKeyManagementService _keyManagementService;

        public AwsSecretRepository(IAmazonSecretsManager secretsManager, IAmazonKeyManagementService keyManagementService)
        {
            _secretsManager = secretsManager;
            _keyManagementService = keyManagementService;
        }

        public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default)
        {
            await _secretsManager.DeleteSecretAsync(new DeleteSecretRequest { SecretId = name }, cancellationToken);
        }

        public async Task<string> GetSecretValueAsync(string name, CancellationToken cancellationToken = default)
        {
            var response = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest { SecretId = name }, cancellationToken);
            return response.SecretString;
        }

        public async Task<string> UpsertSecretAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            var response = await _secretsManager.ListSecretsAsync(new ListSecretsRequest
            {
                Filters = new List<Filter>
                {
                    new Filter
                    {
                        Key = FilterNameStringType.Name,
                        Values = new List<string> { name }
                    }
                }
            }, cancellationToken);

            if (response?.SecretList.Exists(x => x.Name.ToLower() == name.ToLower()) == true)
            {
                var updateResponse = await _secretsManager.UpdateSecretAsync(new UpdateSecretRequest
                {
                    SecretId = name,
                    SecretString = value,
                }, cancellationToken);

                return updateResponse.Name;
            }

            var createResponse = await _secretsManager.CreateSecretAsync(new CreateSecretRequest
            {
                Name = name,
                SecretString = value,
            }, cancellationToken);

            return createResponse.Name;
        }

        public async Task<string> WrapKeyAsync(byte[] key, string keyName, CancellationToken cancellationToken = default)
        {
            var response = await _keyManagementService.EncryptAsync(new EncryptRequest
            {
                KeyId = keyName,
                EncryptionAlgorithm = _wrapAlgorithm,
                Plaintext = new MemoryStream(key)
            }, cancellationToken);

            // When you use the HTTP API or the Amazon Web Services CLI, the value is Base64-encoded. Otherwise, it is not Base64-encoded.
            return response.HttpStatusCode == HttpStatusCode.OK ? Convert.ToBase64String(response.CiphertextBlob.ToArray()) : string.Empty;
        }

        public async Task<byte[]> UnWrapKeyAsync(string base64EncodedWrappedKey, string keyName, CancellationToken cancellationToken = default)
        {
            var response = await _keyManagementService.DecryptAsync(new DecryptRequest
            {
                KeyId = keyName,
                EncryptionAlgorithm = _wrapAlgorithm,
                CiphertextBlob = new MemoryStream(Convert.FromBase64String(base64EncodedWrappedKey))
            }, cancellationToken);

            // When you use the HTTP API or the Amazon Web Services CLI, the value is Base64-encoded. Otherwise, it is not Base64-encoded.
            return response.Plaintext.ToArray();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // dispose resources
        }
    }
}
