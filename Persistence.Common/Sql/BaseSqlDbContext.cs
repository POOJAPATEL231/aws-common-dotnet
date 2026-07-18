using Domain.Common.Dates;
using Domain.Common.Entities;
using Domain.Common.Identity;
using Domain.Common.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Persistence.Common.Sql
{
    /// <summary>
    /// EF Core base context for relational stores (SQL Server, Aurora/RDS, Azure SQL, ...).
    /// Mirrors <see cref="AWS.BaseDynamoDbContext"/>: audit stamping on save and domain
    /// event dispatch via <see cref="SaveEntitiesAsync"/>.
    /// </summary>
    public abstract class BaseSqlDbContext : DbContext, IUnitOfWork
    {
        private readonly ICurrentUser _currentUser;
        private readonly IDateTime _dateTime;
        private readonly IMediator _mediator;

        protected BaseSqlDbContext(DbContextOptions options, ICurrentUser currentUser, IDateTime dateTime, IMediator mediator)
            : base(options)
        {
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            _dateTime = dateTime ?? throw new ArgumentNullException(nameof(dateTime));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditStamps();
            return base.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditStamps();
            await DispatchDomainEventsAsync(cancellationToken);
            await base.SaveChangesAsync(cancellationToken);
            return true;
        }

        private void ApplyAuditStamps()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified))
                {
                    continue;
                }

                var entity = entry.Entity;
                if (entry.State == EntityState.Added)
                {
                    entity.CreateUserId ??= _currentUser.UserId;
                    entity.CreateUserName ??= _currentUser.FullName;
                    entity.CreateSource ??= _currentUser.Source;
                    entity.CreateDateTimeUtc ??= _dateTime.Now;
                }

                entity.ModifyDateTimeUtc = _dateTime.Now;
                entity.ModifyUserId = _currentUser.UserId;
                entity.ModifyUserName = _currentUser.FullName;
                entity.ModifySource = _currentUser.Source;
            }
        }

        private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
        {
            var entitiesWithEvents = ChangeTracker.Entries<BaseEntity>()
                .Select(e => e.Entity)
                .Where(e => e.DomainEvents is { Count: > 0 })
                .ToList();

            var domainEvents = entitiesWithEvents.SelectMany(e => e.DomainEvents!).ToList();
            entitiesWithEvents.ForEach(e => e.ClearDomainEvents());

            foreach (var domainEvent in domainEvents)
            {
                domainEvent.SetUserSource(_currentUser.UserId, _currentUser.FullName, _currentUser.Source);
                await _mediator.Publish(domainEvent, cancellationToken);
            }
        }
    }
}
