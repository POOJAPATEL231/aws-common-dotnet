using Domain.Common;
using Domain.Common.Entities;
using System.Linq.Expressions;

namespace Persistence.Common.AWS
{
    public interface IDynamoDbSet<TEntity> : IBaseDynamoDbSet, IAsyncEnumerable<TEntity>, IOrderedQueryable<TEntity> where TEntity : DocEntity
    {
        // Add Methods
        Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

        Task AddRangeAsync(params TEntity[] entities);

        Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

        // Update Methods
        Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

        Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken);

        // Remove Methods
        Task<TEntity> RemoveAsync(TEntity entity, CancellationToken cancellationToken = default);

        Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken);

        // Find Methods
        Task<TEntity?> FindAsync(params object[] keyValues);

        Task<TEntity?> FindAsync(object[] keyValues, CancellationToken cancellationToken);

        // Query Methods
        Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

        Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<TEntity?> SingleOrDefaultAsync(CancellationToken cancellationToken = default);

        Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<TEntity?> FirstAsync(CancellationToken cancellationToken = default);

        Task<TEntity?> FirstAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<TEntity?> SingleAsync(CancellationToken cancellationToken = default);

        Task<TEntity?> SingleAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<List<TEntity>?> ToListAsync(CancellationToken cancellationToken = default);

        Task<PagedList<TEntity>> ToPagedListAsync(Expression<Func<TEntity, bool>> predicate, int page, int pageSize, CancellationToken cancellationToken = default);

        Task<int> CountAsync(CancellationToken cancellationToken = default);

        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<List<TEntity>> GetItemsAsync(
           Expression<Func<TEntity, bool>>? filterExpression = null,
           bool sortDescending = false,
           int? limit = null,
           CancellationToken cancellationToken = default);

        Task<List<TEntity>> GetItemsAsync(string partitionKey, Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

        Task<List<TEntity>> GetItemsAsync(string partitionKey, CancellationToken cancellationToken = default);

        Task<List<TEntity>?> ExecuteQueryAsync(QueryExpression<TEntity> queryExpression, CancellationToken cancellationToken = default);
    }
}