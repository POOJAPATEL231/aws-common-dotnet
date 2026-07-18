using Domain.Common;
using Domain.Common.Entities;
using Persistence.Common.AWS.EntityManagement;
using Persistence.Common.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Persistence.Common.AWS.Repositories
{
    public abstract class DynamoDbRepository<TEntity> : IDocRepository<TEntity> where TEntity : DocEntity, IAggregateRoot
    {
        private readonly BaseDynamoDbContext _context;

        protected DynamoDbRepository(BaseDynamoDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        protected IDynamoDbSet<TEntity> Entities => _context.Set<TEntity>();

        private TEntity? GetTrackedEntity(Expression<Func<TEntity, bool>> predicate)
        {
            return _context.GetTrackedEntities<TEntity>()
                .Find(e => predicate.Compile().Invoke(e));
        }

        private TEntity? GetTrackedEntityByKey(params object?[] keyValues)
        {
            if (keyValues == null || keyValues.Length == 0)
            {
                return null;
            }

            // Identify hash and range key properties
            var (hashKeyProperty, _) = DynamoUtils.ResolveHashKeyProperty<TEntity>();
            var (rangeKeyProperty, _) = DynamoUtils.ResolveRangeKeyProperty<TEntity>();

            // Validate number of key values against expected key properties
            if (keyValues.Length == 1 && hashKeyProperty == null)
            {
                throw new ArgumentException("Hash key property is not defined for the entity");
            }

            if (keyValues.Length == 2 && (hashKeyProperty == null || rangeKeyProperty == null))
            {
                throw new ArgumentException("Range key property is not defined for the entity");
            }

            return _context.GetTrackedEntities<TEntity>()
                .Find(entity =>
                {
                    // Check hash key value
                    var entityHashKeyValue = hashKeyProperty?.GetValue(entity);
                    if (!object.Equals(entityHashKeyValue, keyValues[0]))
                    {
                        return false;
                    }

                    // If range key exists, check the range key value
                    if (keyValues.Length == 2)
                    {
                        var entityRangeKeyValue = rangeKeyProperty?.GetValue(entity);
                        if (!object.Equals(entityRangeKeyValue, keyValues[1]))
                        {
                            return false;
                        }
                    }

                    return true;
                });
        }

        public void Add(TEntity entity)
        {
            entity.SetId(GenerateId(entity));
            _context.Add(entity);
        }

        public void AddRange(IEnumerable<TEntity> entities)
        {
            foreach (var entity in entities)
            {
                entity.SetId(GenerateId(entity));
                _context.Add(entity);
            }
        }

        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var count = await Entities.CountAsync(predicate, cancellationToken);
            return count > 0;
        }

        public IDocRepository<TEntity> AsNoTracking()
        {
            // Since DynamoDB doesn't have tracking by default, this is a no-op
            return this;
        }

        IDbRepository<TEntity> IDbRepository<TEntity>.AsNoTracking()
        {
            // Since DynamoDB doesn't have tracking by default, this is a no-op
            return this;
        }

        public async Task<TEntity?> FindAsync(params object?[]? keyValues)
        {
            if (keyValues is not { Length: > 0 })
            {
                throw new ArgumentException("Key values must be provided", nameof(keyValues));
            }

            var trackedEntity = GetTrackedEntityByKey(keyValues);

            if (trackedEntity is not null)
            {
                return trackedEntity;
            }

            var entity = await Entities.FindAsync(keyValues!);

            if (entity is not null)
            {
                return _context.AddOriginalEntity(entity);
            }

            return entity;
        }

        public async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate,
                                                        CancellationToken cancellationToken = default)
        {
            var trackedEntity = GetTrackedEntity(predicate);

            if (trackedEntity is not null)
            {
                return trackedEntity;
            }

            var entity = await Entities.FirstOrDefaultAsync(predicate, cancellationToken: cancellationToken);

            if (entity is not null)
            {
                return _context.AddOriginalEntity(entity);
            }

            return entity;
        }

        public async Task<TEntity?> FirstOrDefaultAsync<TKey>(Expression<Func<TEntity, bool>> predicate,
                                                        Expression<Func<TEntity, TKey>> sortKeySelector,
                                                        bool sortDescending = false, CancellationToken cancellationToken = default)
        {
            var trackedEntity = GetTrackedEntity(predicate);

            if (trackedEntity is not null)
            {
                return trackedEntity;
            }

            var entity = await FirstOrDefaultAsync(predicate, cancellationToken);

            if (entity is not null)
            {
                return _context.AddOriginalEntity(entity);
            }

            return entity;
        }

        public async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var entities = await Entities.GetItemsAsync(cancellationToken: cancellationToken);

            if (entities is { Count: > 0 })
            {
                return _context.AddOriginalEntities(entities).ToList();
            }

            return entities ?? new List<TEntity>();
        }

        public async Task<List<TEntity>> GetAllAsync<TKey>(Expression<Func<TEntity, TKey>> sortKeySelector, bool sortDescending = false, CancellationToken cancellationToken = default)
        {
            List<TEntity> entities = await GetAllAsync(cancellationToken);

            var compiledKeySelector = sortKeySelector.Compile();

            var result = sortDescending
                ? entities?.OrderByDescending(compiledKeySelector)
                : entities?.OrderBy(compiledKeySelector);

            if (entities is { Count: > 0 })
            {
                return _context.AddOriginalEntities(result!).ToList();
            }

            return result?.ToList() ?? new List<TEntity>();
        }

        public async Task<List<TEntity>> GetAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var entities = await Entities.GetItemsAsync(filterExpression: predicate, cancellationToken: cancellationToken);

            if (entities is { Count: > 0 })
            {
                return _context.AddOriginalEntities(entities).ToList();
            }

            return entities ?? new List<TEntity>();
        }

        public async Task<List<TEntity>> GetAsync<TKey>(Expression<Func<TEntity, bool>> predicate,
                                                  Expression<Func<TEntity, TKey>> sortKeySelector,
                                                  bool sortDescending = false, CancellationToken cancellationToken = default)
        {
            List<TEntity> entities = await GetAsync(predicate, cancellationToken);

            var compiledKeySelector = sortKeySelector.Compile();

            var result = sortDescending
                ? entities?.OrderByDescending(compiledKeySelector)
                : entities?.OrderBy(compiledKeySelector);

            if (entities is { Count: > 0 })
            {
                return _context.AddOriginalEntities(result!).ToList();
            }

            return result?.ToList() ?? new List<TEntity>();
        }

        public async Task<TEntity?> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            var trackedEntity = GetTrackedEntityByKey(new object[] { ResolvePartitionKey(id), id });

            if (trackedEntity is not null)
            {
                return trackedEntity;
            }
            var entity = await Entities.FindAsync(new object[] { ResolvePartitionKey(id), id }, cancellationToken: cancellationToken);

            if (entity is not null)
            {
                return _context.AddOriginalEntity(entity);
            }

            return entity;
        }

        public async Task<List<TEntity>> GetAsync(string partitionKey, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var entities = await Entities.GetItemsAsync(partitionKey, predicate, cancellationToken);

            if (entities is { Count: > 0 })
            {
                return _context.AddOriginalEntities(entities).ToList();
            }

            return entities ?? new List<TEntity>();
        }

        public async Task<List<TEntity>> GetAllAsync(string partitionKey, CancellationToken cancellationToken = default)
        {
            var entities = await Entities.GetItemsAsync(partitionKey, cancellationToken);

            if (entities is { Count: > 0 })
            {
                return _context.AddOriginalEntities(entities).ToList();
            }

            return entities ?? new List<TEntity>();
        }

        public List<TEntity> GetModified()
        {
            return _context.GetTrackedEntities<TEntity>(EntityState.Modified);
        }

        public async Task<PagedList<TEntity>> GetPagedAsync<TKey>(
            int page, int pageSize,
            Expression<Func<TEntity, TKey>> sortKeySelector,
            bool sortDescending = false,
            CancellationToken cancellationToken = default)
        {
            var entities = await GetAllAsync(sortKeySelector, sortDescending, cancellationToken);

            if (entities is { Count: > 0 })
            {
                entities = _context.AddOriginalEntities(entities).ToList();
            }

            if (entities is { Count: > 0 })
            {
                return new PagedList<TEntity>
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalRecords = entities.Count,
                    Items = entities.Skip(page * pageSize - pageSize).Take(pageSize).ToList()
                };
            }

            return new PagedList<TEntity>();
        }

        public async Task<PagedList<TEntity>> GetPagedAsync<TKey>(
            int page, int pageSize,
            Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TKey>> sortKeySelector,
            bool sortDescending = false,
            CancellationToken cancellationToken = default)
        {
            var entities = await GetAsync(predicate, sortKeySelector, sortDescending, cancellationToken);

            if (entities is { Count: > 0 })
            {
                entities = _context.AddOriginalEntities(entities).ToList();
            }

            if (entities is { Count: > 0 })
            {
                return new PagedList<TEntity>
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalRecords = entities.Count,
                    Items = entities.Skip(page * pageSize - pageSize).Take(pageSize).ToList()
                };
            }

            return new PagedList<TEntity>();
        }

        public IDocRepository<TEntity> Include(string navigationPropertyPath)
        {
            // No-op as DynamoDB does not support navigation properties
            return this;
        }

        public IDocRepository<TEntity> Include(Expression<Func<TEntity, object?>> navigationPropertyPath)
        {
            // No-op as DynamoDB does not support navigation properties
            return this;
        }

        IDbRepository<TEntity> IDbRepository<TEntity>.Include(Expression<Func<TEntity, object?>> navigationPropertyPath)
        {
            // No-op as DynamoDB does not support navigation properties
            return this;
        }

        IDbRepository<TEntity> IDbRepository<TEntity>.Include(string navigationPropertyPath)
        {
            // No-op as DynamoDB does not support navigation properties
            return this;
        }

        public void Remove(TEntity entity)
        {
            _context.Delete(entity);
        }

        public void RemoveRange(IEnumerable<TEntity> entities)
        {
            foreach (var entity in entities)
            {
                _context.Delete(entity);
            }
        }

        public void Update(TEntity entity)
        {
            _context.Update(entity);
        }

        public string GenerateId(TEntity entity) => DynamoUtils.GenerateId(entity);

        public string ResolvePartitionKey(string entityId) => DynamoUtils.ResolvePartitionKey(entityId);
    }
}
