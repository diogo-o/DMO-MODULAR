# Current Repository Structure — DMO Modular

Authoritative description of the physical repository layout **after** Stages 1–4 of the
repartition. This document reflects the committed code; it is the current-structure authority
that supersedes the P1-era layout descriptions in `docs/ARCHITECTURE.md` and the root `README.md`.

Domain/functional authority remains `APPLICATION_DOMAIN_MAP.md` (modules/workflow) and the
accepted workstream plans/contracts under `plans/`. This document only records **where things
live** and which seams were intentionally retained.

## Solution layout

```
DMO.slnx                        .NET 10 solution
Directory.Build.props           shared build settings (net10.0, nullable, implicit usings)
Directory.Packages.props        central package versions

src/
  DMO.Web/                      application host (composition root, HTTP, auth, pages, endpoints)
  DMO.Application/              orchestration + repository/persistence contracts
  DMO.Domain/                   domain primitives (value objects, entities, identities)
  DMO.Infrastructure/           persistence implementation (single context, migrations, repositories)

tests/
  DMO.UnitTests/                isolated unit tests
  DMO.IntegrationTests/         host-level + persistence/migration integration tests

docs/  plans/  reports/         documentation, accepted contracts, evidence reports
```

No new project/assembly was introduced by the repartition: the solution remains the same four
`src/` projects plus two `tests/` projects. The "no future project split is pre-authorised" rule
(`docs/ARCHITECTURE.md`) still binds: domain boundaries are expressed as namespaces and folders
**inside** the six existing projects.

## Domain ownership

| Domain | Owner | Primary physical home |
|---|---|---|
| D0 Shared runtime & infrastructure | cross-domain | `DMO.Web` (Program/Startup/Core endpoints), `DMO.Infrastructure/Persistence/Core`, `DMO.Application/Persistence` + `Migrations` |
| D1 Identity & Access | identity/access | `DMO.Web/Auth·Authorization·Navigation·Endpoints/Access`, `DMO.Application/{Access,Accounts,Authentication,Session}`, `DMO.Infrastructure/Persistence/Access` |
| D2 Administration | admin | `DMO.Application/{Templates,TemplateAdministration,UserAdministration}`, `DMO.Web/Endpoints/Administration` + `Pages/Administration` |
| D3 Shared frontend primitives | presentation-only | `DMO.Web/Frontend/Shared`, `DMO.Web/Pages/Shared/Components`, `wwwroot/css·js` |
| D4 Tool & Job On | tool/jobon | `DMO.Domain/{Tools,JobOn}`, `DMO.Application/{Tools,JobOn}`, `DMO.Infrastructure/Persistence/ToolJobOn`, `DMO.Web/Endpoints/ToolJobOn` + `Pages/JobOn·Ferramentas` |
| D5 Controlo | controlo | `DMO.Domain/{Controlo,ControloComparacao}`, `DMO.Application/Controlo/{Pesos,Settings,Approve,Comparacao}`, `DMO.Infrastructure/Persistence/Controlo`, `DMO.Web/Endpoints/Controlo` + `Pages/Controlo` |
| D6 Boquilhas | boquilhas | `DMO.Domain/Boquilhas`, `DMO.Application/Boquilhas`, `DMO.Infrastructure/Persistence/Boquilhas`, `DMO.Web/Endpoints/Boquilhas` + `Pages/Boquilhas` |
| Documents | controlo peso output | `DMO.Application/Documents`, `DMO.Web/Endpoints/Documents`, `tests/*/Documents` |
| D7 Deferred / inert | — | no code, no folders (identities preserved only) |

## Infrastructure organization

`src/DMO.Infrastructure/` is grouped by real domain ownership:

