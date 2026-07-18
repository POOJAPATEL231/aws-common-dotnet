using Utils.Common.Crypto;
using Utils.Common.Utils;
using Xunit;

namespace Common.Tests
{
    public class ByteArrayUtilTests
    {
        [Fact]
        public void String_RoundTrips()
        {
            var bytes = "hello world".ToByteArray();
            Assert.Equal("hello world", bytes.FromByteArray<string>());
        }

        [Fact]
        public void ByteArray_PassesThrough()
        {
            var input = new byte[] { 1, 2, 3 };
            Assert.Same(input, input.ToByteArray());
            Assert.Equal(input, input.FromByteArray<byte[]>());
        }

        [Fact]
        public void Poco_RoundTrips()
        {
            var address = new Address { Street = "1 Main St", City = "Pune", Zip = "411001" };

            var restored = address.ToByteArray().FromByteArray<Address>();

            Assert.NotNull(restored);
            Assert.Equal(address.Street, restored!.Street);
            Assert.Equal(address.City, restored.City);
        }

        [Fact]
        public void EmptyBytes_ReturnDefault()
        {
            Assert.Null(Array.Empty<byte>().FromByteArray<Address>());
        }
    }

    public class FileInformationTests
    {
        [Theory]
        [InlineData("application/json", ".json")]
        [InlineData("image/png", ".png")]
        [InlineData("text/plain; charset=utf-8", ".txt")]
        [InlineData("application/unknown-type", "")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void GetFileExtension_MapsContentTypes(string? contentType, string expected)
        {
            Assert.Equal(expected, FileInformation.GetFileExtension(contentType));
        }
    }

    public class CryptoProviderTests
    {
        [Fact]
        public void EncryptDecrypt_RoundTrips()
        {
            using var provider = new CryptoProvider(null); // generates a key

            var cipher = provider.EncryptString("sensitive value");
            var plain = provider.DecryptString(cipher!);

            Assert.Equal("sensitive value", plain);
        }

        [Fact]
        public void Decrypt_TooShortCipher_ThrowsArgumentException()
        {
            using var provider = new CryptoProvider(null);

            // Regression: previously threw an obscure error from Buffer.BlockCopy.
            var tooShort = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            Assert.Throws<ArgumentException>(() => provider.DecryptString(tooShort));
        }

        [Fact]
        public void PasswordHash_Validates()
        {
            using var provider = new CryptoProvider(null);

            var hash = provider.HashPassword("s3cret!", 10_000, out var salt);

            Assert.True(provider.ValidatePassword("s3cret!", hash, salt, 10_000));
            Assert.False(provider.ValidatePassword("wrong", hash, salt, 10_000));
        }
    }
}
