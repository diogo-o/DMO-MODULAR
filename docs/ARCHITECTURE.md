# DMO Modular — Architecture

How the repository is physically organised, and where each boundary lives.

This file describes the **code structure** and the binding architecture rules. Product/functional
authority is `diogo-o/dmo-master`; this file does not restate domain behaviour.

> **Current structure authority.** The full post-repartition physical layout (domain grouping,
> seams, frozen areas) is `docs/CURRENT_REPOSITORY_STRUCTURE.md`. The rules below (reference
> direction, "no project split pre-authorised", terminology, runtime-owns-runtime) remain
> binding; the P1-era layout descriptions in the earlier revisions of this file are historical.

## Current structure (post Stages 1–4)

```text
DMO.slnx

Directory.Build.props          shared build settings
Directory.Packages.props       central package versions

src/
  DMO.Web/                     host: composition root, HTTP, auth, admin, pages, endpoints
  DMO.Application/             orchestration + repository/persistence contracts
  DMO.Domain/                  domain primitives (value objects, entities, identities)
  DMO.Infrastructure/          persistence (single context, centralized migrations, repositories)

tests/
  DMO.UnitTests/               unit tests (domain-mirrored folders)
  DMO.IntegrationTests/        host-level + persistence/migration tests (domain-mirrored)

docs/  plans/  reports/        documentation, accepted contracts, evidence
```

Domain boundaries are expressed as namespaces/folders **inside** these six projects. No new
project or assembly was introduced by the repartition. See
`docs/CURRENT_REPOSITORY_STRUCTURE.md` for the full per-project breakdown.

## Project responsibilities

### `src/DMO.Web` — host and runtime only

Composition root, HTTP pipeline, configuration binding, dependency registration, the technical
health surface, the authentication/account boundary, ADMIN-only administration surfaces, the
shared frontend shell, navigation projection and the domain-grouped endpoint subfolders
(`Endpoints/{Core,Access,Administration,ToolJobOn,Controlo,Boquilhas,Documents}`).

Owns **no** industrial business rule. Session/authentication wiring, the Module Registry,
access resolution and navigation composition are runtime concerns and live here (D1/D2/D3).

### `src/DMO.Application` — orchestration + contracts

Orchestration services and repository/persistence contracts, kept free of infrastructure detail.
Domain-grouped: `Access/` `Accounts/` `Authentication/` `Session/` (identity/access), `Tools/`
`JobOn/` (Tool & Job On), `Controlo/{Pesos,Settings,Approve,Comparacao}` (Controlo),
`Boquilhas/`, `Documents/`, `Templates/` `TemplateAdministration/` `UserAdministration/`
(administration), `Migrations/IMigrationRunner.cs` (migration-runner boundary) and
`Persistence/` (shared persistence exception taxonomy).

`Repositories/` stays **flat** — repository contracts are cross-domain and are not split per
folder (see `docs/CURRENT_REPOSITORY_STRUCTURE.md` §Shared seams).

### `src/DMO.Domain` — domain primitives

Value objects, entities and identities for the implemented domains:
`Tools/` (`Tool`, `ToolId`, `ToolType`), `JobOn/` (`JobOn`, `JobOnId`),
`Controlo/` (Peso + Definições + Approve primitives), `ControloComparacao/` (`Comparacao`, `ComparacaoId`,
`ComparacaoCmSubject`, `ComparacaoCmDecisionKind`, `ComparacaoMeasurementRow`), `Boquilhas/`.

Types are added here only when a concrete task genuinely requires them. The comparacao domain
lives in `DMO.Domain.ControloComparacao` (a sibling of `DMO.Domain.Controlo`, not a child
`Controlo/Comparacao`), matching its production namespace.

### `src/DMO.Infrastructure` — persistence implementation

PostgreSQL connection/configuration, the single persistence context and the centralized
migration mechanism, grouped by domain ownership:
`Persistence/{Core,Access,ToolJobOn,Controlo,Boquilhas}` plus shared `Database/`,
`Configuration/`, `Migrations/`. The shared concurrency-conflict mapping
(`Persistence/Core/ConcurrencyConflictExceptionMapping.cs`) adapts EF concurrency failures to the
shared exception taxonomy in `DMO.Application/Persistence/`.

