using Amazon.DynamoDBv2.Model;
using Domain.Common.Entities;
using Domain.Common.Extensions;
using Persistence.Common.AWS.Builder;
using System.Linq.Expressions;

namespace Persistence.Common.AWS
{
    public class QueryExpression<TEntity> where TEntity : DocEntity
    {
        public string? KeyConditionExpression { get; private set; }

        public List<string> FilterExpressions { get; } = new List<string>();

        public Dictionary<string, AttributeValue> ExpressionAttributeValues { get; private set; } = new Dictionary<string, AttributeValue>();

        public Dictionary<string, string> ExpressionAttributeNames { get; private set; } = new Dictionary<string, string>();

        public bool IsScanIndexForward { get; private set; } = true; // True by default for ascending order

        /// <summary>Global Secondary Index the query should target; null for the base table.</summary>
        public string? IndexName { get; private set; }

        public int? Limit { get; private set; }

        public List<string>? ProjectionAttributes { get; private set; }

        // Method to add a partition key condition
        public QueryExpression<TEntity> WithPartitionKey(string partitionKey, string partitionKeyValue)
        {
            KeyConditionExpression = $"{partitionKey} = :partitionKeyValue";
            ExpressionAttributeValues[":partitionKeyValue"] = new AttributeValue { S = partitionKeyValue };
            return this;
        }

        // Method to target a Global Secondary Index explicitly
        public QueryExpression<TEntity> WithIndex(string indexName)
        {
            IndexName = indexName;
            return this;
        }

        // Method to set projection attributes
        public QueryExpression<TEntity> WithProjectionAttributes(params string[] projectionAttributes)
        {
            ProjectionAttributes = projectionAttributes.ToList();
            return this;
        }

        public QueryExpression<TEntity> AddExpressionAttributeNames(Dictionary<string, string> aliases)
        {
            if (aliases is not null && aliases.Count > 0)
            {
                ExpressionAttributeNames.AddRange(aliases);
            }

            return this;
        }

        // Method to add filter expressions with Dictionary<string, object>
        public QueryExpression<TEntity> AddFilterExpression(string filterExpression, Dictionary<string, object> args, Dictionary<string, string>? aliases = null)
        {
            // Combine filter expressions with AND
            if (string.IsNullOrEmpty(FilterExpressions.FirstOrDefault()))
            {
                FilterExpressions.Add(filterExpression);
            }
            else
            {
                FilterExpressions[0] += $" AND {filterExpression}";
            }

            // Convert the Dictionary<string, object> to Dictionary<string, AttributeValue>
            foreach (var kvp in args)
            {
                ExpressionAttributeValues[kvp.Key] = ToAttributeValue(kvp.Value);
            }

            if (aliases is not null && aliases.Count > 0)
            {
                ExpressionAttributeNames.AddRange(aliases);
            }

            return this;
        }

        // Method to add filter expressions with lambda expression
        public QueryExpression<TEntity> AddFilterExpression(Expression<Func<TEntity, bool>> filterExpression)
        {
            // Use DynamoDbQueryBuilder to parse the expression
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>();
            queryBuilder.WithExpression(filterExpression);

            // Build the filter expression and attribute values
            var (keyConditionExpr, filterExpr, attributeValues, aliases) = queryBuilder.Build();

            if (keyConditionExpr is not null && keyConditionExpr.CanUseKeyFilter())
            {
                // Carry over a GSI target detected during predicate translation.
                if (!string.IsNullOrWhiteSpace(keyConditionExpr.IndexName) && string.IsNullOrWhiteSpace(IndexName))
                {
                    IndexName = keyConditionExpr.IndexName;
                }

                if (string.IsNullOrWhiteSpace(KeyConditionExpression))
                {
                    KeyConditionExpression = keyConditionExpr.GenerateFinalKeyFilter();
                }
                else if (string.IsNullOrWhiteSpace(keyConditionExpr.PartitionKeyFilter) && !string.IsNullOrWhiteSpace(keyConditionExpr.PrimaryKeyFilter))
                {
                    KeyConditionExpression = $"{KeyConditionExpression} AND {keyConditionExpr.PrimaryKeyFilter}";
                }
                else
                {
                    KeyConditionExpression = keyConditionExpr.GenerateFinalKeyFilter();
                }
            }

            // Combine the generated filter expression with existing ones
            if (string.IsNullOrWhiteSpace(FilterExpressions.FirstOrDefault()))
            {
                FilterExpressions.Add(filterExpr);
            }
            else
            {
                FilterExpressions[0] += $" AND {filterExpr}";
            }

            // Merge the generated attribute values with existing ones
            foreach (var kvp in attributeValues)
            {
                ExpressionAttributeValues[kvp.Key] = kvp.Value;
            }

            if (aliases is not null && aliases.Count > 0)
            {
                ExpressionAttributeNames.AddRange(aliases);
            }

            return this;
        }

        // Helper method to convert object to AttributeValue
        private AttributeValue ToAttributeValue(object value)
        {
            if (value == null)
            {
                return new AttributeValue { NULL = true };
            }

            // Numeric values must use invariant culture: DynamoDB "N" values require '.' as
            // the decimal separator regardless of the host machine's culture.
            return value switch
            {
                string s => new AttributeValue { S = s },
                int i => new AttributeValue { N = i.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                long l => new AttributeValue { N = l.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                double d => new AttributeValue { N = d.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                float f => new AttributeValue { N = f.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                decimal dec => new AttributeValue { N = dec.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                bool b => new AttributeValue { BOOL = b },
                byte[] bArr => new AttributeValue { B = new MemoryStream(bArr) },
                MemoryStream ms => new AttributeValue { B = ms },
                DateTime dt => new AttributeValue { S = dt.ToString("o") }, // ISO 8601 format for DynamoDB
                DateTimeOffset dto => new AttributeValue { S = dto.ToString("o") },
                List<string> ss => new AttributeValue { SS = ss },
                List<int> ns => new AttributeValue { NS = ns.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList() },
                List<long> nsl => new AttributeValue { NS = nsl.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList() },
                List<double> nsd => new AttributeValue { NS = nsd.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList() },
                List<float> nsf => new AttributeValue { NS = nsf.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList() },
                List<decimal> nsdec => new AttributeValue { NS = nsdec.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList() },
                List<object> listObj => new AttributeValue { L = listObj.Select(ToAttributeValue).ToList() }, // Handles List<object>
                Dictionary<string, object> dict => new AttributeValue { M = dict.ToDictionary(kvp => kvp.Key, kvp => ToAttributeValue(kvp.Value)) }, // Nested dictionary
                _ => new AttributeValue { S = value.ToString() } // Fallback for unsupported types
            };
        }

        // Method to set order by ascending
        public QueryExpression<TEntity> OrderByAscending()
        {
            IsScanIndexForward = true; // Ascending order
            return this;
        }

        // Method to set order by descending
        public QueryExpression<TEntity> OrderByDescending()
        {
            IsScanIndexForward = false; // Descending order
            return this;
        }

        // Method to set paging limit
        public QueryExpression<TEntity> PagingLimit(int limit)
        {
            Limit = limit;
            return this;
        }

        // Method to determine if a QueryRequest should be used instead of a ScanRequest
        public bool ShouldUseQueryRequest()
        {
            return !string.IsNullOrEmpty(KeyConditionExpression);
        }
    }
}