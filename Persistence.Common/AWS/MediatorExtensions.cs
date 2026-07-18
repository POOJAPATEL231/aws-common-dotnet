using Domain.Common.Entities;
using Domain.Common.Identity;
using MediatR;
using Persistence.Common.AWS.EntityManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Persistence.Common.AWS
{
    internal static class MediatorExtensions
    {
        internal static async Task DispatchDomainEventsAsync(this IPublisher publisher,
            Dictionary<object, EntityEntry> changeTracker, ICurrentUser user, CancellationToken cancellationToken = default)
        {
            var domainEntities = changeTracker
                .Values
                 .Where(x => x.State != EntityState.Unchanged && x.Entity is BaseEntity baseEntity && baseEntity.DomainEvents?.Count > 0)
                  .Select(x => (BaseEntity)x.Entity)
                  .ToList();

            var domainEvents = domainEntities
                .SelectMany(x => x.DomainEvents!)
                .ToList();

            domainEntities.ToList()
                .ForEach(entity => entity.ClearDomainEvents());

            foreach (var domainEvent in domainEvents)
            {
                domainEvent.SetUserSource(user.UserId, user.FullName, user.Source);
                await publisher.Publish(domainEvent, cancellationToken);
            }
        }
    }
}
