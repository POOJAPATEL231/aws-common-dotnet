using Domain.Common;
using Domain.Common.Entities;
using System.Linq.Expressions;

namespace Persistence.Common.Repositories
{
    public interface IDbRepository<TEntity> where TEntity : BaseEntity, IAggregateRoot
    {
        #region Specifications

        /// <summary>
        /// The change tracker will not track any of the entities that are returned from
        /// a LINQ query. If the entity instances are modified, this will not be detected
        /// by the change tracker and Microsoft.EntityFrameworkCore.DbContext.SaveChanges
        /// will not persist those changes to the database.
        /// </summary>
        /// <returns>The same IDbRepository implementation so that multiple calls can be chained.</returns>
        IDbRepository<TEntity> AsNoTracking();

        /// <summary>
        /// Specifies related entities to include in the query results. The navigation property
        /// to be included is specified starting with the type of entity being queried (TEntity).
        /// </summary>
        /// <param name="navigationPropertyPath">A lambda expression representing the navigation
        /// property to be included (t => t.Property1).</param>
        /// <returns>The same IDbRepository implementation so that multiple calls can be chained.</returns>
        IDbRepository<TEntity> Include(Expression<Func<TEntity, object?>> navigationPropertyPath);

        /// <summary>
        /// Specifies related entities to include in the query results. The navigation property
        /// to be included is specified starting with the type of entity being queried (TEntity).
        /// Further navigation properties to be included can be appended, separated by the '.' character.
        /// </summary>
        /// <param name="navigationPropertyPath">A string of '.' separated navigation property names to be included.</param>
        /// <returns>The same IDbRepository implementation so that multiple calls can be chained.</returns>
        IDbRepository<TEntity> Include(string navigationPropertyPath);

        #endregion

        #region Find

        /// <summary>
        /// Finds an entity with the given primary key values. If an entity with the given
        /// primary key values is being tracked by the context, then it is returned immediately
        /// without making a request to the database. Otherwise, a query is made to the database
        /// for an entity with the given primary key values and this entity, if found, is
        /// attached to the context and returned. If no entity is found, then null is returned.
        /// </summary>
        /// <param name="keyValues">The values of the primary key for the entity to be found.</param>
        /// <returns>The entity found, or null.</returns>
        Task<TEntity?> FindAsync(params object?[]? keyValues);

        #endregion

        #region Get

        /// <summary>
        /// Gets all entities filtered by a predicate.
        /// </summary>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>List of entities.</returns>
        Task<List<TEntity>> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all entities filtered by a predicate and sorted.
        /// </summary>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="sortKeySelector">Function to extract key to sort by.</param>
        /// <param name="sortDescending">Whether to sort in descending order or not.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>List of entities.</returns>
        Task<List<TEntity>> GetAsync<TKey>(Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default);

        #endregion

        #region GetAll

        /// <summary>
        /// Gets all entities.
        /// </summary>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>List of entities.</returns>
        Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all entities sorted by a key.
        /// </summary>
        /// <param name="sortKeySelector">Function to extract key to sort by.</param>
        /// <param name="sortDescending">Whether to sort in descending order or not.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>Sorted list of entities.</returns>
        Task<List<TEntity>> GetAllAsync<TKey>(Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default);

        #endregion

        #region GetPaged

        /// <summary>
        /// Gets a collection entities with the requested sorting, filtering, and pagination
        /// </summary>
        /// <param name="page">Number of the page to return</param>
        /// <param name="pageSize">Number of items returned per page.</param>
        /// <param name="sortKeySelector">Function to extract key to sort by.</param>
        /// <param name="sortDescending">Whether to sort in descending order or not.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>Paged list of entities.</returns>
        Task<PagedList<TEntity>> GetPagedAsync<TKey>(int page, int pageSize,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all entities filtered by a predicate.
        /// </summary>
        /// <param name="page">Number of the page to return</param>
        /// <param name="pageSize">Number of items returned per page.</param>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="sortKeySelector">Function to extract key to sort by.</param>
        /// <param name="sortDescending">Whether to sort in descending order or not.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>Paged, filtered, and sorted list of entities.</returns>
        Task<PagedList<TEntity>> GetPagedAsync<TKey>(int page, int pageSize, Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default);

        #endregion

        #region Other

        /// <summary>
        /// Gets a single entity filtered by a predicate or null of none are found.
        /// </summary>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>Single Entity.</returns>
        Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a single entity filtered by a predicate or null of none are found.
        /// </summary>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="sortKeySelector">Function to extract key to sort by.</param>
        /// <param name="sortDescending">Whether to sort in descending order or not.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>Single Entity.</returns>
        Task<TEntity?> FirstOrDefaultAsync<TKey>(Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously determines whether any entity satisfies a condition.
        /// </summary>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="cancellationToken">A System.Threading.CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result contains true if any elements in the source sequence pass
        /// the test in the specified predicate; otherwise, false.</returns>
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all tracked entities that have been added or modified
        /// (Must be called before SaveChanges).
        /// </summary>
        /// <returns>List of entities that have been added or modified.</returns>
        List<TEntity> GetModified();

        #endregion

        #region Add

        /// <summary>
        /// Adds an entity to the repository.
        /// </summary>
        /// <param name="entity">Entity to add.</param>
        void Add(TEntity entity);

        /// <summary>
        /// Adds a collection of entities to the repository.
        /// </summary>
        /// <param name="entities">Collection of entities to add.</param>
        void AddRange(IEnumerable<TEntity> entities);

        #endregion

        #region Update

        /// <summary>
        /// Sets an entities state as modified to force it to be updated.
        /// </summary>
        /// <param name="entity">Entity to update in the repository.</param>
        void Update(TEntity entity);

        #endregion

        #region Remove

        /// <summary>
        /// Removes an entity from the repository.
        /// </summary>
        /// <param name="entity">Entity to remove from repository.</param>
        void Remove(TEntity entity);

        /// <summary>
        /// Removes a collection of entities from the repository.
        /// </summary>
        /// <param name="entities">Collection of entities to remove.</param>
        void RemoveRange(IEnumerable<TEntity> entities);

        #endregion
    }
}