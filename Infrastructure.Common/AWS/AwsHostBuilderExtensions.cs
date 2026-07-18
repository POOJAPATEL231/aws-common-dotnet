using Amazon.CloudWatchLogs;
using Infrastructure.Common.AWS.XRay;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Formatting.Compact;
using Serilog.Sinks.AwsCloudWatch;
using Serilog;
using Amazon.Extensions.NETCore.Setup;

namespace Infrastructure.Common.AWS
{
    public static class AwsHostBuilderExtensions
    {
        public static IConfigurationBuilder AddAwsAppConfig(this IConfigurationBuilder builder, IConfigurationRoot? configuration, IHostEnvironment env)
        {
            var configurationReloadTimeSpan = configuration?.GetSection("AwsInfraSettings:ConfigurationReloadTimeSpan").Get<TimeSpan>();

            var awsOptions = configuration?.GetAWSOptions();
            if (!env.IsDevelopment() && awsOptions is not null)
            {
                builder.AddSystemsManager(sources =>
                {
                    sources.Path = "/Configs/";
                    sources.ReloadAfter = configurationReloadTimeSpan;
                    sources.AwsOptions = new AWSOptions
                    {
                        Region = awsOptions.Region
                    };
                });
            }
            return builder;
        }

        public static IHostBuilder ConfigureAwsLogging(this IHostBuilder builder)
        {
            return builder.ConfigureLogging(static (context, loggingBuilder) =>
            {
                var env = context.HostingEnvironment;


                env.AddAwsLogging(loggingBuilder, context.Configuration);
            });
        }

        public static void AddAwsLogging(this IHostEnvironment env, ILoggingBuilder loggingBuilder, IConfiguration configuration)
        {
            var cloudWatchOptions = configuration.GetCloudWatchOptions();
            var awsOptions = configuration.GetAWSOptions();

            if (cloudWatchOptions is not null && awsOptions is not null && !env.IsDevelopment() && !env.IsEnvironment("Test"))
            {
                var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                    .WriteTo.AmazonCloudWatch(
                        new CloudWatchSinkOptions
                        {
                            LogGroupName = env.ApplicationName,
                            LogStreamNameProvider = new LogStreamNameProvider(env.ApplicationName),
                            CreateLogGroup = true,
                            TextFormatter = new RenderedCompactJsonFormatter(),
                            LogGroupRetentionPolicy = cloudWatchOptions.LogGroupRetentionPolicy
                        },
                        new AmazonCloudWatchLogsClient(awsOptions.Region)
                    )
                    .CreateLogger();
                loggingBuilder.AddSerilog(logger);
            }
            else
            {
                loggingBuilder.AddDebug();
                loggingBuilder.AddConsole();
            }
        }
    }
}
