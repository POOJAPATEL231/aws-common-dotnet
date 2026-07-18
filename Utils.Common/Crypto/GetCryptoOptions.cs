using Microsoft.Extensions.Options;
using Utils.Common.Settings;

namespace Utils.Common.Crypto
{
    public record GetCryptoOptions : CryptoOptions
    {
        public GetCryptoOptions(IOptionsMonitor<CryptoSettings> settingsAccessor, ISecretRepository secretRepository)
            : base(UnwrapKey(settingsAccessor, secretRepository))
        {
        }

        private static byte[] UnwrapKey(IOptionsMonitor<CryptoSettings> settingsAccessor, ISecretRepository secretRepository)
        {
            var settings = settingsAccessor.CurrentValue;

            // Validate BEFORE using the values - previously validation ran after the
            // base-constructor call had already consumed them.
            if (string.IsNullOrEmpty(settings.WrappedEncryptionKeyBase64Encoded))
            {
                throw new ArgumentException("CryptoSettings.WrappedEncryptionKeyBase64Encoded is null/empty", nameof(settingsAccessor));
            }

            if (string.IsNullOrEmpty(settings.EncryptionWrapKeyName))
            {
                throw new ArgumentException("CryptoSettings.EncryptionWrapKeyName is null/empty", nameof(settingsAccessor));
            }

            // NOTE: sync-over-async is unavoidable here because options are resolved in DI
            // constructors; this runs once at startup. Do not call on a request path.
            return secretRepository.UnWrapKeyAsync(settings.WrappedEncryptionKeyBase64Encoded,
                settings.EncryptionWrapKeyName).GetAwaiter().GetResult() ?? Array.Empty<byte>();
        }
    }
}