```
Persistence/
  Core/            DmoDbContext, EfCoreMigrationRunner, DesignTimeDmoDbContextFactory,
                   ConcurrencyConflictExceptionMapping (shared plumbing)
  Access/          Entities/ + EntityConfigurations/  (admin_accounts, users, templates,
                   template_modules) + repositories
  ToolJobOn/       Entities/ + EntityConfigurations/  (Tool, JobOn) + repositories
  Controlo/        Entities/ + EntityConfigurations/  (Peso, Settings, Approve) + repositories
  Boquilhas/       Entities/ + EntityConfigurations/  (Boquilhas) + repositories
Database/         connection options, resolver, connection exceptions
Configuration/    shared configuration
Migrations/       centralized migration stream (10 migrations) — see below
```

- **One** `DmoDbContext` (`Persistence/Core/DmoDbContext.cs`); no second context.
- **One** centralized migration stream under `Migrations/`; migrations are frozen and only ever
  added, never edited.
- The **shared persistence exception taxonomy** is intentionally retained as a flat set under
  `DMO.Application/Persistence/` (`*PersistenceException` per domain + `ConcurrencyConflictException`);
  `DMO.Infrastructure/Persistence/Core/ConcurrencyConflictExceptionMapping.cs` maps EF concurrency
  failures onto it. It was **not** split into per-domain folders.

## Application organization

`src/DMO.Application/`:

```
Access/ · Accounts/ · Authentication/ · Session/     identity & access contracts
Templates/ · TemplateAdministration/ · UserAdministration/   administration
Tools/ · JobOn/                                      Tool & Job On orchestration
Controlo/
  Pesos/        Controlo Create orchestration (P2-T05)
  Settings/     Definições (repairers, per-machine assignments, PDF directory, email lists/templates)
  Approve/      Controlo Approve orchestration (P2-T06)
  Comparacao/   Comparação orchestration
Boquilhas/                                           Boquilhas orchestration (P2-T07)
Documents/                                           Peso PDF composition/render/naming/store/send + email transport
Migrations/      IMigrationRunner (migration-runner boundary contract)
Persistence/     shared persistence exception taxonomy (flat, retained)
Repositories/    repository contracts (STILL FLAT — see "Shared seams")
```

The `Controlo` area was physically split into `Pesos`, `Settings`, `Approve`, `Comparacao`
(Stage 2). `Documents` is a real implemented area (Peso PDF + email), not future work.

## Web organization

`src/DMO.Web/` grouped non-route-bearing code by domain (Stage 3):

```
Endpoints/
  Core/            TechnicalEndpoints        (health)
  Access/          AuthEndpoints             (login/logout/me)
  Administration/  TemplateAdministrationEndpoints, UserAdministrationEndpoints
  ToolJobOn/       JobOnEndpoints, FerramentasEndpoints
  Controlo/        ControloCreateEndpoints, ControloDefinicoesEndpoints, ControloApproveEndpoints
  Boquilhas/       BoquilhasEndpoints, BoquilhasDefinicoesEndpoints
  Documents/       DocumentsEndpoints
Auth/ · Authorization/ · Navigation/ · Startup/     runtime/access composition
Frontend/Shared/ · Frontend/Shell/                  shared presentation + shell
Pages/**                                             FROZEN (see below)
wwwroot/**                                           FROZEN (see below)
```

## Test organization

Tests were regrouped to mirror domain ownership (Stage 4). The pre-split `ControloCreate`,
`ControloApprove`, `ControloComparacao` test folders were split into
`Controlo/{Pesos,Settings,Approve,Comparacao}` (unit) and `Controlo/{Pesos,Settings,Approve}`
(integration). All other test areas already mirrored production and were retained:

- `tests/DMO.UnitTests/`: Access, Accounts, Authentication, Boquilhas, Configuration, Controlo/
  (Pesos · Settings · Approve · Comparacao), Database, Documents, Frontend/Shared, JobOn,
  Navigation, Persistence, Session, TemplateAdministration, Tools, UserAdministration.
- `tests/DMO.IntegrationTests/`: Access, Auth, Boquilhas, Controlo/ (Pesos · Settings · Approve),
  Frontend/Shared, Host, JobOn, Navigation, Persistence, Startup, TemplateAdministration, Tools,
  UserAdministration.

