namespace Application.Common.Identity
{
    public record IdentityUser(string UserName, string? Email, bool Enabled, string Status, Dictionary<string, string> Attributes);

    /// <summary>
    /// User-directory administration abstraction (implemented for AWS Cognito by
    /// Infrastructure.Common.AWS.Identity.CognitoIdentityService).
    /// </summary>
    public interface IIdentityService
    {
        /// <summary>Creates a user with optional attributes; returns the created user.</summary>
        Task<IdentityUser> CreateUserAsync(string userName, string? email, IDictionary<string, string>? attributes = null,
            bool suppressInviteMessage = false, CancellationToken cancellationToken = default);

        /// <summary>Gets a user, or null when not found.</summary>
        Task<IdentityUser?> GetUserAsync(string userName, CancellationToken cancellationToken = default);

        /// <summary>Deletes a user.</summary>
        Task DeleteUserAsync(string userName, CancellationToken cancellationToken = default);

        /// <summary>Enables or disables a user's sign-in.</summary>
        Task SetUserEnabledAsync(string userName, bool enabled, CancellationToken cancellationToken = default);

        /// <summary>Sets a user's password. Permanent passwords skip the forced-change state.</summary>
        Task SetPasswordAsync(string userName, string password, bool permanent = true, CancellationToken cancellationToken = default);

        /// <summary>Adds a user to a group.</summary>
        Task AddToGroupAsync(string userName, string groupName, CancellationToken cancellationToken = default);

        /// <summary>Removes a user from a group.</summary>
        Task RemoveFromGroupAsync(string userName, string groupName, CancellationToken cancellationToken = default);
    }
}
