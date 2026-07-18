using Application.Common.Metrics;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.Common.AWS.Metrics
{
    public record EmfMetricsOptions
    {
        /// <summary>CloudWatch metrics namespace.</summary>
        public string Namespace { get; init; } = "Application";

        /// <summary>Dimensions attached to every metric (e.g. Service, Environment).</summary>
        public Dictionary<string, string> DefaultDimensions { get; init; } = new();
    }

    /// <summary>
    /// <see cref="IMetrics"/> implementation using the CloudWatch Embedded Metric Format:
    /// each metric is emitted as one structured JSON log line, which CloudWatch turns
    /// into a metric automatically - no PutMetricData API calls, no throttling, no cost
    /// per call. Works out of the box on Lambda and on ECS/EC2 with the awslogs driver.
    /// </summary>
    public class EmfMetrics : IMetrics
    {
        private readonly EmfMetricsOptions _options;
        private readonly TextWriter _writer;

        public EmfMetrics(EmfMetricsOptions options)
            : this(options, Console.Out)
        {
        }

        /// <summary>Test/advanced constructor allowing the output writer to be replaced.</summary>
        public EmfMetrics(EmfMetricsOptions options, TextWriter writer)
        {
            _options = options;
            _writer = writer;
        }

        public void Count(string name, double value = 1, IDictionary<string, string>? dimensions = null)
            => Emit(name, value, "Count", dimensions);

        public void Gauge(string name, double value, IDictionary<string, string>? dimensions = null)
            => Emit(name, value, "None", dimensions);

        public void Duration(string name, TimeSpan duration, IDictionary<string, string>? dimensions = null)
            => Emit(name, duration.TotalMilliseconds, "Milliseconds", dimensions);

        public async Task<T> TimeAsync<T>(string name, Func<Task<T>> operation, IDictionary<string, string>? dimensions = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return await operation();
            }
            finally
            {
                stopwatch.Stop();
                Duration(name, stopwatch.Elapsed, dimensions);
            }
        }

        private void Emit(string name, double value, string unit, IDictionary<string, string>? dimensions)
        {
            var allDimensions = new Dictionary<string, string>(_options.DefaultDimensions);
            if (dimensions is not null)
            {
                foreach (var kvp in dimensions)
                {
                    allDimensions[kvp.Key] = kvp.Value;
                }
            }

            // EMF envelope: https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/CloudWatch_Embedded_Metric_Format_Specification.html
            var document = new Dictionary<string, object?>
            {
                ["_aws"] = new Dictionary<string, object?>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["CloudWatchMetrics"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["Namespace"] = _options.Namespace,
                            ["Dimensions"] = new[] { allDimensions.Keys.ToArray() },
                            ["Metrics"] = new[] { new Dictionary<string, object?> { ["Name"] = name, ["Unit"] = unit } }
                        }
                    }
                },
                [name] = value
            };

            foreach (var kvp in allDimensions)
            {
                document[kvp.Key] = kvp.Value;
            }

            // The log line itself must be the bare JSON document for CloudWatch to parse it.
            _writer.WriteLine(JsonSerializer.Serialize(document));
        }
    }
}
