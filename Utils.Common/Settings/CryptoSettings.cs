using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utils.Common.Settings
{
    public record CryptoSettings
    {
        #region Crypto Settings

        public string WrappedEncryptionKeyBase64Encoded { get; init; } = "";

        public string EncryptionWrapKeyName { get; init; } = "";

        #endregion

        #region Azure Key Vault Settings

        public string AzureKeyVaultUri { get; init; } = "";

        #endregion

    }
}
