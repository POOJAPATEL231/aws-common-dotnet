using Domain.Common.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common.Event
{
    public record EntityUpdatedDomainEvent<TEntity> : DomainEvent where TEntity : BaseEntity
    {
        public TEntity? Entity { get; private set; }

        public EntityUpdatedDomainEvent(TEntity? entity)
        {
            Entity = entity;
            SetUserSource(entity?.ModifyUserId, entity?.ModifyUserName, entity?.ModifySource);
        }

        public EntityUpdatedDomainEvent(TEntity? entity, dynamic? eventOrgId)
        {
            Entity = entity;
            SetOrg(eventOrgId);
            SetUserSource(entity?.ModifyUserId, entity?.ModifyUserName, entity?.ModifySource);
        }
    }
}
