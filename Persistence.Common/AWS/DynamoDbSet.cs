using Amazon.DynamoDBv2.Model;
using Domain.Common.Entities;
using Domain.Common;
using Persistence.Common.AWS.Builder;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Persistence.Common.AWS
{
    public class DynamoDbSet<TEntity> : IDynamoDbSet<TEntity> where TEntity : DocEntity
    {
        private readonly IDynamoDbDocProvider<TEntity> _docProvider;

        private readonly IDynamoDbTransactionExecutor _transactionExecutor;

        private Expression _expression;

        private IQueryProvider? _provider;

        public Type ElementType => typeof(TEntity);
        public Expression Expression => _expression;
        public IQueryProvider Provider => _provider ??= new DynamoDbQueryProvider<TEntity>(_docProvider, _transactionExecutor);

        public DynamoDbSet(IDynamoDbDocProvider<TEntity> docProvider, IDynamoDbTransactionExecutor transactionExecutor)
        {
            _docProvider = docProvider;
            _transactionExecutor = transactionExecutor;
            _expression = Expression.Constant(null);
            InitializeExpression();
        }

        public DynamoDbSet(IDynamoDbDocProvider<TEntity> docProvider, IDynamoDbTransactionExecutor transactionExecutor, Expression expression)
        {
            _docProvider = docProvider;
            _expression = expression;
            _transactionExecutor = transactionExecutor;
        }

        private void InitializeExpression()
        {
            _expression = Expression.Constant(this);
        }

        #region Add Methods
        public async Task AddAsync(object entity, CancellationToken cancellationToken)
        {
            await AddAsync((TEntity)entity, cancellationToken);
        }

        public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _docProvider.CreateItemAsync(entity, cancellationToken);
            return entity;
        }

        public async Task AddRangeAsync(params TEntity[] entities)
        {
            await _docProvider.CreateItemsAsync(entities, CancellationToken.None);
        }

        public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await _docProvider.CreateItemsAsync(entities, cancellationToken);
        }
        #endregion

        #region Update Methods

        public async Task UpdateAsync(object entity, CancellationToken cancellationToken)
        {
            await UpdateAsync((TEntity)entity, cancellationToken);
        }

        public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _docProvider.UpdateItemAsync(entity, cancellationToken);
            return entity;
        }

        public async Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken)
        {
            await _docProvider.UpdateItemsAsync(entities, cancellationToken);
        }

        #endregion

        #region Remove Methods

        public async Task RemoveAsync(object entity, CancellationToken cancellationToken)
        {
            await RemoveAsync((TEntity)entity, cancellationToken);
        }

        public async Task<TEntity> RemoveAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _docProvider.DeleteItemAsync(entity, cancellationToken);
            return entity;
        }

        public async Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken)
        {
            foreach (var entity in entities)
            {
                await _docProvider.DeleteItemAsync(entity, cancellationToken);
            }
        }

        #endregion

        #region Find Methods

        public async Task<TEntity?> FindAsync(params object[] keyValues)
        {
            return await FindAsync(keyValues, CancellationToken.None);
        }

        public async Task<TEntity?> FindAsync(object[] keyValues, CancellationToken cancellationToken)
        {
            if (keyValues is not { Length: > 0 })
            {
                throw new ArgumentException("At least the hash key value must be provided.", nameof(keyValues));
            }

            var hashKey = keyValues[0]?.ToString();

            if (string.IsNullOrWhiteSpace(hashKey))
            {
                return null;
            }

            var rangeKey = keyValues.Length > 1 ? keyValues[1].ToString() : null;

            return await _docProvider.GetItemAsync(hashKey, rangeKey, cancellationToken);
        }

        #endregion

        #region Generate TransactWriteItem

        // Generate TransactWriteItem for Add
        public TransactWriteItem GetAddTransactionItem(object entity)
        {
            return _docProvider.GetAddTransactWriteItem((TEntity)entity);
        }

        // Generate TransactWriteItem for Update
        public TransactWriteItem GetUpdateTransactionItem(object entity)
        {
            return _docProvider.GetUpdateTransactWriteItem((TEntity)entity);
        }

        // Generate TransactWriteItem for Delete
        public TransactWriteItem GetDeleteTransactionItem(object entity)
        {
            return _docProvider.GetDeleteTransactWriteItem((TEntity)entity);
        }

        public async Task ExecuteTransactionAsync(List<TransactWriteItem> transactionItems, CancellationToken cancellationToken = default)
        {
            await _transactionExecutor.ExecuteTransactionAsync(transactionItems, cancellationToken);
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// True when this set instance carries a composed LINQ expression (e.g. from Where(...))
        /// rather than being the plain root set.
        /// </summary>
        private bool HasStoredExpression => _expression is not ConstantExpression;

        /// <summary>
        /// Executes the LINQ expression stored on this set (built via the IQueryable surface)
        /// so parameterless terminal operators still honor prior Where(...) calls.
        /// </summary>
        private async Task<IEnumerable<TEntity>?> QueryByStoredExpressionAsync(CancellationToken cancellationToken)
        {
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>();
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.BuildFromExpression(_expression);

            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                return await _docProvider.GetItemsByQueryAsync(filterExpression,
                    keyConditionExpression, expressionAttributeValues,
                    expressionAttributeNames, cancellationToken);
            }

            return await _docProvider.GetItemsByQueryAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
        }

        public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            if (HasStoredExpression)
            {
                var items = await QueryByStoredExpressionAsync(cancellationToken);
                return items is null ? default : items.FirstOrDefault();
            }

            return await _docProvider.GetAnyItemAsync(cancellationToken);
        }

        public async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>().WithExpression(predicate);
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.Build();
            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                return await _docProvider.GetItemByQueryAsync(filterExpression,
                 keyConditionExpression, expressionAttributeValues,
                  expressionAttributeNames, cancellationToken);
            }
            return await _docProvider.GetItemByQueryAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
        }

        public async Task<TEntity?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            if (HasStoredExpression)
            {
                var items = await QueryByStoredExpressionAsync(cancellationToken);
                return items is null ? default : items.SingleOrDefault();
            }

            return await _docProvider.GetAnyItemAsync(cancellationToken);
        }

        public async Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>().WithExpression(predicate);
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.Build();

            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                var itemsByKey = await _docProvider.GetItemsByQueryAsync(filterExpression,
                 keyConditionExpression, expressionAttributeValues,
                  expressionAttributeNames, cancellationToken);
                return itemsByKey?.SingleOrDefault();
            }

            var items = await _docProvider.GetItemsByQueryAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
            return items?.SingleOrDefault();
        }

        public async Task<TEntity?> FirstAsync(CancellationToken cancellationToken = default)
        {
            if (HasStoredExpression)
            {
                var items = await QueryByStoredExpressionAsync(cancellationToken);
                return items is null ? default : items.First();
            }

            return await _docProvider.GetAnyItemAsync(cancellationToken);
        }

        public async Task<TEntity?> FirstAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>().WithExpression(predicate);
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.Build();

            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                var itemsByKey = await _docProvider.GetItemsByQueryAsync(filterExpression,
                keyConditionExpression, expressionAttributeValues,
                expressionAttributeNames, cancellationToken);
                return itemsByKey?.First();
            }

            var items = await _docProvider.GetItemsByQueryAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
            return items?.First();
        }

        public async Task<TEntity?> SingleAsync(CancellationToken cancellationToken = default)
        {
            if (HasStoredExpression)
            {
                var items = await QueryByStoredExpressionAsync(cancellationToken);
                return items is null ? default : items.Single();
            }

            return await _docProvider.GetAnyItemAsync(cancellationToken);
        }

        public async Task<TEntity?> SingleAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>().WithExpression(predicate);
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.Build();

            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                var itemsByKey = await _docProvider.GetItemsByQueryAsync(filterExpression,
                keyConditionExpression, expressionAttributeValues,
                expressionAttributeNames, cancellationToken);
                return itemsByKey?.Single();
            }

            var items = await _docProvider.GetItemsByQueryAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
            return items?.Single();
        }

        public async Task<List<TEntity>?> ToListAsync(CancellationToken cancellationToken = default)
        {
            var items = await _docProvider.GetAllItemsAsync(cancellationToken);
            return items?.ToList();
        }

        public async Task<PagedList<TEntity>> ToPagedListAsync(Expression<Func<TEntity, bool>> predicate, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>().WithExpression(predicate);
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.Build();

            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                return await _docProvider.GetPagedItemsByQueryAsync(filterExpression,
                keyConditionExpression, expressionAttributeValues,
                expressionAttributeNames, page, pageSize, cancellationToken);
            }

            return await _docProvider.GetPagedItemsAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, page, pageSize, cancellationToken);
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            return await _docProvider.CountItemsByScanAsync(string.Empty, new Dictionary<string, AttributeValue>(), new Dictionary<string, string>(), cancellationToken);
        }

        public async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>().WithExpression(predicate);
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.Build();

            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                return await _docProvider.CountItemsByQueryAsync(filterExpression,
                keyConditionExpression, expressionAttributeValues,
                expressionAttributeNames, cancellationToken);
            }

            return await _docProvider.CountItemsByScanAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
        }

        #endregion

        public async Task<List<TEntity>> GetItemsAsync(string partitionKey, Expression<Func<TEntity, bool>> predicate,
                CancellationToken cancellationToken = default)
        {
            var keyFilter = DynamoUtils.CreateKeyFilter<TEntity>(partitionKey);
            var queryBuilder = new DynamoDbQueryBuilder<TEntity>().WithExpression(predicate);
            var (keyConditionExpression, filterExpression, expressionAttributeValues, expressionAttributeNames) = queryBuilder.Build();

            if (keyConditionExpression is not null && keyConditionExpression.CanUseKeyFilter())
            {
                if (string.IsNullOrWhiteSpace(keyConditionExpression.PartitionKeyFilter) && !string.IsNullOrWhiteSpace(keyConditionExpression.PrimaryKeyFilter))
                {
                    expressionAttributeValues = expressionAttributeValues.Union(keyFilter.ExpressionAttributeValues).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    var keyExpression = $"{keyFilter.KeyConditionExpression} And {keyConditionExpression.PrimaryKeyFilter}";
                    var itemsByKey = await _docProvider.GetItemsByQueryAsync(filterExpression, keyExpression,
                    expressionAttributeValues, expressionAttributeNames, cancellationToken);
                    return itemsByKey?.ToList() ?? new List<TEntity>();
                }
                else
                {
                    var itemsByKey = await _docProvider.GetItemsByQueryAsync(filterExpression,
                     keyConditionExpression, expressionAttributeValues,
                     expressionAttributeNames, cancellationToken);
                    return itemsByKey?.ToList() ?? new List<TEntity>();
                }
            }
            else if (keyConditionExpression is not null && keyConditionExpression.PartitionKeyFilter == Constants.DoNotUseKeyExpression)
            {
                var itemsByFilter = await _docProvider.GetItemsByQueryAsync(filterExpression, expressionAttributeValues, expressionAttributeNames, cancellationToken);
                return itemsByFilter?.ToList() ?? new List<TEntity>();
            }

            expressionAttributeValues = expressionAttributeValues.Union(keyFilter.ExpressionAttributeValues).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var items = await _docProvider.GetItemsByQueryAsync(filterExpression,
             keyFilter.KeyConditionExpression, expressionAttributeValues,
             expressionAttributeNames, cancellationToken);
            return items?.ToList() ?? new List<TEntity>();
        }

        public async Task<List<TEntity>> GetItemsAsync(string partitionKey, CancellationToken cancellationToken = default)
        {
            var keyFilter = DynamoUtils.CreateKeyFilter<TEntity>(partitionKey);
            var items = await _docProvider.GetItemsByQueryAsync(string.Empty,
             keyFilter.KeyConditionExpression, keyFilter.ExpressionAttributeValues,
              new Dictionary<string, string>(), cancellationToken);
            return items?.ToList() ?? new List<TEntity>();
        }

        public async Task<List<TEntity>> GetItemsAsync(
           Expression<Func<TEntity, bool>>? filterExpression = null,
           bool sortDescending = false,
           int? limit = null,
           CancellationToken cancellationToken = default)
        {
            var queryExpression = new QueryExpression<TEntity>();

            if (filterExpression != null)
            {
                queryExpression.AddFilterExpression(filterExpression);
            }

            if (limit.HasValue)
            {
                queryExpression.PagingLimit(limit.Value);
            }

            var results = await _docProvider.ExecuteQueryAsync(queryExpression, cancellationToken);
            return results?.ToList() ?? new List<TEntity>();
        }

        public async Task<List<TEntity>?> ExecuteQueryAsync(QueryExpression<TEntity> queryExpression, CancellationToken cancellationToken = default)
        {
            var results = await _docProvider.ExecuteQueryAsync(queryExpression, cancellationToken);
            return results?.ToList() ?? new List<TEntity>();
        }

        public IEnumerator<TEntity> GetEnumerator()
        {
            // LINQ-to-Objects operations use this.
            return Provider.Execute<IEnumerable<TEntity>>(Expression).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            // Asynchronous iteration
            return Provider.Execute<IAsyncEnumerable<TEntity>>(Expression).GetAsyncEnumerator(cancellationToken);
        }
    }
}
