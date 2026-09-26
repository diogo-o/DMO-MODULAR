# Tests

Two test projects, domain-mirrored in physical layout (post-Stage 4).

| Project | Purpose | Size |
| --- | --- | --- |
| `DMO.UnitTests/` | Isolated tests of application/domain/infrastructure units. | 809 tests |
| `DMO.IntegrationTests/` | Host-level tests: startup, HTTP surface, persistence/migration wiring. | 711 tests (2 environment-gated skips) |

## Layout

Both projects are grouped by domain ownership. In `DMO.UnitTests/`, the Controlo area is split
into `Controlo/{Pesos,Settings,Approve,Comparacao}`; in `DMO.IntegrationTests/` it is
`Controlo/{Pesos,Settings,Approve}` plus a shared `Persistence/` suite. Other areas
(`Access`, `Accounts`, `Authentication`, `Boquilhas`, `Documents`, `Frontend/Shared`, `JobOn`,
`Navigation`, `TemplateAdministration`, `Tools`, `UserAdministration`, `Host`, `Startup`) mirror
production directly. See `docs/CURRENT_REPOSITORY_STRUCTURE.md`.

## Shared test infrastructure

- `DMO.IntegrationTests/Host/DmoWebApplicationFactory.cs` — parameterless factory; injects
  process-scoped environment configuration before the real entry point runs, preserving and
  restoring pre-existing values on dispose.
- `Host/ProcessEnvironmentCollection.cs` — named xUnit collection with
  `DisableParallelization = true`; test classes constructing the factory are serialized.
- Cross-domain P2-T0x test stores/hosts/scans and fakes stay shared with the workstream that
  owns them (not split into a single domain's folder).

## Gating

- `DMO_TEST_POSTGRES_CONNECTION` — disposable PostgreSQL connection string; integration tests that
  need a real database (persistence/migration suites) are skipped unless it is set. They never
  target a development/production database.
- `DMO_SUPABASE_LIVE_TEST=1` (+ DEV/TEST config) — opts into live Supabase Auth tests, which are
  skipped by default (read-only, no account resolution/session).

## Rules

Tests are evidence, not product authority. A test becomes acceptance evidence only after the
Architect confirms it represents the accepted Master/plan behaviour. A green test is never
authority by itself.

## Running

```pwsh
dotnet test DMO.slnx
```

For local development, set `Database__ConnectionString` / `Supabase__*` (see
`src/DMO.Web/README.md`) via user-secrets or process environment, and set
`DMO_TEST_POSTGRES_CONNECTION` to a disposable database for the integration suite.