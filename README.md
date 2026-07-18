# AWS Common Libraries for .NET

Reusable building blocks for AWS-based .NET services — including an **EF Core-style
DynamoDB layer** (DbContext/DbSet, LINQ predicates, change tracking) that AWS doesn't
ship, plus an SNS/SQS event bus, Redis/distributed caching, S3 file storage, Cognito
auth helpers, and KMS-backed crypto utilities.

> Built with clean architecture layering so applications depend only on abstractions;
> AWS-specific implementations stay in the infrastructure/persistence layers.

## Packages

| Project | What it provides |
|---|---|
| **Domain.Common** | Base entities (`DocEntity`, `BaseEntity`), domain/integration events, settings records — zero AWS dependencies |
| **Application.Common** | Abstractions: `ICache`, `IFileProvider`, event-bus subscription managers, identity helpers |
| **Utils.Common** | Crypto (AES, PBKDF2 password hashing, secret wrapping), JSON/byte/string utilities |
| **Infrastructure.Common** | AWS implementations: SNS/SQS event bus, Redis + distributed cache, S3 file provider, Cognito HTTP client, X-Ray middleware, CloudWatch/Serilog logging, feature flags |
| **Persistence.Common** | **DynamoDB EF-style ORM**: `BaseDynamoDbContext`, `IDynamoDbSet<T>`, LINQ→DynamoDB expression translation, change tracking, transactional `SaveChangesAsync`, table auto-creation, repository base |

## Quick start: DynamoDB the EF way

```csharp
// 1. Define an entity
public class Order : DocEntity, IAggregateRoot
{
    public override string PartitionKey { get; set; } = "order";
    public string CustomerName { get; set; } = "";
    public int Quantity { get; set; }
}

// 2. Define a context (like a DbContext)
public class ShopContext : BaseDynamoDbContext
{
    public ShopContext(IServiceProvider sp, ICurrentUser user, IDateTime clock, IMediator mediator)
        : base(sp, user, clock, mediator) { }

    public IDynamoDbSet<Order> Orders => Set<Order>();
}

// 3. Register in Program.cs
builder.Services.AddPersistenceDynamoDb<ShopContext>(builder.Configuration, builder.Environment);

// 4. Use it
var order = await context.Orders.FirstOrDefaultAsync(o => o.CustomerName == "alice");
order.Quantity += 1;                      // change tracking picks this up
await context.SaveChangesAsync();         // atomic TransactWriteItems
```

Supported LINQ predicate features: `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`,
`StartsWith`, `Contains`, list-`Contains` (→ `IN`), `Between`, null checks
(→ `attribute_exists`/`attribute_not_exists`), reserved-word aliasing, and
automatic Query-vs-Scan selection when a key condition is detected.

## Building

```bash
dotnet build   # builds all libraries + samples
dotnet test    # runs the library test suite (Common.Tests) + sample tests
```

Requires the .NET 8 SDK.

## Repository layout

```
├── Domain.Common/           # domain layer (no AWS deps)
├── Application.Common/      # application abstractions
├── Utils.Common/            # cross-cutting utilities
├── Infrastructure.Common/   # AWS infrastructure implementations
├── Persistence.Common/      # DynamoDB EF-style persistence
├── Common.Tests/            # unit tests for the libraries
└── samples/                 # sample app (CloudIntegrator) showing usage
```

## Roadmap

- [ ] GSI/LSI support in the DynamoDB query pipeline (`IndexName` selection)
- [ ] Optimistic concurrency via ETag conditional writes
- [ ] Value-converter read path (`ConvertFromProvider`)
- [ ] TTL attribute mapping (`ttl`) wiring
- [ ] DynamoDB Local integration test suite
- [ ] Azure implementations behind the same abstractions

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and PRs welcome.

## License

[MIT](LICENSE)
