using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utils.Common.Crypto
{
    public interface ISecretRepository : IDisposable
    {
        #region Secrets

        Task<string> UpsertSecretAsync(string name, string value,
            CancellationToken cancellationToken = default);

        Task<string> GetSecretValueAsync(string name,
            CancellationToken cancellationToken = default);

        Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default);

        #endregion

        #region Keys

        Task<string> WrapKeyAsync(byte[] key, string keyName,
            CancellationToken cancellationToken = default);

        Task<byte[]> UnWrapKeyAsync(string base64EncodedWrappedKey, string keyName,
            CancellationToken cancellationToken = default);

        #endregion
    }
}
