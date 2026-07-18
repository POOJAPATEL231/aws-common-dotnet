using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS
{
    public static class ConfigurationExtensions
    {
        public static CloudWatchOptions? GetCloudWatchOptions(this IConfiguration configuration)
        {
            return configuration.GetSection("AwsInfraSettings:CloudWatchSettings")
                    .Get<CloudWatchOptions>();
        }
    }

}
