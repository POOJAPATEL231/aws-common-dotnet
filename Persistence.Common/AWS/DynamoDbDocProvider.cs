using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;
using Domain.Common.Entities;
using Domain.Common;
using Persistence.Common.AWS.EntityManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils.Common.Crypto;
using Amazon.DynamoDBv2.DocumentModel;
using Persistence.Common.AWS.Builder;

namespace Persistence.Common.AWS
{
    public class DynamoDbDocProvider<TEntity> : IDynamoDbDocProvider<TEntity> where TEntity : DocEntity
    {
        private readonly IAmazonDynamoDB _dynamoDB;
        private readonly string _tableName;

        public DynamoDbDocProvider(IAmazonDynamoDB dynamoDBClient, ICryptoProvider cryptoProvider)
        {
            _dynamoDB = dynamoDBClient;
            _tableName = DynamoUtils.GetTableName<TEntity>();
            EntityDocumentConverter.Initialize(cryptoProvider);
        }

        public async Task<TEntity?> GetItemAsync(string hash, string? range = null, CancellationToken cancellationToken = default)
        {
            var getItemRequest = new GetItemRequest
            {
                Key = DynamoUtils.GetKeyAttributeValueDictionary<TEntity>(hash, range),
                TableName = _tableName
            };

            var response = await _dynamoDB.GetItemAsync(getItemRequest, cancellationToken);
            return response.Item is { Count: > 0 }
                ? EntityDocumentConverter.FromConfiguredDocument<TEntity>(Document.FromAttributeMap(response.Item))
                : default;
        }

        public async Task<TEntity?> GetItemByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return await GetItemAsync(id, cancellationToken: cancellationToken);
        }

        public async Task<TEntity?> GetItemByIdAndKeyAsync(string id, string key, CancellationToken cancellationToken = default)
        {
            return await GetItemAsync(id, key, cancellationToken);
        }

        public async Task<IEnumerable<TEntity>?> GetAllItemsAsync(CancellationToken cancellationToken = default)
        {
            return await ExecuteScanAsync(null, null, null, cancellationToken: cancellationToken);
        }

        public async Task<TEntity?> GetAnyItemAsync(CancellationToken cancellationToken = default)
        {
            var scanRequest = new ScanRequest(_tableName)
            {
                Select = Select.ALL_ATTRIBUTES,
                Limit = 1
            };

            var scanResponse = await _dynamoDB.ScanAsync(scanRequest, cancellationToken);
            return scanResponse.Items is { Count: > 0 }
                                ? EntityDocumentConverter.FromConfiguredDocument<TEntity>(Document.FromAttributeMap(scanResponse.Items.FirstOrDefault()))
                                : default;
        }

