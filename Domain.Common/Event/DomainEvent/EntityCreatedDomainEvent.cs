using Domain.Common.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common.Event
{
    public record EntityCreatedDomainEvent<TEntity> : DomainEvent where TEntity : BaseEntity
    {
        public TEntity? Entity { get; private set; }

        public EntityCreatedDomainEvent(TEntity? entity)
        {
            Entity = entity;
            SetUserSource(entity?.CreateUserId, entity?.CreateUserName, entity?.CreateSource);
        }

        public EntityCreatedDomainEvent(TEntity? entity, dynamic? eventOrgId)
        {
            Entity = entity;
            SetOrg(eventOrgId);
            SetUserSource(entity?.CreateUserId, entity?.CreateUserName, entity?.CreateSource);
        }
    }
}