## P1-T02 — authentication + account boundary

P1-T02 introduces the authentication/account boundary only. It stops before
Template/Module/access resolution.

```text
src/DMO.Application/Authentication   contracts: IAuthenticationBoundary, typed AdminLoginRequest
                                       (email + password) and UserLoginRequest
                                       (company_number + password), AuthenticatedIdentity
                                        (ProviderSubject + AuthenticationPath only)
src/DMO.Application/Accounts         IAccountResolver + IAccountLookup split; real
                                       AccountResolver mapping semantics; ADMIN/USER
                                       account records; NoAccess states
src/DMO.Application/Session          read-only ICurrentAccountContext + CurrentAccount

src/DMO.Web/Auth                     runtime: SupabaseAuthenticationService (ADMIN path:
                                       real Supabase Auth DEV/TEST, publishable key only),
                                       SessionAuthentication (cookie scheme session element),
                                       CurrentAccountContext (real ICurrentAccountContext)
src/DMO.Web/Resolution               UnavailableAccountLookup — production P1-T02 lookup:
                                       no persisted mapping, always fails closed (P1-T03
                                       replaces it, not resolver semantics)
src/DMO.Web/Endpoints/AuthEndpoints  POST /auth/login, POST /auth/logout, GET /auth/me
```

Key decisions recorded here:

- ADMIN authentication is **real** Supabase Auth against the DEV/TEST project
  (`jixnteypqqrltsxgwzpv`, `eu-west-1`) using only the project URL and the **publishable
  key** (`apikey` header; publishable keys are never sent as Bearer tokens). No
  `service_role`, no secret key, no old schema/migration reuse. This project is DEV/TEST
  only; the future production backend is a separate clean Supabase project.
- USER authentication has **no provider flow in P1-T02**: the durable
  `company_number → provider identity` mapping requires P1-T03 persistence. The USER login
  contract stays `company_number + password`; email is never a USER login identifier.
- Authentication is separated from account resolution. In P1-T02 production a session is
  never established because resolution always fails closed (`UnavailableAccountLookup`).
- Provider identity is internal linkage only; provider claims/roles never classify access.
- Local configuration uses `dotnet user-secrets` (already initialised); the ignored
  `*.env` file is never read by the application — no `.env` loader is added. Environment
  variables (`Supabase__ProjectUrl`, `Supabase__PublishableKey`, `Database__ConnectionString`)
  are the deployed/test-host form.

## Reference direction

```text
DMO.Web            -> DMO.Infrastructure, DMO.Application, DMO.Domain
DMO.Infrastructure -> DMO.Application, DMO.Domain
DMO.Application    -> DMO.Domain
DMO.Domain         -> (nothing)
```

Dependencies point inward. The host may reference everything; nothing references the host.

Deliberately **not** used: microservices, message buses, CQRS infrastructure, mediator
frameworks, generic repository abstractions, event sourcing, plugin loaders, runtime
reflection-based discovery, multiple database contexts, distributed caching.

## Where future boundaries will live

Future functional/domain boundaries will be introduced by the task that owns them.

They should normally be represented inside the current four-project solution — as
namespaces, folders, services and persistence configuration within `DMO.Web`,
`DMO.Application`, `DMO.Domain` or `DMO.Infrastructure` — unless a concrete,
Architect-approved need justifies a new project/assembly.

**No future project split is pre-authorised by P1-T01.**

The previous README-only skeleton used `App/`, `Modules/`, `Infrastructure/` and `Shared/` as
documentation folders. Those names describe conceptual areas. They do **not** fix an assembly
layout: a future Admin, Boquilhas, Controlo, Tools, Files, Pdf or Contracts boundary is not
thereby committed to becoming its own .NET project.

Authentication and the Module Registry are **runtime** concerns and belong inside
`src/DMO.Web`, consistent with the runtime-owns-runtime boundary.

## Terminology

Preserved from the Master and the accepted plan:

```text
Module = a product-level assignable access unit
Phase  = a development/construction stage
```

A .NET project is a project. It is **not** a Module. Development slices are phases/tasks.
