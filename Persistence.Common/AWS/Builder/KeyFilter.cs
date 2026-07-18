namespace Persistence.Common.AWS.Builder
{
    public class KeyFilter
    {
        public string PartitionKeyFilter { get; set; }
        public string PrimaryKeyFilter { get; set; }

        public KeyFilter()
        {
            PartitionKeyFilter = string.Empty;
            PrimaryKeyFilter = string.Empty;
        }

        public string GenerateFinalKeyFilter()
        {
            // Handle null or empty filters appropriately
            if (!string.IsNullOrWhiteSpace(PartitionKeyFilter) && !string.IsNullOrWhiteSpace(PrimaryKeyFilter))
            {
                return $"{PartitionKeyFilter} AND {PrimaryKeyFilter}";
            }
            else if (!string.IsNullOrWhiteSpace(PartitionKeyFilter))
            {
                return PartitionKeyFilter;
            }
            else if (!string.IsNullOrWhiteSpace(PrimaryKeyFilter))
            {
                return PrimaryKeyFilter;
            }

            // Return an empty string if both filters are null or empty
            return string.Empty;
        }

        public bool CanUseKeyFilter()
        {
            return !string.IsNullOrWhiteSpace(GenerateFinalKeyFilter()) && PartitionKeyFilter != Constants.DoNotUseKeyExpression;
        }
    }
}
