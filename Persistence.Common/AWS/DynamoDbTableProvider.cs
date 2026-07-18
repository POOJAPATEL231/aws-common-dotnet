using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;
using Domain.Common.Entities;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Domain.Common.Settings;

namespace Persistence.Common.AWS
{
    public class DynamoDbTableProvider<TEntity> : IDynamoDbTableProvider where TEntity : DocEntity
    {
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly string _tableName;
        private readonly List<AwsTag> _defaultTags;

        public DynamoDbTableProvider(IAmazonDynamoDB dynamoDbClient,
         IOptions<AwsTagsConfigSettings> tagConfigurationSettings)
        {
            _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
            _tableName = DynamoUtils.GetTableName<TEntity>();
            _defaultTags = tagConfigurationSettings.Value.DefaultTags;
        }

        public async Task<bool> CreateTableAsync(long readCapacityUnits = 5, long writeCapacityUnits = 5, CancellationToken cancellationToken = default)
        {
            var keySchema = new List<KeySchemaElement>();
            var attributeDefinitions = new List<AttributeDefinition>();

            var hashKeyProperty = DynamoUtils.ResolveHashKeyProperty<TEntity>();
            if (hashKeyProperty.Property != null && !string.IsNullOrWhiteSpace(hashKeyProperty.PropertyName))
            {
                keySchema.Add(new KeySchemaElement(hashKeyProperty.PropertyName, KeyType.HASH));
                attributeDefinitions.Add(new AttributeDefinition(hashKeyProperty.PropertyName, GetAttributeType(hashKeyProperty.Property.PropertyType)));
            }
            else
            {
                throw new InvalidOperationException("No hash key defined on the entity.");
            }

            var rangeKeyProperty = DynamoUtils.ResolveRangeKeyProperty<TEntity>();
            if (rangeKeyProperty.Property != null && !string.IsNullOrWhiteSpace(rangeKeyProperty.PropertyName))
            {
                keySchema.Add(new KeySchemaElement(rangeKeyProperty.PropertyName, KeyType.RANGE));
                attributeDefinitions.Add(new AttributeDefinition(rangeKeyProperty.PropertyName, GetAttributeType(rangeKeyProperty.Property.PropertyType)));
            }

            var tags = _defaultTags.Select(t => new Tag
            {
                Key = t.Key,
                Value = t.Value
            }).ToList();

            var request = new CreateTableRequest
            {
                TableName = _tableName,
                KeySchema = keySchema,
                AttributeDefinitions = attributeDefinitions,
                ProvisionedThroughput = new ProvisionedThroughput(readCapacityUnits, writeCapacityUnits),
                Tags = tags
            };

            var response = await _dynamoDbClient.CreateTableAsync(request, cancellationToken);

            // Check if the table was created successfully
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return false;
            }

            return true;
        }

        private async Task<bool> IsTtlEnabledAsync(CancellationToken cancellationToken)
        {
            var response = await _dynamoDbClient.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest
            {
                TableName = _tableName
            }, cancellationToken);

            return response?.TimeToLiveDescription.TimeToLiveStatus == TimeToLiveStatus.ENABLED;
        }

        public async Task<bool> EnableTtlAsync(CancellationToken cancellationToken = default)
        {
            var describeResponse = await _dynamoDbClient.DescribeTableAsync(new DescribeTableRequest
            {
                TableName = _tableName
            }, cancellationToken);

            // Check if TEntity inherits from DocEntityTtl and enable TTL if so
            if (describeResponse.Table.TableStatus == TableStatus.ACTIVE && typeof(DocEntityTtl).IsAssignableFrom(typeof(TEntity)) && !await IsTtlEnabledAsync(cancellationToken))
            {
                // Create a request to enable TTL
                var ttlRequest = new UpdateTimeToLiveRequest
                {
                    TableName = _tableName,
                    TimeToLiveSpecification = new TimeToLiveSpecification
                    {
                        AttributeName = nameof(DocEntityTtl.TimeToLive),
                        Enabled = true
                    }
                };

                try
                {
                    // Call UpdateTimeToLiveAsync to enable TTL
                    var ttlResponse = await _dynamoDbClient.UpdateTimeToLiveAsync(ttlRequest, cancellationToken);
                    return ttlResponse?.HttpStatusCode == System.Net.HttpStatusCode.OK;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return true;
        }

        public async Task<bool> UpdateTableAsync(long readCapacityUnits, long writeCapacityUnits, CancellationToken cancellationToken = default)
        {
            var request = new UpdateTableRequest
            {
                TableName = _tableName,
                ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = readCapacityUnits,
                    WriteCapacityUnits = writeCapacityUnits
                }
            };

            var response = await _dynamoDbClient.UpdateTableAsync(request, cancellationToken);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task<bool> DeleteTableAsync(CancellationToken cancellationToken = default)
        {
            var request = new DeleteTableRequest
            {
                TableName = _tableName
            };

            var response = await _dynamoDbClient.DeleteTableAsync(request, cancellationToken);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task<bool> TableExistsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _dynamoDbClient.DescribeTableAsync(_tableName, cancellationToken);
                return response.Table.TableStatus == TableStatus.ACTIVE;
            }
            catch (ResourceNotFoundException)
            {
                return false;
            }
        }

        public async Task<List<TableDescription>> ListTablesAsync(CancellationToken cancellationToken = default)
        {
            var request = new ListTablesRequest();
            var response = await _dynamoDbClient.ListTablesAsync(request, cancellationToken);
            var tableDescriptions = new List<TableDescription>();

            foreach (var tableName in response.TableNames)
            {
                var tableDescription = await _dynamoDbClient.DescribeTableAsync(tableName, cancellationToken);
                tableDescriptions.Add(tableDescription.Table);
            }

            return tableDescriptions;
        }

        private static ScalarAttributeType GetAttributeType(Type type)
        {
            // Unwrap Nullable<T> so int? etc. map like their underlying type.
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type.IsEnum)
            {
                return ScalarAttributeType.S;
            }
            else if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                || type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            {
                return ScalarAttributeType.N; // DynamoDB treats all numbers as "N"
            }
            else if (type == typeof(byte[]))
            {
                return ScalarAttributeType.B; // "B" is Binary in DynamoDB
            }
            else
            {
                // Note: bool is intentionally not supported - DynamoDB keys can only be S, N or B.
                throw new NotSupportedException($"Type {type} is not supported as a key in DynamoDB.");
            }
        }
    }
}
