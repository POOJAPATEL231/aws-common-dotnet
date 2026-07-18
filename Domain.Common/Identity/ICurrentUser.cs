using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common.Identity
{
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }

        string AuthProvider { get; }

        string? AuthProviderId { get; }

        string? FullName { get; }

        string? FirstName { get; }

        string? LastName { get; }

        string? Email { get; }

        string? MobilePhone { get; }

        string? Source { get; }

        int? UserId { get; }

        bool IsDisabled { get; }
    }
}
