namespace Persistence.Common.AWS
{
    /// <summary>
    /// Thrown when a conditional write fails because another writer modified (or created)
    /// the item since it was read - the DynamoDB equivalent of EF Core's
    /// DbUpdateConcurrencyException. Callers should re-read the entity, reapply their
    /// changes, and retry.
    /// </summary>
    public class DynamoDbConcurrencyException : InvalidOperationException
    {
        public DynamoDbConcurrencyException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
