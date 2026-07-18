using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Application.Common.Identity;

namespace Infrastructure.Common.AWS.Identity
{
    public record CognitoIdentityOptions
    {
        public string UserPoolId { get; init; } = string.Empty;
    }

    /// <summary>
    /// <see cref="IIdentityService"/> implementation on Amazon Cognito admin APIs.
    /// </summary>
    public class CognitoIdentityService : IIdentityService
    {
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;
        private readonly CognitoIdentityOptions _options;

        public CognitoIdentityService(IAmazonCognitoIdentityProvider cognitoClient, CognitoIdentityOptions options)
        {
            _cognitoClient = cognitoClient;
            _options = options;
        }

        public async Task<IdentityUser> CreateUserAsync(string userName, string? email, IDictionary<string, string>? attributes = null,
            bool suppressInviteMessage = false, CancellationToken cancellationToken = default)
        {
            var userAttributes = new List<AttributeType>();
            if (!string.IsNullOrEmpty(email))
            {
                userAttributes.Add(new AttributeType { Name = "email", Value = email });
            }
            if (attributes is not null)
            {
                userAttributes.AddRange(attributes.Select(kvp => new AttributeType { Name = kvp.Key, Value = kvp.Value }));
            }

            var request = new AdminCreateUserRequest
            {
                UserPoolId = _options.UserPoolId,
                Username = userName,
                UserAttributes = userAttributes
            };

            if (suppressInviteMessage)
            {
                request.MessageAction = MessageActionType.SUPPRESS;
            }

            var response = await _cognitoClient.AdminCreateUserAsync(request, cancellationToken);
            return MapUser(response.User.Username, response.User.Attributes, response.User.Enabled, response.User.UserStatus?.Value ?? "UNKNOWN");
        }

        public async Task<IdentityUser?> GetUserAsync(string userName, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _cognitoClient.AdminGetUserAsync(new AdminGetUserRequest
                {
                    UserPoolId = _options.UserPoolId,
                    Username = userName
                }, cancellationToken);

                return MapUser(response.Username, response.UserAttributes, response.Enabled, response.UserStatus?.Value ?? "UNKNOWN");
            }
            catch (UserNotFoundException)
            {
                return null;
            }
        }

        public async Task DeleteUserAsync(string userName, CancellationToken cancellationToken = default)
        {
            await _cognitoClient.AdminDeleteUserAsync(new AdminDeleteUserRequest
            {
                UserPoolId = _options.UserPoolId,
                Username = userName
            }, cancellationToken);
        }

        public async Task SetUserEnabledAsync(string userName, bool enabled, CancellationToken cancellationToken = default)
        {
            if (enabled)
            {
                await _cognitoClient.AdminEnableUserAsync(new AdminEnableUserRequest
                {
                    UserPoolId = _options.UserPoolId,
                    Username = userName
                }, cancellationToken);
            }
            else
            {
                await _cognitoClient.AdminDisableUserAsync(new AdminDisableUserRequest
                {
                    UserPoolId = _options.UserPoolId,
                    Username = userName
                }, cancellationToken);
            }
        }

        public async Task SetPasswordAsync(string userName, string password, bool permanent = true, CancellationToken cancellationToken = default)
        {
            await _cognitoClient.AdminSetUserPasswordAsync(new AdminSetUserPasswordRequest
            {
                UserPoolId = _options.UserPoolId,
                Username = userName,
                Password = password,
                Permanent = permanent
            }, cancellationToken);
        }

        public async Task AddToGroupAsync(string userName, string groupName, CancellationToken cancellationToken = default)
        {
            await _cognitoClient.AdminAddUserToGroupAsync(new AdminAddUserToGroupRequest
            {
                UserPoolId = _options.UserPoolId,
                Username = userName,
                GroupName = groupName
            }, cancellationToken);
        }

        public async Task RemoveFromGroupAsync(string userName, string groupName, CancellationToken cancellationToken = default)
        {
            await _cognitoClient.AdminRemoveUserFromGroupAsync(new AdminRemoveUserFromGroupRequest
            {
                UserPoolId = _options.UserPoolId,
                Username = userName,
                GroupName = groupName
            }, cancellationToken);
        }

        private static IdentityUser MapUser(string userName, List<AttributeType>? attributes, bool enabled, string status)
        {
            var attributeMap = attributes?.ToDictionary(a => a.Name, a => a.Value) ?? new Dictionary<string, string>();
            attributeMap.TryGetValue("email", out var email);
            return new IdentityUser(userName, email, enabled, status, attributeMap);
        }
    }
}
