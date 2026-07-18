using Domain.Common.Converter;
using Domain.Common.Identity;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Identity
{
    public class HttpCurrentUser : ICurrentUser
    {
        public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
        {
            ArgumentNullException.ThrowIfNull(httpContextAccessor);
            var context = httpContextAccessor.HttpContext;
            IsAuthenticated = context?.IsAuthenticated() ?? false;
            AuthProviderId = context?.GetAuthProviderId();
            FullName = context?.GetFullName();
            FirstName = context?.GetFirstName();
            LastName = context?.GetLastName();
            Email = context?.GetEmail();
            MobilePhone = context?.GetMobilePhone();
            Source = context?.GetRequestIp();
            UserId = context?.GetUserId();
            IsDisabled = context?.GetIsDisabled() ?? false;
        }

        public string AuthProvider { get; } = AuthConstants.AuthProvider;

        public string? AuthProviderId { get; }

        public bool IsAuthenticated { get; }

        public string? FullName { get; }

        public string? FirstName { get; }

        public string? LastName { get; }

        public string? Email { get; }

        public string? MobilePhone { get; }

        public string? Source { get; }

        public int? UserId { get; }

        public bool IsDisabled { get; }
    }
}
