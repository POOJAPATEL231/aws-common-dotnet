using Infrastructure.Common.AWS;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common
{
    public static class HostBuilderExtensions
    {
        [ExcludeFromCodeCoverage(Justification = "Test cases are written but not covering on sonar that's why excluding from code coverage.")]
        public static IHostBuilder CreateBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods)] TStartup>(this string[] args)
                where TStartup : class
        {
            var hostBuilder = Host.CreateDefaultBuilder(args);

            hostBuilder.ConfigureAppConfiguration((context, builder) =>
            {
                var config = builder.Build();
                var cloudType = config?.GetSection("InfraSettings")?.GetValue<CloudInfrastructureType>("CloudType");

                // NOTE: only AWS is supported by this library at the moment.
                // Azure/GCP support can be added here later.
                if (cloudType == CloudInfrastructureType.AWS)
                {
                    builder.AddAwsAppConfig(config, context.HostingEnvironment);
                    hostBuilder.ConfigureAwsLogging();
                }
            });

            hostBuilder.ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<TStartup>());

            return hostBuilder;
        }
    }
}
