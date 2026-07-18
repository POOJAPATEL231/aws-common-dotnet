using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utils.Common.Crypto
{
    public interface ICryptoProvider : IDisposable
    {
        #region Encryption Functions

        byte[]? GenerateKey();

        string? GenerateKeyString();

        string? EncryptString(string text, byte[]? key = null);

        string? DecryptString(string cipherText, byte[]? key = null);

        #endregion

        #region Base64 Functions

        string Base64Encode(string text);

        string Base64Decode(string encodedText);

        #endregion

        #region Hashing Functions

        string? HashString(string text);

        #endregion

        #region Password Hashing Functions

        string GenerateRandomCode(int min, int max);

        string HashPassword(string password, int iterations, out string base64EncodedSalt);

        bool ValidatePassword(string password, string hashedPassword, string base64EncodedSalt, int iterations);

        string GenerateRandomPassword(short length, bool strong = false);

        #endregion
    }

}
