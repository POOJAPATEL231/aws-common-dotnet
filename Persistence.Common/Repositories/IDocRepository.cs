using Domain.Common.Entities;
using System.Linq.Expressions;

namespace Persistence.Common.Repositories
{
    public interface IDocRepository<TEntity> : IDbRepository<TEntity>
        where TEntity : DocEntity
    {
        /// <summary>
        /// The change tracker will not track any of the entities that are returned from
        /// a LINQ query. If the entity instances are modified, this will not be detected
        /// by the change tracker and Microsoft.EntityFrameworkCore.DbContext.SaveChanges
        /// will not persist those changes to the database.
        /// </summary>
        /// <returns>The same IRepository implementation so that multiple calls can be chained.</returns>
        new IDocRepository<TEntity> AsNoTracking();

        /// <summary>
        /// Specifies related entities to include in the query results. The navigation property
        /// to be included is specified starting with the type of entity being queried (TEntity).
        /// </summary>
        /// <param name="navigationPropertyPath">A lambda expression representing the navigation
        /// property to be included (t => t.Property1).</param>
        /// <returns>The same IRepository implementation so that multiple calls can be chained.</returns>
        new IDocRepository<TEntity> Include(Expression<Func<TEntity, object?>> navigationPropertyPath);

        /// <summary>
        /// Specifies related entities to include in the query results. The navigation property
        /// to be included is specified starting with the type of entity being queried (TEntity).
        /// Further navigation properties to be included can be appended, separated by the '.' character.
        /// </summary>
        /// <param name="navigationPropertyPath">A string of '.' separated navigation property names to be included.</param>
        /// <returns>The same IRepository implementation so that multiple calls can be chained.</returns>
        new IDocRepository<TEntity> Include(string navigationPropertyPath);

        /// <summary>
        /// Gets an entity with the specified Id.
        /// </summary>
        /// <param name="id">Id of entity to get.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>The entity found, or null.</returns>
        Task<TEntity?> GetAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns entities matching the given query
        /// </summary>
        /// <param name="partitionKey">The partition key.</param>
        /// <param name="predicate">The query predicate.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>A list of entities that matched the query predicate.</returns>
        Task<List<TEntity>> GetAsync(string partitionKey, Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all entities with the specified partition key.
        /// </summary>
        /// <param name="partitionKey">The partition key.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>A list of entities.</returns>
        Task<List<TEntity>> GetAllAsync(string partitionKey, CancellationToken cancellationToken = default);
    }
}
