using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Utils.Common.Utils;

namespace Utils.Common.Crypto
{
    public class CryptoProvider : ICryptoProvider
    {
        #region Private Properties

        private readonly CryptoOptions _options;

        #endregion

        #region Constructors

        public CryptoProvider(CryptoOptions? options)
        {
            _options = options ?? new CryptoOptions(GenerateKey());
        }

        #endregion

        #region IDisposable Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // dispose resources
        }

        #endregion

        #region ICryptoProvider Methods

        #region Encryption Functions

        public byte[] GenerateKey()
        {
            byte[]? key = null;
            using (var aesAlg = Aes.Create())
            {
                aesAlg.KeySize = _options?.KeySize ?? 256;
                aesAlg.GenerateKey();
                key = aesAlg.Key;
            }
            return key;
        }

        public string? GenerateKeyString()
        {
            return Convert.ToBase64String(GenerateKey());
        }

        public string? EncryptString(string text, byte[]? key = null)
        {
            using var aesAlg = Aes.Create();
            aesAlg.GenerateIV();
            var iv = aesAlg.IV;

#pragma warning disable CA5401 // Do not use CreateEncryptor with non-default IV
            using var encryptor = aesAlg.CreateEncryptor(key ?? _options.Key, iv);
#pragma warning restore CA5401 // Do not use CreateEncryptor with non-default IV
            using var msEncrypt = new MemoryStream();
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(text);
            }

            var encryptedContent = msEncrypt.ToArray();

            var result = new byte[iv.Length + encryptedContent.Length];

            Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
            Buffer.BlockCopy(encryptedContent, 0, result, iv.Length, encryptedContent.Length);

            return Convert.ToBase64String(result);
        }

        public string? DecryptString(string cipherText, byte[]? key = null)
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            // The payload must contain at least the 16-byte IV produced by EncryptString.
            if (fullCipher.Length < 16)
            {
                throw new ArgumentException("Cipher text is too short to contain a valid IV.", nameof(cipherText));
            }

            var iv = new byte[16];
            var cipher = new byte[fullCipher.Length - iv.Length];

            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, fullCipher.Length - iv.Length);

            using var aesAlg = Aes.Create();
            using var decryptor = aesAlg.CreateDecryptor(key ?? _options.Key, iv);
            string result;
            using (var msDecrypt = new MemoryStream(cipher))
            {
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);
                result = srDecrypt.ReadToEnd();
            }

            return result;
        }

        #endregion

        #region Hashing Functions

        public string? HashString(string text)
        {
            string? hashText = null;
            if (!string.IsNullOrEmpty(text))
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                if (hash != null)
                {
                    hashText = Convert.ToBase64String(hash);
                }
            }
            return hashText;
        }

        #endregion

        #region Base64 Functions

        public string Base64Encode(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        public string Base64Decode(string encodedText)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encodedText));
        }

        #endregion

        #region Password Hashing Functions

        public string GenerateRandomCode(int min, int max)
        {
            return NumberUtil.GenerateRandomNumber(min, max).ToString();
        }

        ///<Summary>
        /// Returns a base64 encoded hashed version of an input plain text password
        ///</Summary>
        public string HashPassword(string password, int iterations, out string base64EncodedSalt)
        {
            byte[] hash = GeneratePasswordHash(password, iterations, 16, 20, out byte[] salt);
            base64EncodedSalt = Convert.ToBase64String(salt);
            return Convert.ToBase64String(hash);
        }

        ///<Summary>
        /// Validates a password by comparing the hashed and non-hashed versions
        ///</Summary>
        public bool ValidatePassword(string password, string hashedPassword, string base64EncodedSalt, int iterations)
        {
            byte[] hash = GeneratePasswordHash(password, Convert.FromBase64String(base64EncodedSalt), iterations, 20);
            return string.Equals(Convert.ToBase64String(hash), hashedPassword, StringComparison.Ordinal);
        }

        /// <summary>
        /// Generates a random password
        /// </summary>
        /// <param name="length">minimum length</param>
        /// <param name="strong">generate a strong password</param>
        /// <returns></returns>
        public string GenerateRandomPassword(short length, bool strong = false)
        {
            const string allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
            const string specialChars = @"!#$%&'()*+,-./:;<=>?@[\]_";

            var chars = new char[length];

            for (var i = 0; i < length; i++)
            {
                // If we are to use special characters
                chars[i] = (strong && i % NumberUtil.GenerateRandomNumber(3, length) == 0) ?
                    specialChars[RandomNumberGenerator.GetInt32(0, specialChars.Length)]
                    : allowedChars[RandomNumberGenerator.GetInt32(0, allowedChars.Length)];
            }

            return new string(chars);
        }

        #endregion

        #endregion

        #region Private Methods

        ///<Summary>
        /// Generates a hash for a given text
        ///</Summary>
        private static byte[] GeneratePasswordHash(string text, int iterations, int saltLength, int outputLength, out byte[] salt)
        {
            using var deriveBytes = new Rfc2898DeriveBytes(text, saltLength, iterations, HashAlgorithmName.SHA256);
            salt = deriveBytes.Salt;
            return deriveBytes.GetBytes(outputLength);
        }

        ///<Summary>
        /// Generates a hash for a given text
        ///</Summary>
        private static byte[] GeneratePasswordHash(string text, byte[] salt, int iterations, int outputLength)
        {
            using var deriveBytes = new Rfc2898DeriveBytes(text, salt, iterations, HashAlgorithmName.SHA256);
            return deriveBytes.GetBytes(outputLength);
        }

        #endregion

    }
}
