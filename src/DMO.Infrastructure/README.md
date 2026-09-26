# DMO.Infrastructure

Persistence implementation: PostgreSQL connection/configuration, the single persistence context,
the centralized migration stream, and the domain-grouped repositories.

No industrial business rules live here.

## Contents (domain-grouped persistence)

| Concern | Type |
| --- | --- |
| Persistence context | `Persistence/Core/DmoDbContext.cs` (the single context) |
| Migration runner implementation | `Persistence/Core/EfCoreMigrationRunner.cs` |
| Design-time context factory | `Persistence/Core/DesignTimeDmoDbContextFactory.cs` |
| Concurrency conflict mapping | `Persistence/Core/ConcurrencyConflictExceptionMapping.cs` |
| Identity & access persistence | `Persistence/Access/` (Entities + EntityConfigurations + repositories: accounts, users, templates, template modules) |
| Tool & Job On persistence | `Persistence/ToolJobOn/` (Entities + EntityConfigurations + repositories) |
| Controlo persistence | `Persistence/Controlo/` (Entities + EntityConfigurations + repositories: Peso, Settings, Approve) |
| Boquilhas persistence | `Persistence/Boquilhas/` (Entities + EntityConfigurations + repositories) |
| Connection configuration | `Database/` (`DatabaseOptions`, resolver, connection exception) |
| Product migrations | `Migrations/` (centralized; 10 migrations, append-only) |
| DI registration | `InfrastructureServiceCollectionExtensions.cs` |

## Boundaries

- one database context only (`DmoDbContext`); no second context and no generic repository
  abstraction;
- one centralized migration stream under `Migrations/`; migrations are frozen and only ever
  added as **new** files, never edited;
- the shared persistence exception taxonomy stays in `DMO.Application/Persistence/` (flat);
  `Persistence/Core/ConcurrencyConflictExceptionMapping.cs` adapts EF concurrency failures onto it;
- no connection string, credential, host, user or password default.

Configuration is supplied by the environment (see `src/DMO.Web/README.md`). A missing or invalid
connection string fails startup; it is never replaced by a production-looking default.