using Amazon.KinesisFirehose;
using Amazon.KinesisFirehose.Model;
using Application.Common.Streaming;
using System.Text.Json;

namespace Infrastructure.Common.AWS.Streaming
{
    /// <summary>
    /// <see cref="IStreamPublisher"/> implementation on Amazon Data Firehose - use when
    /// records should land in S3/Redshift/OpenSearch without consumer code. A newline is
    /// appended to every record so downstream files are line-delimited JSON.
    /// </summary>
    public class FirehoseStreamPublisher : IStreamPublisher
    {
        private const int _maxBatchSize = 500;
        private readonly IAmazonKinesisFirehose _firehoseClient;

        public FirehoseStreamPublisher(IAmazonKinesisFirehose firehoseClient)
        {
            _firehoseClient = firehoseClient;
        }

        public async Task PublishAsync<T>(string streamName, T record, string? partitionKey = null, CancellationToken cancellationToken = default)
        {
            await _firehoseClient.PutRecordAsync(new PutRecordRequest
            {
                DeliveryStreamName = streamName,
                Record = ToRecord(record)
            }, cancellationToken);
        }

        public async Task<int> PublishBatchAsync<T>(string streamName, IEnumerable<T> records, CancellationToken cancellationToken = default)
        {
            var allRecords = records.Select(ToRecord).ToList();
            var succeeded = 0;

            for (var i = 0; i < allRecords.Count; i += _maxBatchSize)
            {
                var batch = allRecords.Skip(i).Take(_maxBatchSize).ToList();
                var response = await _firehoseClient.PutRecordBatchAsync(new PutRecordBatchRequest
                {
                    DeliveryStreamName = streamName,
                    Records = batch
                }, cancellationToken);

                succeeded += batch.Count - response.FailedPutCount;
            }

            return succeeded;
        }

        private static Record ToRecord<T>(T record)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(record);
            var withNewline = new byte[json.Length + 1];
            json.CopyTo(withNewline, 0);
            withNewline[^1] = (byte)'\n';
            return new Record { Data = new MemoryStream(withNewline) };
        }
    }
}
