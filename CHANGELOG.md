# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- **Event consumption (both modes)**: `SqsMessageDispatcher` routes queued messages
  to subscribed typed/dynamic handlers; hosted either by the in-process
  `SqsConsumerService` (`AddSqsConsumer`, long-polling with DLQ-friendly failure
  semantics - ideal for local/LocalStack and container deployments) or by the
  `QueueEventDispatcher` Lambda sample (partial batch responses) that the bus
  wires as the SQS event source.
- **New service integrations (Tier 1)**: SES email (`IEmailService`), S3 presigned
  upload/download URLs, EventBridge publishing (`IIntegrationEventPublisher`) and
  EventBridge Scheduler (`IScheduler`), CloudWatch custom metrics via Embedded
  Metric Format (`IMetrics`), AWS AppConfig feature flags.
- **New service integrations (Tier 2)**: Step Functions (`IWorkflowClient`),
  Kinesis + Firehose streaming (`IStreamPublisher`), Cognito user administration
  (`IIdentityService`), DynamoDB-backed distributed lock (`IDistributedLock`),
  transactional outbox (`AddOutboxMessage` + `OutboxDispatcherService`), and an
  EF Core SQL repository (`BaseSqlDbContext`/`SqlRepository`) for relational stores.
- `IFeatureManager` was an empty marker interface - now defines the real
  feature-flag surface shared by the SSM, AppConfig and local implementations.
- **Global Secondary Index support**: declare indexes via EF `HasIndex(...)` or
  `HasGlobalSecondaryIndex(...)`; predicates on index keys are promoted from
  full-table Scans to index Queries, and `CreateTableAsync` provisions the GSIs.
- **Optimistic concurrency**: ETag stamps rotate on every save; transactional
  writes condition on the previously-read ETag and surface conflicts as
  `DynamoDbConcurrencyException`. Adds are guarded against overwriting
  existing items.
- `QueryExpression.WithIndex(...)` for explicitly targeting a GSI.
- Initial public release of the library suite: Domain.Common, Application.Common,
  Utils.Common, Infrastructure.Common, Persistence.Common.
- `Common.Tests` unit-test suite covering LINQ→DynamoDB translation, entity
  round-trip conversion, change tracking, pagination, transactions, caching
  utilities, crypto, and the event-bus subscription managers.

### Fixed
- DynamoDB pagination previously ignored the requested page and returned wrong totals.
- LINQ translation: `>=`/`<=` operator parsing, reversed operands, captured null
  variables, `Any()` missing `:zero` value, reserved-word alias collisions,
  culture-sensitive numeric formatting.
- Change tracking: inverted complex-object comparison and snapshot aliasing caused
  silent lost updates; Id-based tracker keying collided new entities.
- Entity conversion: `Guid`/`DateTimeOffset`/`TimeSpan`/`TimeOnly` properties threw
  on read; static property cache was not thread-safe.
- Transactions: writes over the DynamoDB limit were silently split into
  non-atomic batches; limit raised to 100 with fail-fast behavior.
- Redis cache re-initialization after dispose; event-bus unknown-event lookups.
