using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Utils.Common.Utils;
using Domain.Common.Converter;

namespace Application.Common.Identity
{
    public static class HttpContextExtensions
    {
        public static bool IsAuthenticated(this HttpContext context)
        {
            return context.User.Identity?.IsAuthenticated == true;
        }

        public static string? GetAuthProviderId(this HttpContext context)
        {
            string? id = null;
            if (context.IsAuthenticated())
            {
                id = context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            }
            return id;
        }

        public static string? GetFirstName(this HttpContext context)
        {
            string? name = null;
            if (context.IsAuthenticated())
            {
                name = context.User.FindFirst(ClaimTypes.GivenName)?.Value;
            }
            return name;
        }

        public static string? GetLastName(this HttpContext context)
        {
            string? name = null;
            if (context.IsAuthenticated())
            {
                name = context.User.FindFirst(ClaimTypes.Surname)?.Value;
            }
            return name;
        }

        public static string? GetFullName(this HttpContext context)
        {
            string? name = null;
            if (context.IsAuthenticated() && context.User.HasClaim(c => c.Type == ClaimTypes.GivenName))
            {
                name = string.Concat(context.User.FindFirst(ClaimTypes.GivenName)?.Value,
                    " ", context.User.FindFirst(ClaimTypes.Surname)?.Value);
            }
            return name;
        }

        public static string? GetEmail(this HttpContext context)
        {
            string? email = null;
            if (context.IsAuthenticated())
            {
                email = context.User.FindFirst(ClaimTypes.Email)?.Value ?? context.User.FindFirst("emails")?.Value;
            }
            return email;
        }

        public static string? GetMobilePhone(this HttpContext context)
        {
            string? phone = null;
            if (context.IsAuthenticated())
            {
                phone = context.User.FindFirst("extension_MobilePhone")?.Value;
            }
            return phone;
        }

        public static int? GetUserId(this HttpContext context)
        {
            int? id = default;
            if (context.IsAuthenticated())
            {
                id = context.User.FindFirst("extension_UserId")?.Value?.ToNullableInt();
            }
            return id;
        }

        public static AuthenticationHeaderValue? GetAuthenticationHeaderValue(this HttpContext context)
        {
            if (context.IsAuthenticated())
            {
                var value = context.GetHeaderValueAs<string>("Authorization");
                if (value?.Contains(' ') == true)
                {
                    var parts = value.Split(' ');
                    return new AuthenticationHeaderValue(parts[0], parts[1]);
                }
            }
            return null;
        }

        public static bool IsApiKeyAuth(this HttpContext context)
        {
            return !string.IsNullOrEmpty(context.GetHeaderValueAs<string>(AuthConstants.ApiKeyHeaderName))
                && context.GetUserId() is null;
        }

        public static string? GetApiKey(this HttpContext context)
        {
            if (context.IsAuthenticated())
            {
                return context.GetHeaderValueAs<string>(AuthConstants.ApiKeyHeaderName);
            }
            return null;
        }

        public static bool GetIsDisabled(this HttpContext context)
        {
            bool val = false;
            if (context.IsAuthenticated())
            {
                val = context.User.FindFirst("extension_IsDisabled")?.Value.ToBool() ?? false;
            }
            return val;
        }

        public static bool GetIsMultipleOrgs(this HttpContext context)
        {
            bool val = false;
            if (context.IsAuthenticated())
            {
                val = context.User.FindFirst("extension_IsMultipleOrgs")?.Value.ToBool() ?? false;
            }
            return val;
        }

        public static string? GetRequestIp(this HttpContext context, bool tryUseXForwardHeader = true)
        {
            string? ip = null;

            // need to support new "Forwarded" header (2014) https://en.wikipedia.org/wiki/X-Forwarded-For

            // X-Forwarded-For (csv list):  Using the First entry in the list seems to work
            // for 99% of cases however it has been suggested that a better (although tedious)
            // approach might be to read each IP from right to left and use the first public IP.
            // http://stackoverflow.com/a/43554000/538763
            //
            if (tryUseXForwardHeader)
            {
                ip = context.GetHeaderValueAs<string>("X-Forwarded-For")?.SplitCsv()?.FirstOrDefault();
            }

            // RemoteIpAddress is always null in DNX RC1 Update1 (bug).
            if (ip.IsNullOrWhitespace())
            {
                ip = context.Connection.RemoteIpAddress?.ToString();
            }

            if (ip.IsNullOrWhitespace())
            {
                ip = context.GetHeaderValueAs<string>("REMOTE_ADDR");
            }

            // _httpContextAccessor.HttpContext.Request?.Host this is the local host.

            if (ip.IsNullOrWhitespace())
            {
                throw new Exception("Unable to determine caller's IP.");
            }

            // check for existence of port in ip address
            if (ip != "::1" && !ip.IsIPV6() && ip!.Contains(':'))
            {
                var i = ip.LastIndexOf(':');
                ip = ip[..i];
            }

            if (ip.IsNullOrWhitespace())
            {
                ip = "unknown";
            }

            return ip;
        }

        private static bool IsIPV6(this string? s)
        {
            _ = IPAddress.TryParse(s, out IPAddress? ip);
            return ip?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        }

        public static T? GetHeaderValueAs<T>(this HttpContext context, string headerName)
        {
            if (context.Request.Headers.TryGetValue(headerName, out StringValues values))
            {
                string rawValues = values.ToString(); // writes out as Csv when there are multiple.

                if (!rawValues.IsNullOrWhitespace())
                {
                    return (T)Convert.ChangeType(values.ToString(), typeof(T));
                }
            }
            return default;
        }
    }
}
