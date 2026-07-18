using AwsShowcase.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Persistence.Common.Configuration;

namespace AwsShowcase.Integration.Persistence;

/// <summary>
/// EF-style configuration adapted to DynamoDB by the library:
/// - ToContainer sets the table name
/// - HasIndex declares a Global Secondary Index, so queries filtering on
///   CustomerEmail run as index Queries instead of full-table Scans.
/// </summary>
public class OrderConfiguration : DocEntityConfiguration<Order>
{
    protected override string ContainerName => "showcase-orders";

    public override void Configure(EntityTypeBuilder<Order> builder)
    {
        base.Configure(builder);

        builder.HasIndex(e => e.CustomerEmail);
    }
}
