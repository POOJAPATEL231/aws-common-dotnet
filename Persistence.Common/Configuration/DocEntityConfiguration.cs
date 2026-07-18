using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Domain.Common.Entities;

namespace Persistence.Common.Configuration
{
    public abstract class DocEntityConfiguration<TBase> : IEntityTypeConfiguration<TBase>
        where TBase : DocEntity
    {
        protected abstract string ContainerName { get; }

        public virtual void Configure(EntityTypeBuilder<TBase> builder)
        {
            if (!string.IsNullOrEmpty(ContainerName))
            {
                builder.ToContainer(ContainerName);
            }

            builder.HasPartitionKey(e => e.PartitionKey);
            builder.HasKey(e => e.Id);
            builder.HasNoDiscriminator();

            builder.Ignore(e => e.DomainEvents);

            builder.Property(e => e.Id)
                .HasColumnOrder(-2)
                .ToJsonProperty("id");

            builder.Property(e => e.IsTest).HasColumnOrder(81);
            builder.Property(e => e.IsDisabled).HasColumnOrder(82);

            builder.Property(e => e.SelfLink)
                .ToJsonProperty("_self")
                .HasColumnOrder(91);

            // builder.Property(e => e.TimeToLive)
            //     .ToJsonProperty("ttl")
            //     .HasColumnOrder(92);

            builder.Property(e => e.ETag)
                .IsETagConcurrency()
                .HasColumnOrder(93);

            builder.Property(e => e.PartitionKey)
                .HasColumnOrder(94);

            builder.Property(e => e.CreateDateTimeUtc).HasColumnOrder(81);
            builder.Property(e => e.CreateUserId).HasColumnOrder(82);
            builder.Property(e => e.CreateUserName).HasColumnOrder(83);
            builder.Property(e => e.CreateSource).HasColumnOrder(84);
            builder.Property(e => e.ModifyDateTimeUtc).HasColumnOrder(85);
            builder.Property(e => e.ModifyUserId).HasColumnOrder(86);
            builder.Property(e => e.ModifyUserName).HasColumnOrder(87);
            builder.Property(e => e.ModifySource).HasColumnOrder(88);
        }
    }
}
