namespace Application.Common.Streaming
{
    /// <summary>
    /// High-throughput record streaming abstraction (implemented for AWS Kinesis and
    /// Firehose in Infrastructure.Common.AWS.Streaming). Records are serialized as
    /// UTF-8 JSON.
    /// </summary>
    public interface IStreamPublisher
    {
        /// <summary>Publishes a single record. <paramref name="partitionKey"/> controls shard routing where supported.</summary>
        Task PublishAsync<T>(string streamName, T record, string? partitionKey = null, CancellationToken cancellationToken = default);

        /// <summary>Publishes a batch of records; returns the number of successfully accepted records.</summary>
        Task<int> PublishBatchAsync<T>(string streamName, IEnumerable<T> records, CancellationToken cancellationToken = default);
    }
}
