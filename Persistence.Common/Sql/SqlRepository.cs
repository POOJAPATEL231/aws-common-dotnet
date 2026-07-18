using Domain.Common;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Persistence.Common.Repositories;
using System.Linq.Expressions;

namespace Persistence.Common.Sql
{
    /// <summary>
    /// EF Core implementation of <see cref="IDbRepository{TEntity}"/> for relational
    /// stores. Works with any EF provider (SQL Server, Npgsql/Aurora, Sqlite, ...).
    /// </summary>
    public class SqlRepository<TEntity> : IDbRepository<TEntity> where TEntity : SqlEntity, IAggregateRoot
    {
        protected readonly DbContext Context;

        private bool _asNoTracking;
        private readonly List<Expression<Func<TEntity, object?>>> _includes = new();
        private readonly List<string> _stringIncludes = new();

        public SqlRepository(DbContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region Specifications

        public IDbRepository<TEntity> AsNoTracking()
        {
            _asNoTracking = true;
            return this;
        }

        public IDbRepository<TEntity> Include(Expression<Func<TEntity, object?>> navigationPropertyPath)
        {
            _includes.Add(navigationPropertyPath);
            return this;
        }

        public IDbRepository<TEntity> Include(string navigationPropertyPath)
        {
            _stringIncludes.Add(navigationPropertyPath);
            return this;
        }

        private IQueryable<TEntity> Query()
        {
            IQueryable<TEntity> query = Context.Set<TEntity>();

            foreach (var include in _includes)
            {
                query = query.Include(include);
            }

            foreach (var include in _stringIncludes)
            {
                query = query.Include(include);
            }

            return _asNoTracking ? query.AsNoTracking() : query;
        }

        #endregion

        public async Task<TEntity?> FindAsync(params object?[]? keyValues)
        {
            return await Context.Set<TEntity>().FindAsync(keyValues);
        }

        public Task<List<TEntity>> GetAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
            => Query().Where(predicate).ToListAsync(cancellationToken);

        public Task<List<TEntity>> GetAsync<TKey>(Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default)
            => Sort(Query().Where(predicate), sortKeySelector, sortDescending).ToListAsync(cancellationToken);

        public Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
            => Query().ToListAsync(cancellationToken);

        public Task<List<TEntity>> GetAllAsync<TKey>(Expression<Func<TEntity, TKey>> sortKeySelector,
            bool sortDescending = false, CancellationToken cancellationToken = default)
            => Sort(Query(), sortKeySelector, sortDescending).ToListAsync(cancellationToken);

        public Task<PagedList<TEntity>> GetPagedAsync<TKey>(int page, int pageSize,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default)
            => GetPagedInternalAsync(Query(), page, pageSize, sortKeySelector, sortDescending, cancellationToken);

        public Task<PagedList<TEntity>> GetPagedAsync<TKey>(int page, int pageSize, Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default)
            => GetPagedInternalAsync(Query().Where(predicate), page, pageSize, sortKeySelector, sortDescending, cancellationToken);

        public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
            => Query().FirstOrDefaultAsync(predicate, cancellationToken);

        public Task<TEntity?> FirstOrDefaultAsync<TKey>(Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default)
            => Sort(Query().Where(predicate), sortKeySelector, sortDescending).FirstOrDefaultAsync(cancellationToken);

        public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
            => Query().AnyAsync(predicate, cancellationToken);

        public List<TEntity> GetModified()
        {
            return Context.ChangeTracker.Entries<TEntity>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified)
                .Select(e => e.Entity)
                .ToList();
        }

        public void Add(TEntity entity) => Context.Set<TEntity>().Add(entity);

        public void AddRange(IEnumerable<TEntity> entities) => Context.Set<TEntity>().AddRange(entities);

        public void Update(TEntity entity) => Context.Entry(entity).State = EntityState.Modified;

        public void Remove(TEntity entity) => Context.Set<TEntity>().Remove(entity);

        public void RemoveRange(IEnumerable<TEntity> entities) => Context.Set<TEntity>().RemoveRange(entities);

        private static IOrderedQueryable<TEntity> Sort<TKey>(IQueryable<TEntity> query,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending)
            => sortDescending ? query.OrderByDescending(sortKeySelector) : query.OrderBy(sortKeySelector);

        private static async Task<PagedList<TEntity>> GetPagedInternalAsync<TKey>(IQueryable<TEntity> query, int page, int pageSize,
            Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending, CancellationToken cancellationToken)
        {
            var totalRecords = await query.CountAsync(cancellationToken);
            var items = await Sort(query, sortKeySelector, sortDescending)
                .Skip(Math.Max(0, (page - 1) * pageSize))
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedList<TEntity>
            {
                Items = items,
                TotalRecords = totalRecords,
                Page = page,
                PageSize = pageSize
            };
        }
    }
}
