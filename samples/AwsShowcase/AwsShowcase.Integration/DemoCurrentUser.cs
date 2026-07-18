using Domain.Common.Identity;

namespace AwsShowcase.Integration;

/// <summary>Fixed demo identity so audit stamping works without real authentication.</summary>
public class DemoCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public string AuthProvider => "Demo";
    public string? AuthProviderId => "demo-1";
    public string? FullName => "Showcase User";
    public string? FirstName => "Showcase";
    public string? LastName => "User";
    public string? Email => "showcase@example.com";
    public string? MobilePhone => null;
    public string? Source => "AwsShowcase";
    public int? UserId => 1;
    public bool IsDisabled => false;
}