Shared test infrastructure (`Host/DmoWebApplicationFactory.cs`, the `DMO_TEST_POSTGRES_CONNECTION`
disposable-database gate, cross-domain P2-T0x test stores/hosts/scans) stayed shared rather than
being moved under a single domain.

## Shared seams intentionally retained

- **`DMO.Application/Repositories/`** stays flat: repository contracts (`IPesoRepository`,
  `IComparacaoRepository`, `IToolRepository`, `IJobOnRepository`, etc.) are cross-domain contracts
  and were deliberately not split into per-domain folders.
- **Persistence exception taxonomy** stays flat in `DMO.Application/Persistence/`.
- **Shared frontend primitives** (`Frontend/Shared`, `Pages/Shared/Components`, `wwwroot`) stay
  presentation-only and domain-neutral; never owned by a single domain.
- **Cross-domain seams**: `IJobOnDependencyProbe`/`PesoJobOnDependencyProbe` (Peso → Job On lineage),
  Boquilhas → repairer-assignment read seam, Ferramentas → Tool orchestration. These are kept at
  the contract level; the repartition did not relocate their ownership.

## Frozen / high-risk areas

The following were intentionally **not** repartitioned and must not be moved without a separate
gated plan:

- `src/DMO.Web/Pages/**` — Razor page file paths define URLs; any move is a route change (Stage 6
  is gated pending Architect decision).
- `src/DMO.Web/wwwroot/**` — asset paths are referenced from partials/CSS.
- `src/DMO.Infrastructure/Migrations/**` — centralized migration stream; never edited.
- `src/DMO.Web/Program.cs` — composition root.
- `ModuleRegistrations.CurrentBuildAvailable` — stays `[]` (no operational module is registered
  "available" until its real surface + route are registered; P2-T10 is open).

## Migration / DbContext rules

- One `DmoDbContext`; one centralized `Migrations/` stream; **10** migrations (unchanged through
  Stages 1–4).
- Migrations are append-only and frozen; schema/model drift is gated by the
  `dotnet ef migrations has-pending-model-changes` check (must report "no changes").
- The migration-runner boundary contract is `DMO.Application/Migrations/IMigrationRunner.cs`;
  its implementation is `DMO.Infrastructure/Persistence/Core/EfCoreMigrationRunner.cs`.

## Deferred / not-implemented areas

No code, folders, routes, or availability for: **Armazém**, **Pegamentos**, **Reparação Interna**,
**Reparação Programada View/Create**, **Tampões**, **História (HISTÓRICO GLOBAL)**. Their canonical
module identities are preserved un-renamed in `ModuleCatalog`. Do not create folders for them.

Not yet implemented (open work): P2-T10 module-availability/route registration
(`ModuleRegistrations` stays `[]`).

## Relationship to historical plans/reports

- Domain workstream plans: `plans/beta-workstreams/P2-T04 DOMAIN CORE TOOL JOBON … P2-T07 BOQUILHAS`,
  plus P2-T08 DOCUMENTS-PDF and P2-T10 FINAL-INTEGRATION.
- Accepted contracts: `plans/contracts/P2-T04_…`, `P2-T05_CONTROLO_CREATE_…`
  (+ `_GLASS_DENSITY_CORRECTION_…`), `P2-T06_CONTROLO_APPROVE_…`, `P2-T07_BOQUILHAS_…`.
- Implementation/verification evidence: `reports/**` and `dev/responses/**` (immutable evidence).
- Repartition execution record: `D:\DMO\STAGE_1_INFRASTRUCTURE_REPARTITION.md` …
  `STAGE_4_TEST_REPARTITION.md` (this repository's Stage 1–4 execution reports).

The P1-era `docs/ARCHITECTURE.md` and the root `README.md` described the pre-implementation
skeleton; their **binding** rules (reference direction, "no project split pre-authorised",
`Module`/`Phase` terminology, runtime-owns-runtime) remain in force, and their stale layout
descriptions point here.