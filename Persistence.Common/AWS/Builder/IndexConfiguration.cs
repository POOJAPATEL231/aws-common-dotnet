namespace Persistence.Common.AWS.Builder
{
    /// <summary>
    /// Configuration of a DynamoDB Global Secondary Index for an entity.
    /// Property names refer to CLR property names; the stored attribute name is
    /// resolved through the property's <see cref="PropertyConfiguration.JsonPropertyName"/>
    /// when one is configured.
    /// </summary>
    public class IndexConfiguration
    {
        public IndexConfiguration(string indexName, string partitionKeyPropertyName, string? sortKeyPropertyName = null)
        {
            if (string.IsNullOrWhiteSpace(indexName))
            {
                throw new ArgumentException("Index name must be provided.", nameof(indexName));
            }

            if (string.IsNullOrWhiteSpace(partitionKeyPropertyName))
            {
                throw new ArgumentException("Index partition key property must be provided.", nameof(partitionKeyPropertyName));
            }

            IndexName = indexName;
            PartitionKeyPropertyName = partitionKeyPropertyName;
            SortKeyPropertyName = sortKeyPropertyName;
        }

        public string IndexName { get; }

        public string PartitionKeyPropertyName { get; }

        public string? SortKeyPropertyName { get; }
    }
}
