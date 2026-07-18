namespace Application.Common.Metrics
{
    /// <summary>
    /// Application metrics abstraction (implemented for CloudWatch via Embedded
    /// Metric Format by Infrastructure.Common.AWS.Metrics.EmfMetrics).
    /// </summary>
    public interface IMetrics
    {
        /// <summary>Increments a counter metric by <paramref name="value"/>.</summary>
        void Count(string name, double value = 1, IDictionary<string, string>? dimensions = null);

        /// <summary>Records an arbitrary gauge/value metric.</summary>
        void Gauge(string name, double value, IDictionary<string, string>? dimensions = null);

        /// <summary>Records a duration in milliseconds.</summary>
        void Duration(string name, TimeSpan duration, IDictionary<string, string>? dimensions = null);

        /// <summary>Times an operation and records its duration in milliseconds.</summary>
        Task<T> TimeAsync<T>(string name, Func<Task<T>> operation, IDictionary<string, string>? dimensions = null);
    }
}
