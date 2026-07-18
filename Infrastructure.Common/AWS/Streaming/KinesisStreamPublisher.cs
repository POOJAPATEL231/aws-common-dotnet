using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Application.Common.Streaming;
using System.Text.Json;

namespace Infrastructure.Common.AWS.Streaming
{
    /// <summary>
    /// <see cref="IStreamPublisher"/> implementation on Amazon Kinesis Data Streams.
    /// Records are serialized as UTF-8 JSON; batches are chunked to the 500-record
    /// PutRecords limit.
    /// </summary>
    public class KinesisStreamPublisher : IStreamPublisher
    {
        private const int _maxBatchSize = 500;
        private readonly IAmazonKinesis _kinesisClient;

        public KinesisStreamPublisher(IAmazonKinesis kinesisClient)
        {
            _kinesisClient = kinesisClient;
        }

        public async Task PublishAsync<T>(string streamName, T record, string? partitionKey = null, CancellationToken cancellationToken = default)
        {
            await _kinesisClient.PutRecordAsync(new PutRecordRequest
            {
                StreamName = streamName,
                PartitionKey = partitionKey ?? Guid.NewGuid().ToString("N"),
                Data = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(record))
            }, cancellationToken);
        }

        public async Task<int> PublishBatchAsync<T>(string streamName, IEnumerable<T> records, CancellationToken cancellationToken = default)
        {
            var entries = records.Select(r => new PutRecordsRequestEntry
            {
                PartitionKey = Guid.NewGuid().ToString("N"),
                Data = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(r))
            }).ToList();

            var succeeded = 0;
            for (var i = 0; i < entries.Count; i += _maxBatchSize)
            {
                var batch = entries.Skip(i).Take(_maxBatchSize).ToList();
                var response = await _kinesisClient.PutRecordsAsync(new PutRecordsRequest
                {
                    StreamName = streamName,
                    Records = batch
                }, cancellationToken);

                succeeded += batch.Count - response.FailedRecordCount;
            }

            return succeeded;
        }
    }
}
