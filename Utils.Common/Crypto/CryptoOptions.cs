using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utils.Common.Crypto
{
    public record CryptoOptions(byte[] Key, int KeySize = 256);
}