        public async Task<IEnumerable<TEntity>?> GetItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryAsync(filterExpression, keyConditionExpression, filterAttributeValues,
             expressionAttributeNames, null, cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<TEntity>?> GetItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
             bool isScanIndexForward, CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryAsync(filterExpression, keyConditionExpression, filterAttributeValues,
             expressionAttributeNames, isScanIndexForward, cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<TEntity>?> GetItemsByQueryAsync(
            string filterExpression, Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteScanAsync(filterExpression, filterAttributeValues, expressionAttributeNames, cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<TEntity>?> GetItemsByQueryAsync(
            string filterExpression, KeyFilter keyFilter,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteQueryAsync(filterExpression, keyFilter.GenerateFinalKeyFilter(), filterAttributeValues,
                expressionAttributeNames, null, indexName: keyFilter.IndexName, cancellationToken: cancellationToken);
        }

        public async Task<TEntity?> GetItemByQueryAsync(
            string filterExpression, KeyFilter keyFilter,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            var result = await GetItemsByQueryAsync(filterExpression, keyFilter, filterAttributeValues, expressionAttributeNames, cancellationToken);
            return result is null ? default : result.FirstOrDefault();
        }

        public async Task<int> CountItemsByQueryAsync(
            string filterExpression, KeyFilter keyFilter,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteCountQueryAsync(filterExpression, keyFilter.GenerateFinalKeyFilter(), filterAttributeValues,
                expressionAttributeNames, keyFilter.IndexName, cancellationToken);
        }

        public async Task<PagedList<TEntity>> GetPagedItemsByQueryAsync(
            string filterExpression, KeyFilter keyFilter,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return await ExecutePagedQueryAsync(filterExpression, keyFilter.GenerateFinalKeyFilter(), filterAttributeValues,
                expressionAttributeNames, null, page, pageSize, keyFilter.IndexName, cancellationToken);
        }

        public async Task<TEntity?> GetItemByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            var result = await ExecuteQueryAsync(filterExpression, keyConditionExpression, filterAttributeValues,
                                                expressionAttributeNames, null, cancellationToken: cancellationToken);
            return result?.FirstOrDefault();
        }

        public async Task<TEntity?> GetItemByQueryAsync(
            string filterExpression, Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            var result = await ExecuteScanAsync(filterExpression, filterAttributeValues, expressionAttributeNames, cancellationToken: cancellationToken);
            return result?.FirstOrDefault();
        }

        public async Task<int> CountItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
             CancellationToken cancellationToken = default)
        {
            return await ExecuteCountQueryAsync(filterExpression, keyConditionExpression, filterAttributeValues,
            expressionAttributeNames, null, cancellationToken);
        }

        public async Task<int> CountItemsByScanAsync(
            string filterExpression, Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteCountScanAsync(filterExpression, filterAttributeValues, expressionAttributeNames, cancellationToken);
        }

        public async Task<PagedList<TEntity>> GetPagedItemsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return await ExecutePagedScanAsync(null, null, null, page, pageSize, cancellationToken);
        }

        public async Task<PagedList<TEntity>> GetPagedItemsAsync(
            string filterExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return await ExecutePagedScanAsync(filterExpression, filterAttributeValues, expressionAttributeNames,
             page, pageSize, cancellationToken);
        }

        public async Task<PagedList<TEntity>> GetPagedItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return await ExecutePagedQueryAsync(filterExpression, keyConditionExpression, filterAttributeValues,
            expressionAttributeNames, null, page, pageSize, cancellationToken: cancellationToken);
        }

