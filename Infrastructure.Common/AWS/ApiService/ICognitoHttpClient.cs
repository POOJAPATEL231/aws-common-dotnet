using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.ApiService
{
    public interface ICognitoHttpClient
    {
        Task<TokenResponse?> GetTokenAsync(string clientId, string clientSecret, string scope, CancellationToken cancellationToken);
    }
}
