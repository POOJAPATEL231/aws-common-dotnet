# Contributing

Thanks for your interest in contributing!

## Getting started

1. Fork and clone the repository.
2. Install the .NET 8 SDK.
3. `dotnet build` and `dotnet test` must both pass before and after your change.

## Guidelines

- **Tests required**: bug fixes need a regression test that fails without the fix;
  new features need coverage of the happy path and principal edge cases.
- **Layering rules**: `Domain.Common` must stay free of AWS dependencies;
  application code depends on abstractions (`Application.Common`), never directly
  on `Infrastructure.Common`/`Persistence.Common` implementations.
- **Style**: follow the `.editorconfig`; match the surrounding code's conventions.
- **Public API**: add XML doc comments to new public types and members.
- **Commits**: small, focused commits with imperative-mood messages.

## Reporting issues

Open a GitHub issue with reproduction steps, expected vs. actual behavior, and
library/OS versions. For DynamoDB translation bugs, include the LINQ expression
and the generated filter/key-condition expression if you can.
