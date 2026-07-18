using Amazon.DynamoDBv2.Model;
using Domain.Common;
using Domain.Common.Entities;

namespace Persistence.Common.AWS
{
    public interface IDynamoDbDocProvider<TEntity> where TEntity : DocEntity
    {
        Task<TEntity?> GetItemAsync(string hash, string? range = null, CancellationToken cancellationToken = default);

        Task<TEntity?> GetItemByIdAsync(string id, CancellationToken cancellationToken = default);

        Task<TEntity?> GetItemByIdAndKeyAsync(string id, string key, CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>?> GetAllItemsAsync(CancellationToken cancellationToken = default);

        Task<TEntity?> GetAnyItemAsync(CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>?> GetItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>?> GetItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            bool isScanIndexForward,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>?> GetItemsByQueryAsync(
            string filterExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default);

        Task<TEntity?> GetItemByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default);

        Task<TEntity?> GetItemByQueryAsync(
            string filterExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default);

        Task<int> CountItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default);

        Task<int> CountItemsByScanAsync(
            string filterExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            CancellationToken cancellationToken = default);

        Task<PagedList<TEntity>> GetPagedItemsAsync(int page, int pageSize, CancellationToken cancellationToken = default);

        Task<PagedList<TEntity>> GetPagedItemsAsync(
            string filterExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            int page, int pageSize, CancellationToken cancellationToken = default);

        Task<PagedList<TEntity>> GetPagedItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            int page, int pageSize, CancellationToken cancellationToken = default);

        Task<PagedList<TEntity>> GetPagedItemsByQueryAsync(
            string filterExpression, string keyConditionExpression,
            Dictionary<string, AttributeValue> filterAttributeValues,
            Dictionary<string, string>? expressionAttributeNames,
            bool isScanIndexForward, int page, int pageSize,
            CancellationToken cancellationToken = default);

        Task CreateItemAsync(TEntity entity, CancellationToken cancellationToken = default);

        Task CreateItemsAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        Task<TEntity> UpdateItemAsync(TEntity entity, CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>> UpdateItemsAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        Task<TEntity> DeleteItemAsync(string hash, string? range = null, CancellationToken cancellationToken = default);

        Task<TEntity> DeleteItemAsync(TEntity entity, CancellationToken cancellationToken = default);

        TransactWriteItem GetAddTransactWriteItem(TEntity entity);

        TransactWriteItem GetUpdateTransactWriteItem(TEntity entity);

        TransactWriteItem GetDeleteTransactWriteItem(TEntity entity);

        Task<IEnumerable<TEntity>?> ExecuteQueryAsync(QueryExpression<TEntity> queryExpression, CancellationToken cancellationToken = default);
    }


}