using Amazon.Runtime;
using LocalStack.Client.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.AWS
{
    public static class AwsServiceConfigurationExtensions
    {
        private static bool? _isLocalStackEnabled;

        private static bool IsLocalStackEnabled(this IConfiguration configuration)
        {
            if (_isLocalStackEnabled.HasValue)
            {
                return _isLocalStackEnabled.Value;
            }

            const string useLocalStackSectionName = "LocalStack:UseLocalStack";

            var useLocalStackSection = configuration.GetSection(useLocalStackSectionName);

            _isLocalStackEnabled = useLocalStackSection.Exists() && useLocalStackSection.Get<bool>();

            return _isLocalStackEnabled.Value;
        }

        public static IServiceCollection AddBaseAwsServiceWithConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            if (services is null)
            {
                throw new InvalidOperationException("Service collection is null");
            }

            if (configuration.IsLocalStackEnabled())
            {
                services
                    .AddLocalStack(configuration)
                    .AddDefaultAwsOptions(configuration.GetAWSOptions());
            }
            else
            {
                services
                    .AddDefaultAWSOptions(configuration.GetAWSOptions());
            }

            return services;
        }

        public static IServiceCollection AddAwsServiceWithConfiguration<T>(this IServiceCollection services, IConfiguration configuration)
        where T : IAmazonService
        {
            if (configuration.IsLocalStackEnabled())
            {
                services
                    .AddBaseAwsServiceWithConfiguration(configuration)
                    .TryAddAwsService<T>();
            }
            else
            {
                services
                    .AddBaseAwsServiceWithConfiguration(configuration)
                    .TryAddAWSService<T>();
            }

            return services;
        }
    }
}
