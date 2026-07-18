using Infrastructure.Common.Cache;

namespace Infrastructure.Common
{
    /// <summary>
    /// Which cloud the host application runs on. Drives configuration and logging wiring
    /// in <see cref="HostBuilderExtensions"/>.
    /// </summary>
    public enum CloudInfrastructureType
    {
        None = 0,
        AWS = 1,
        Azure = 2
    }

    /// <summary>
    /// Root infrastructure settings, bound from the "InfraSettings" configuration section.
    /// </summary>
    public class InfraSettings
    {
        public CloudInfrastructureType CloudType { get; init; }

        public RedisCacheOptions RedisCacheSettings { get; init; } = new();
    }
}
