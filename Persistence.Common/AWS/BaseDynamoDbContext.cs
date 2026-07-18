using Amazon.DynamoDBv2.Model;
using Domain.Common.Dates;
using Domain.Common.Entities;
using Domain.Common.Identity;
using Domain.Common.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Persistence.Common.AWS.EntityManagement;
using System.Diagnostics.CodeAnalysis;

namespace Persistence.Common.AWS
{
    public abstract class BaseDynamoDbContext : IUnitOfWork
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ICurrentUser _currentUser;
        private readonly IDateTime _dateTime;
        private readonly IMediator _mediator;
        private readonly Dictionary<Type, IBaseDynamoDbSet> _sets;

        // Keyed by reference identity: BaseEntity overrides Equals/GetHashCode by Id, so two
        // new entities (both with an empty Id) would collide, and mutating the Id after
        // tracking would orphan the entry.
        private readonly Dictionary<object, EntityEntry> _changeTracker = new(ReferenceEqualityComparer.Instance);

        protected BaseDynamoDbContext(IServiceProvider serviceProvider,
            ICurrentUser currentUser,
            IDateTime dateTime,
            IMediator mediator)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            _dateTime = dateTime ?? throw new ArgumentNullException(nameof(dateTime));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _sets = new Dictionary<Type, IBaseDynamoDbSet>();
            InstantiateAllSets();
        }

        [SuppressMessage("Maintainability", "S4261", Justification = "We need a method like this only to match it with EntityFramework DBSet and DbContext code.")]
        public IDynamoDbSet<TEntity> Set<TEntity>() where TEntity : DocEntity
        {
            var entityType = typeof(TEntity);
            if (!_sets.TryGetValue(entityType, out var value))
            {
                var dynamoDbSet = _serviceProvider.GetRequiredService<IDynamoDbSet<TEntity>>();
                _sets[entityType] = dynamoDbSet;
                return dynamoDbSet;
            }

            return (IDynamoDbSet<TEntity>)value;
        }

        internal TEntity TrackEntity<TEntity>(TEntity entity, EntityState state) where TEntity : DocEntity
        {
            if (_changeTracker.TryGetValue(entity, out var entry) && entry.EntityType == typeof(TEntity))
            {
                if (state != EntityState.Unchanged)
                {
                    entry.State = state;
                }
                return (TEntity)entry.Entity;
            }
            else
            {
                _changeTracker[entity] = new EntityEntry(entity, state);
                return entity;
            }
        }

        public void Add<TEntity>(TEntity entity) where TEntity : DocEntity
        {
            TrackEntity(entity, EntityState.Added);
        }

        public void Update<TEntity>(TEntity entity) where TEntity : DocEntity
        {
            TrackEntity(entity, EntityState.Modified);
        }

        public void Delete<TEntity>(TEntity entity) where TEntity : DocEntity
        {
            TrackEntity(entity, EntityState.Deleted);
        }

        public TEntity AddOriginalEntity<TEntity>(TEntity entity) where TEntity : DocEntity
        {
            return TrackEntity(entity, EntityState.Unchanged);
        }

        public IEnumerable<TEntity> AddOriginalEntities<TEntity>(IEnumerable<TEntity> entities) where TEntity : DocEntity
        {
            // Materialize eagerly - with lazy yield-return, a caller that ignores the
            // return value would never actually track the entities.
            var tracked = new List<TEntity>();
            foreach (var entry in entities)
            {
                tracked.Add(TrackEntity(entry, EntityState.Unchanged));
            }

            return tracked;
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var result = await SaveChangesInternalAsync(cancellationToken);

            UpdateChangeTracker();

            return result;
        }

        public async Task<int> SaveChangesInternalAsync(CancellationToken cancellationToken = default)
        {
            TrackEntityChanges();
            var transactWriteItems = new List<TransactWriteItem>();

            foreach (var entry in _changeTracker.Values.Where(e => e.State != EntityState.Unchanged))
            {
                var entityType = entry.EntityType;
                TransactWriteItem? transactionItem = null;

                if (_sets.TryGetValue(entityType, out var set))
                {
                    transactionItem = entry.State switch
                    {
                        EntityState.Added => set.GetAddTransactionItem(entry.Entity),
                        EntityState.Modified => set.GetUpdateTransactionItem(entry.Entity),
                        EntityState.Deleted => set.GetDeleteTransactionItem(entry.Entity),
                        _ => null
                    };

                    if (transactionItem != null)
                    {
                        transactWriteItems.Add(transactionItem);
                    }
                }
                else
                {
                    throw new NotSupportedException($"DynamoDbSet is not created for {entityType}.");
                }
            }

            if (transactWriteItems is { Count: > 0 })
            {
                await _sets.Values.First().ExecuteTransactionAsync(transactWriteItems, cancellationToken);
            }

            return transactWriteItems.Count;
        }

        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            await SaveChangesInternalAsync(cancellationToken);

            await _mediator.DispatchDomainEventsAsync(_changeTracker, _currentUser, cancellationToken);

            UpdateChangeTracker();

            return true;
        }

        internal List<TEntity> GetTrackedEntities<TEntity>() where TEntity : DocEntity
        {
            return _changeTracker
                .Where(entry => entry.Value.Entity is TEntity)
                .Select(entry => (TEntity)entry.Value.Entity)
                .ToList();
        }

        internal List<TEntity> GetTrackedEntities<TEntity>(EntityState state) where TEntity : DocEntity
        {
            return _changeTracker
                .Where(entry => entry.Value.Entity is TEntity && entry.Value.State == state)
                .Select(entry => (TEntity)entry.Value.Entity)
                .ToList();
        }

        internal void TrackEntityChanges()
        {
            foreach (var entry in _changeTracker.Values.Where(x => x.Entity is BaseEntity))
            {
                if (entry.State == EntityState.Unchanged && entry.HasChanges())
                {
                    entry.State = EntityState.Modified;
                }

                if (entry.State == EntityState.Unchanged)
                {
                    continue;
                }

                if (entry.Entity is BaseEntity baseEntity && (entry.State == EntityState.Added || entry.State == EntityState.Modified))
                {
                    if (!baseEntity.CreateUserId.HasValue)
                    {
                        ((BaseEntity)entry.Entity).CreateUserId = _currentUser.UserId;
                    }
                    if (string.IsNullOrEmpty(baseEntity.CreateUserName))
                    {
                        ((BaseEntity)entry.Entity).CreateUserName = _currentUser.FullName;
                    }
                    if (string.IsNullOrEmpty(baseEntity.CreateSource))
                    {
                        ((BaseEntity)entry.Entity).CreateSource = _currentUser.Source;
                    }
                    if (!baseEntity.CreateDateTimeUtc.HasValue)
                    {
                        ((BaseEntity)entry.Entity).CreateDateTimeUtc = _dateTime.Now;
                    }

                    ((BaseEntity)entry.Entity).ModifyDateTimeUtc = _dateTime.Now;
                    ((BaseEntity)entry.Entity).ModifyUserId = _currentUser.UserId;
                    ((BaseEntity)entry.Entity).ModifyUserName = _currentUser.FullName;
                    ((BaseEntity)entry.Entity).ModifySource = _currentUser.Source;
                }
            }
        }

        public void InstantiateAllSets()
        {
            var setProperties = this.GetType()
                .GetProperties()
                .Where(p => p.PropertyType.IsGenericType
                            && p.PropertyType.GetGenericTypeDefinition() == typeof(IDynamoDbSet<>));

            foreach (var property in setProperties)
            {
                var entityType = property.PropertyType.GenericTypeArguments[0];
                var setMethod = this.GetType().GetMethod(nameof(Set))?.MakeGenericMethod(entityType);

                setMethod?.Invoke(this, null);
            }
        }

        private void UpdateChangeTracker()
        {
            // Detach deleted entities entirely - keeping them tracked would let a later
            // mutation resurrect them as Modified writes.
            var deletedKeys = _changeTracker
                .Where(e => e.Value.State == EntityState.Deleted)
                .Select(e => e.Key)
                .ToList();

            foreach (var key in deletedKeys)
            {
                _changeTracker.Remove(key);
            }

            _changeTracker
            .Where(e => e.Value.State != EntityState.Unchanged)
            .ToList()
            .ForEach(e => e.Value.ResetAfterSave());
        }

        #region IDisposable Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }
            _changeTracker.Clear();
        }

        #endregion
    }
}