        public async Task<PagedList<TEntity>> GetPagedItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            bool isScanIndexForward, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return await ExecutePagedQueryAsync(filterExpression, keyConditionExpression, filterAttributeValues,
            expressionAttributeNames, isScanIndexForward, page, pageSize, cancellationToken: cancellationToken);
        }

        public async Task CreateItemAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var document = EntityDocumentConverter.ToConfiguredDocument(entity);
            var putItemRequest = new PutItemRequest
            {
                Item = document.ToAttributeMap(),
                TableName = _tableName
            };
            await _dynamoDB.PutItemAsync(putItemRequest, cancellationToken);
        }

        public async Task CreateItemsAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var tasks = entities.Select(entity => CreateItemAsync(entity, cancellationToken)).ToList();
            await Task.WhenAll(tasks);
        }

        public async Task<TEntity> UpdateItemAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var document = EntityDocumentConverter.ToConfiguredDocument(entity);
            var keyAttributeValues = entity.GetKeyAttributeValueDictionary();
            var attributeUpdates = document.ToAttributeUpdateMap(false);
            foreach (var key in keyAttributeValues.Keys)
            {
                attributeUpdates.Remove(key);
            }
            var updateItemRequest = new UpdateItemRequest
            {
                Key = keyAttributeValues,
                AttributeUpdates = attributeUpdates,
                TableName = _tableName,
                ReturnValues = ReturnValue.ALL_NEW
            };

            var result = await _dynamoDB.UpdateItemAsync(updateItemRequest, cancellationToken);
            return EntityDocumentConverter.FromConfiguredDocument<TEntity>(Document.FromAttributeMap(result.Attributes));
        }

        public async Task<IEnumerable<TEntity>> UpdateItemsAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var tasks = entities.Select(entity => UpdateItemAsync(entity, cancellationToken)).ToList();
            var results = await Task.WhenAll(tasks);
            return results.AsEnumerable();
        }

        public async Task<TEntity> DeleteItemAsync(string hash, string? range = null, CancellationToken cancellationToken = default)
        {
            var deleteItemRequest = new DeleteItemRequest
            {
                Key = DynamoUtils.GetKeyAttributeValueDictionary<TEntity>(hash, range),
                ReturnValues = ReturnValue.ALL_OLD,
                TableName = _tableName
            };

            var response = await _dynamoDB.DeleteItemAsync(deleteItemRequest, cancellationToken);
            var document = Document.FromAttributeMap(response.Attributes);
            return EntityDocumentConverter.FromConfiguredDocument<TEntity>(document);
        }

        public async Task<TEntity> DeleteItemAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var deleteItemRequest = new DeleteItemRequest
            {
                Key = entity.GetKeyAttributeValueDictionary(),
                ReturnValues = ReturnValue.ALL_OLD,
                TableName = _tableName
            };

            var response = await _dynamoDB.DeleteItemAsync(deleteItemRequest, cancellationToken);
            var document = Document.FromAttributeMap(response.Attributes);
            return EntityDocumentConverter.FromConfiguredDocument<TEntity>(document);
        }

        private async Task<IEnumerable<TEntity>?> ExecuteScanAsync(
            string? filterExpression,
             Dictionary<string, AttributeValue>? filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
             IEnumerable<string>? projectionAttributes = null,
             int? limit = null,
             CancellationToken cancellationToken = default)
        {
            var entities = new List<Dictionary<string, AttributeValue>>();
            var lastEvaluatedKey = new Dictionary<string, AttributeValue>();

            do
            {
                var scanRequest = new ScanRequest(_tableName)
                {
                    ExclusiveStartKey = lastEvaluatedKey
                };

                if (!string.IsNullOrWhiteSpace(filterExpression))
                {
                    scanRequest.FilterExpression = filterExpression;
                }

                if (projectionAttributes is not null && projectionAttributes.Any())
                {
                    scanRequest.Select = Select.SPECIFIC_ATTRIBUTES;
                    scanRequest.ProjectionExpression = string.Join(',', projectionAttributes);
                }
                else
                {
                    scanRequest.Select = Select.ALL_ATTRIBUTES;
                }

                if (filterAttributeValues != null && filterAttributeValues.Count > 0)
                {
                    scanRequest.ExpressionAttributeValues = filterAttributeValues;
                }

                if (expressionAttributeNames != null && expressionAttributeNames.Count > 0)
                {
                    scanRequest.ExpressionAttributeNames = expressionAttributeNames;
                }

                var scanResponse = await _dynamoDB.ScanAsync(scanRequest, cancellationToken);
                if (scanResponse != null && scanResponse.Items.Count > 0)
                {
                    entities.AddRange(scanResponse.Items);
                }

                lastEvaluatedKey = scanResponse?.LastEvaluatedKey;
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0 && (!limit.HasValue || entities.Count < limit.Value));

            if (limit.HasValue && entities.Count > limit.Value)
            {
                entities = entities.Take(limit.Value).ToList();
            }

            return entities.Count > 0
                ? EntityDocumentConverter.FromConfiguredDocuments<TEntity>(entities.Select(Document.FromAttributeMap))
                : default;
        }

        private async Task<IEnumerable<TEntity>?> ExecuteQueryAsync(
            string? filterExpression, string? keyConditionExpression,
            Dictionary<string, AttributeValue>? filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            bool? isScanIndexForward, IEnumerable<string>? projectionAttributes = null,
            int? limit = null,
            string? indexName = null,
            CancellationToken cancellationToken = default)
        {
            var entities = new List<Dictionary<string, AttributeValue>>();
            var lastEvaluatedKey = new Dictionary<string, AttributeValue>();

            do
            {
                var queryRequest = new QueryRequest(_tableName)
                {
                    ExclusiveStartKey = lastEvaluatedKey
                };

                if (!string.IsNullOrWhiteSpace(indexName))
                {
                    queryRequest.IndexName = indexName;
                }

                if (projectionAttributes is not null && projectionAttributes.Any())
                {
                    queryRequest.Select = Select.SPECIFIC_ATTRIBUTES;
                    queryRequest.ProjectionExpression = string.Join(',', projectionAttributes);
                }
                else
                {
                    queryRequest.Select = Select.ALL_ATTRIBUTES;
                }

                if (isScanIndexForward.HasValue)
                {
                    queryRequest.ScanIndexForward = isScanIndexForward.Value;
                }

                if (!string.IsNullOrWhiteSpace(filterExpression))
                {
                    queryRequest.FilterExpression = filterExpression;
                }

                if (!string.IsNullOrWhiteSpace(keyConditionExpression))
                {
                    queryRequest.KeyConditionExpression = keyConditionExpression;
                }

                if (filterAttributeValues != null && filterAttributeValues.Count > 0)
                {
                    queryRequest.ExpressionAttributeValues = filterAttributeValues;
                }

                if (expressionAttributeNames != null && expressionAttributeNames.Count > 0)
                {
                    queryRequest.ExpressionAttributeNames = expressionAttributeNames;
                }

                var queryResponse = await _dynamoDB.QueryAsync(queryRequest, cancellationToken);
                if (queryResponse != null && queryResponse.Items.Count > 0)
                {
                    entities.AddRange(queryResponse.Items);
                }

                lastEvaluatedKey = queryResponse?.LastEvaluatedKey;
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0 && (!limit.HasValue || entities.Count < limit.Value));

            if (limit.HasValue && entities.Count > limit.Value)
            {
                entities = entities.Take(limit.Value).ToList();
            }

            return entities.Count > 0
                ? EntityDocumentConverter.FromConfiguredDocuments<TEntity>(entities.Select(Document.FromAttributeMap))
                : default;
        }

        private async Task<int> ExecuteCountScanAsync(
            string? filterExpression,
            Dictionary<string, AttributeValue>? filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken)
        {
            var count = 0;
            var lastEvaluatedKey = new Dictionary<string, AttributeValue>();

            do
            {
                var scanRequest = new ScanRequest(_tableName)
                {
                    ExclusiveStartKey = lastEvaluatedKey,
                    Select = Select.COUNT
                };

                if (!string.IsNullOrWhiteSpace(filterExpression))
                {
                    scanRequest.FilterExpression = filterExpression;
                }

                if (filterAttributeValues != null && filterAttributeValues.Count > 0)
                {
                    scanRequest.ExpressionAttributeValues = filterAttributeValues;
                }

                if (expressionAttributeNames != null && expressionAttributeNames.Count > 0)
                {
                    scanRequest.ExpressionAttributeNames = expressionAttributeNames;
                }

                var scanResponse = await _dynamoDB.ScanAsync(scanRequest, cancellationToken);
                if (scanResponse != null)
                {
                    count += scanResponse.Count;
                }

                lastEvaluatedKey = scanResponse?.LastEvaluatedKey;
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return count;
        }

        private async Task<int> ExecuteCountQueryAsync(
            string? filterExpression, string? keyConditionExpression,
            Dictionary<string, AttributeValue>? filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            string? indexName,
            CancellationToken cancellationToken)
        {
            var count = 0;
            var lastEvaluatedKey = new Dictionary<string, AttributeValue>();

            do
            {
                var queryRequest = new QueryRequest(_tableName)
                {
                    ExclusiveStartKey = lastEvaluatedKey,
                    Select = Select.COUNT
                };

                if (!string.IsNullOrWhiteSpace(indexName))
                {
                    queryRequest.IndexName = indexName;
                }

                if (!string.IsNullOrWhiteSpace(filterExpression))
                {
                    queryRequest.FilterExpression = filterExpression;
                }

                if (!string.IsNullOrWhiteSpace(keyConditionExpression))
                {
                    queryRequest.KeyConditionExpression = keyConditionExpression;
                }

                if (filterAttributeValues != null && filterAttributeValues.Count > 0)
                {
                    queryRequest.ExpressionAttributeValues = filterAttributeValues;
                }

                if (expressionAttributeNames != null && expressionAttributeNames.Count > 0)
                {
                    queryRequest.ExpressionAttributeNames = expressionAttributeNames;
                }

                var queryResponse = await _dynamoDB.QueryAsync(queryRequest, cancellationToken);
                if (queryResponse != null)
                {
                    count += queryResponse.Count;
                }

                lastEvaluatedKey = queryResponse?.LastEvaluatedKey;
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            return count;
        }

        private async Task<PagedList<TEntity>> ExecutePagedScanAsync(
            string? filterExpression, Dictionary<string, AttributeValue>? filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            int page, int pageSize, CancellationToken cancellationToken)
        {
            // DynamoDB has no offset support, so we stream matched items, skipping
            // everything before the requested page window and continuing to the end
            // to produce an accurate total count. Only the window is materialized.
            var skip = Math.Max(0, (page - 1) * pageSize);
            var lastEvaluatedKey = new Dictionary<string, AttributeValue>();
            var window = new List<Dictionary<string, AttributeValue>>();
            var totalRecords = 0;

            do
            {
                var scanRequest = new ScanRequest(_tableName)
                {
                    ExclusiveStartKey = lastEvaluatedKey,
                    Select = Select.ALL_ATTRIBUTES
                };

                if (!string.IsNullOrWhiteSpace(filterExpression))
                {
                    scanRequest.FilterExpression = filterExpression;
                }

                if (filterAttributeValues != null && filterAttributeValues.Count > 0)
                {
                    scanRequest.ExpressionAttributeValues = filterAttributeValues;
                }

                if (expressionAttributeNames != null && expressionAttributeNames.Count > 0)
                {
                    scanRequest.ExpressionAttributeNames = expressionAttributeNames;
                }

                var scanResponse = await _dynamoDB.ScanAsync(scanRequest, cancellationToken);

                if (scanResponse != null && scanResponse.Items.Count > 0)
                {
                    foreach (var item in scanResponse.Items)
                    {
                        if (totalRecords >= skip && window.Count < pageSize)
                        {
                            window.Add(item);
                        }
                        totalRecords++;
                    }
                }

                lastEvaluatedKey = scanResponse?.LastEvaluatedKey;
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count != 0);

            return new PagedList<TEntity>
            {
                Items = EntityDocumentConverter.FromConfiguredDocuments<TEntity>(window.Select(Document.FromAttributeMap)),
                TotalRecords = totalRecords,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task<PagedList<TEntity>> ExecutePagedQueryAsync(
            string? filterExpression, string? keyConditionExpression,
            Dictionary<string, AttributeValue>? filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            bool? isScanIndexForward, int page, int pageSize,
            string? indexName = null, CancellationToken cancellationToken = default)
        {
            // See ExecutePagedScanAsync: skip matched items before the page window and
            // keep counting to the end for an accurate total.
            var skip = Math.Max(0, (page - 1) * pageSize);
            var lastEvaluatedKey = new Dictionary<string, AttributeValue>();
            var window = new List<Dictionary<string, AttributeValue>>();
            var totalRecords = 0;

            do
            {
                var request = new QueryRequest(_tableName)
                {
                    ExclusiveStartKey = lastEvaluatedKey,
                    Select = Select.ALL_ATTRIBUTES
                };

                if (!string.IsNullOrWhiteSpace(indexName))
                {
                    request.IndexName = indexName;
                }

                if (isScanIndexForward.HasValue)
                {
                    request.ScanIndexForward = isScanIndexForward.Value;
                }

                if (!string.IsNullOrWhiteSpace(filterExpression))
                {
                    request.FilterExpression = filterExpression;
                }

                if (!string.IsNullOrWhiteSpace(keyConditionExpression))
                {
                    request.KeyConditionExpression = keyConditionExpression;
                }

                if (filterAttributeValues != null && filterAttributeValues.Count > 0)
                {
                    request.ExpressionAttributeValues = filterAttributeValues;
                }

                if (expressionAttributeNames != null && expressionAttributeNames.Count > 0)
                {
                    request.ExpressionAttributeNames = expressionAttributeNames;
                }

                var response = await _dynamoDB.QueryAsync(request, cancellationToken);

                if (response != null && response.Items.Count > 0)
                {
                    foreach (var item in response.Items)
                    {
                        if (totalRecords >= skip && window.Count < pageSize)
                        {
                            window.Add(item);
                        }
                        totalRecords++;
                    }
                }

                lastEvaluatedKey = response?.LastEvaluatedKey;
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count != 0);

            return new PagedList<TEntity>
            {
                Items = EntityDocumentConverter.FromConfiguredDocuments<TEntity>(window.Select(Document.FromAttributeMap)),
                TotalRecords = totalRecords,
                Page = page,
                PageSize = pageSize
            };
        }

        /// <summary>Resolves the stored attribute name of the ETag concurrency stamp.</summary>
        private static string GetETagAttributeName()
        {
            var configuration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<TEntity>();
            return configuration?
                .GetPropertyConfigurations()
                .Find(e => e.PropertyName == nameof(DocEntity.ETag))?
                .JsonPropertyName ?? nameof(DocEntity.ETag);
        }

        public TransactWriteItem GetAddTransactWriteItem(TEntity entity)
        {
            // Stamp a fresh concurrency token so subsequent updates have a baseline.
            entity.SetETag(Guid.NewGuid().ToString("N"));

            var document = EntityDocumentConverter.ToConfiguredDocument(entity);
            var hashKeyAttributeName = DynamoUtils.ResolveHashKeyPropertyName<TEntity>();

            var put = new Put
            {
                Item = document.ToAttributeMap(),
                TableName = _tableName
            };

            // Adding must not silently overwrite an existing item (EF Add semantics).
            if (!string.IsNullOrWhiteSpace(hashKeyAttributeName))
            {
                put.ConditionExpression = "attribute_not_exists(#pk)";
                put.ExpressionAttributeNames = new Dictionary<string, string> { ["#pk"] = hashKeyAttributeName };
            }

            return new TransactWriteItem { Put = put };
        }

        public TransactWriteItem GetUpdateTransactWriteItem(TEntity entity)
        {
            // Optimistic concurrency: remember the ETag the entity was read with, then
            // rotate it so the stored item carries a new stamp after this write.
            var expectedETag = entity.ETag;
            entity.SetETag(Guid.NewGuid().ToString("N"));

            // Convert the entity to a DynamoDB document
            var document = EntityDocumentConverter.ToConfiguredDocument(entity);

            // Extract the key attributes (primary key values)
            var keyAttributeValues = entity.GetKeyAttributeValueDictionary();

            // Create placeholders for ExpressionAttributeNames and Values
            var expressionAttributeValues = new Dictionary<string, AttributeValue>();
            var expressionAttributeNames = new Dictionary<string, string>();
            var updateExpression = new List<string>();
            var attributeUpdates = document.ToAttributeMap();

            foreach (var key in keyAttributeValues.Keys)
            {
                attributeUpdates.Remove(key);
            }

            // If the entity has no non-key attributes, an Update with an empty SET clause
            // would be invalid - replace the whole item instead.
            if (attributeUpdates.Count == 0)
            {
                return GetAddTransactWriteItem(entity);
            }

            // Iterate over each attribute in the document (except keys) to construct UpdateExpression.
            // Placeholders are index-based so attribute names with special characters cannot
            // produce invalid placeholder tokens.
            var placeholderIndex = 0;
            foreach (var attribute in attributeUpdates)
            {
                updateExpression.Add($"#attr_{placeholderIndex} = :val_{placeholderIndex}");

                // Add the actual values and names to the respective dictionaries
                expressionAttributeValues[$":val_{placeholderIndex}"] = attribute.Value;
                expressionAttributeNames[$"#attr_{placeholderIndex}"] = attribute.Key;
                placeholderIndex++;
            }

            // Combine all update parts into a single UpdateExpression
            var updateExpressionString = "SET " + string.Join(", ", updateExpression);

            // Optimistic-concurrency condition: the stored ETag must still match the one
            // this entity was read with. A missing expected ETag (legacy/new item) instead
            // requires the attribute to be absent.
            var etagAttributeName = GetETagAttributeName();
            expressionAttributeNames["#etag_attr"] = etagAttributeName;

            string conditionExpression;
            if (string.IsNullOrEmpty(expectedETag))
            {
                conditionExpression = "attribute_not_exists(#etag_attr)";
            }
            else
            {
                conditionExpression = "#etag_attr = :etag_expected";
                expressionAttributeValues[":etag_expected"] = new AttributeValue { S = expectedETag };
            }

            // Return the TransactWriteItem for the transaction
            return new TransactWriteItem
            {
                Update = new Update
                {
                    Key = keyAttributeValues, // The item's primary key
                    TableName = _tableName, // DynamoDB table name
                    UpdateExpression = updateExpressionString, // The update expression we just constructed
                    ConditionExpression = conditionExpression, // Optimistic-concurrency guard
                    ExpressionAttributeValues = expressionAttributeValues, // The values for the update
                    ExpressionAttributeNames = expressionAttributeNames, // Attribute names
                }
            };
        }

        public TransactWriteItem GetDeleteTransactWriteItem(TEntity entity)
        {
            var keyAttributeValues = entity.GetKeyAttributeValueDictionary();
            var delete = new Delete
            {
                Key = keyAttributeValues,
                TableName = _tableName
            };

            // Optimistic concurrency: only delete the version this entity was read as.
            if (!string.IsNullOrEmpty(entity.ETag))
            {
                delete.ConditionExpression = "#etag_attr = :etag_expected";
                delete.ExpressionAttributeNames = new Dictionary<string, string> { ["#etag_attr"] = GetETagAttributeName() };
                delete.ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":etag_expected"] = new AttributeValue { S = entity.ETag } };
            }

            return new TransactWriteItem { Delete = delete };
        }

        public async Task<IEnumerable<TEntity>?> ExecuteQueryAsync(QueryExpression<TEntity> queryExpression, CancellationToken cancellationToken = default)
        {
            var result = queryExpression.ShouldUseQueryRequest() ?
               await ExecuteQueryAsync(
                     string.Join(" AND ", queryExpression.FilterExpressions),
                     queryExpression.KeyConditionExpression!,
                     queryExpression.ExpressionAttributeValues,
                     queryExpression.ExpressionAttributeNames,
                     queryExpression.IsScanIndexForward,
                     queryExpression.ProjectionAttributes,
                     queryExpression.Limit,
                     queryExpression.IndexName,
                     cancellationToken) :
                  await ExecuteScanAsync(
                     string.Join(" AND ", queryExpression.FilterExpressions),
                     queryExpression.ExpressionAttributeValues,
                     queryExpression.ExpressionAttributeNames,
                     queryExpression.ProjectionAttributes,
                     queryExpression.Limit,
                     cancellationToken).ConfigureAwait(false);

            return result;
        }
    }
}
