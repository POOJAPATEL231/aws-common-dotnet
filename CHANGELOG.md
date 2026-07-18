# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
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
