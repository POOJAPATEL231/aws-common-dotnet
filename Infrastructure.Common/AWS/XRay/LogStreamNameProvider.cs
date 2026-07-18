using Serilog.Sinks.AwsCloudWatch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.XRay
{
    public class LogStreamNameProvider : ILogStreamNameProvider
    {
        private readonly string _applicationName;

        public LogStreamNameProvider(string applicationName)
        {
            _applicationName = applicationName;
        }

        public string GetLogStreamName()
        {
            const string dateFormat = "yyyy-MM-dd";
            return $"{_applicationName}_{DateTime.UtcNow.ToString(dateFormat)}";
        }
    }
}
