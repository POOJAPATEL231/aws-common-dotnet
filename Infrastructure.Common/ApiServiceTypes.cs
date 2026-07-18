namespace Infrastructure.Common
{
    /// <summary>
    /// Well-known logical service names used with <see cref="AWS.ApiService.IApiService"/>.
    /// A service name selects the named <c>HttpClient</c> registered by the host application,
    /// so consumers can pass any string that matches their own client registrations -
    /// the constants here only cover names this library itself references.
    /// </summary>
    public static class ApiServiceTypes
    {
        /// <summary>Identity/authentication service (used for dev-token acquisition).</summary>
        public static readonly string Identity = "Identity";
    }
}
