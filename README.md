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
| **Infrastructure.Common** | AWS implementations: SNS/SQS + EventBridge eventing, SES email, Redis + distributed cache, S3 file provider (incl. presigned URLs), Cognito auth + user administration, Step Functions workflows, Kinesis/Firehose streaming, EventBridge Scheduler, CloudWatch metrics (EMF) + Serilog logging, X-Ray middleware, SSM/AppConfig feature flags |
| **Persistence.Common** | **DynamoDB EF-style ORM**: `BaseDynamoDbContext`, `IDynamoDbSet<T>`, LINQ→DynamoDB expression translation, change tracking, transactional `SaveChangesAsync` with ETag concurrency, GSI queries, table auto-creation, transactional outbox, distributed lock, plus an EF Core SQL repository (`BaseSqlDbContext`/`SqlRepository`) for relational stores |

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

### Global Secondary Indexes

Declare an index with EF's `HasIndex` (or `HasGlobalSecondaryIndex` on the
DynamoDB builder) and predicates on the index key are automatically promoted
from a full-table Scan to an index Query:

```csharp
// in your DocEntityConfiguration<Order>:
builder.HasIndex(e => e.CustomerName);   // → GSI "CustomerName-index"

// this now runs as a Query against the GSI, not a Scan:
var orders = await repository.GetAsync(o => o.CustomerName == "alice");
```

Tables created by the library include the configured GSIs automatically.

### Optimistic concurrency

Every entity carries an `ETag` stamp that rotates on each save. Transactional
saves (`SaveChangesAsync`) condition writes on the ETag the entity was read
with — if another writer changed the item in between, the save throws
`DynamoDbConcurrencyException` so you can re-read and retry. Adds are guarded
with `attribute_not_exists`, so inserting over an existing key also fails fast.

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
└── samples/
    ├── AwsShowcase/         # ⭐ full clean-architecture reference app (see below)
    └── CloudIntegrator*/    # minimal legacy sample
```

## ⭐ AwsShowcase - the reference application

`samples/AwsShowcase` is a complete clean-architecture application that exercises
**every service in the suite**, structured the way a production system would be:

```
AwsShowcase.Entity        # domain: Order aggregate + integration event (no dependencies)
AwsShowcase.Core          # CQRS: commands/queries/handlers, DTOs, MediatR pipeline
                          #   behaviors: logging → Polly retry → read-through cache
                          #   → cache invalidation (queries cached, commands invalidate)
AwsShowcase.Integration   # DynamoDB context/repository (Unit of Work), transactional
                          #   outbox bridge, all AWS DI wiring (LocalStack-aware)
AwsShowcase.API           # thin controllers: one Swagger endpoint per library method
AwsShowcase.Tests         # unit tests for handlers and pipeline behaviors
```

Run it:

```bash
docker run -d -p 4566:4566 localstack/localstack   # local AWS
dotnet run --project samples/AwsShowcase/AwsShowcase.API
# open the Swagger UI shown in the console
```

Patterns demonstrated: CQRS with MediatR, cache-aside via pipeline behaviors
(`ICacheableQuery` / `ICacheInvalidatingRequest`), Polly retries on transient
faults (`IRetryableRequest`), Unit of Work with atomic saves, transactional
outbox, GSI queries via EF `HasIndex`, DTO separation, and ports-and-adapters
so Core never references AWS directly.

## Service coverage

**Eventing & messaging:** SNS/SQS event bus with two consumption modes — in-process
`SqsConsumerService` (local/containers) or the `QueueEventDispatcher` Lambda
(serverless) — plus EventBridge publisher, EventBridge Scheduler, transactional outbox
**Storage & data:** DynamoDB (EF-style ORM), S3 (+presigned URLs), SQL via EF Core (`SqlRepository`), ElastiCache/Redis
**Communication:** SES email (simple + templated)
**Identity & security:** Cognito (tokens + user administration), Secrets Manager, KMS, AES crypto utilities
**Operations:** CloudWatch Logs (Serilog) + custom metrics (EMF), X-Ray tracing, SSM Parameter Store config, feature flags (SSM or AppConfig), distributed lock
**Workflows & streaming:** Step Functions, Kinesis Data Streams, Data Firehose
**Local development:** LocalStack switch for every AWS client

## Roadmap

- [x] GSI support in the DynamoDB query pipeline (`IndexName` selection)
- [x] Optimistic concurrency via ETag conditional writes
- [x] SES, EventBridge (+Scheduler), CloudWatch metrics, AppConfig flags, presigned URLs
- [x] Step Functions, Kinesis/Firehose, Cognito admin, distributed lock, transactional outbox, SQL repository
- [ ] Value-converter read path (`ConvertFromProvider`)
- [ ] TTL attribute mapping (`ttl`) wiring
- [ ] DynamoDB Local / LocalStack integration test suite
- [ ] Azure implementations behind the same abstractions

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and PRs welcome.

## License

[MIT](LICENSE)
