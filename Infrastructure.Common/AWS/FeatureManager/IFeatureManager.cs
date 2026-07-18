namespace Infrastructure.Common.AWS.FeatureManager
{
    /// <summary>
    /// Feature-flag abstraction. Implementations: <see cref="AwsFeatureManager"/>
    /// (SSM Parameter Store under /Features/), <see cref="AppConfigFeatureManager"/>
    /// (AWS AppConfig feature flags) and <see cref="LocalFeatureManager"/> (config file).
    /// </summary>
    public interface IFeatureManager
    {
        /// <summary>Enumerates the names of all known feature flags.</summary>
        IAsyncEnumerable<string> GetFeatureNamesAsync();

        /// <summary>True when the named feature is enabled.</summary>
        Task<bool> IsEnabledAsync(string feature);

        /// <summary>True when the named feature is enabled for the given context.</summary>
        Task<bool> IsEnabledAsync<TContext>(string feature, TContext context);
    }
}
