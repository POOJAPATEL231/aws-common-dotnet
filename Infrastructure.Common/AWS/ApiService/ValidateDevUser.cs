namespace Infrastructure.Common.AWS.ApiService
{
    /// <summary>
    /// Request sent to the identity service's "devtokens" endpoint to obtain a
    /// development bearer token using basic-auth credentials.
    /// </summary>
    public record ValidateDevUserCommand(string UserName, string Password);

    /// <summary>
    /// Response from the identity service's "devtokens" endpoint.
    /// </summary>
    public record ValidateDevUserResponse
    {
        public string Token { get; init; } = string.Empty;
    }
}
