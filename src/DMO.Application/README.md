# DMO.Application

Application orchestration and the contracts required by the runtime.

## Contents (domain-grouped)

| Concern | Location |
| --- | --- |
| Migration-runner boundary | `Migrations/IMigrationRunner.cs` |
| Shared persistence exception taxonomy | `Persistence/` (`*PersistenceException` + `ConcurrencyConflictException`) |
| Canonical Module vocabulary + registry | `Access/ModuleCatalog.cs`, `Access/ModuleRegistry.cs` |
| Fail-closed access resolution | `Access/AccessResolver.cs`, `Access/AccessOutcome.cs`, `Access/ModuleResolve.cs` |
| Access facade | `Access/ModuleAccessService.cs` |
| Account resolution + account models | `Accounts/` |
| Authentication boundary + session | `Authentication/`, `Session/` |
| Template model + administration | `Templates/`, `TemplateAdministration/` |
| USER administration | `UserAdministration/` |
| Tool orchestration (canonical Tool) | `Tools/` |
| Job On orchestration | `JobOn/` |
| Controlo — Create | `Controlo/Pesos/` |
| Controlo — Definições | `Controlo/Settings/` |
| Controlo — Approve | `Controlo/Approve/` |
| Controlo — Comparação | `Controlo/Comparacao/` |
| Boquilhas orchestration | `Boquilhas/` |
| Peso PDF + email output | `Documents/` (PesoPdf*, EmailTransport, PesoPdfSendService) |
| Repository contracts | `Repositories/` (flat — see below) |

## Honest build availability

`Access/ModuleRegistrations.cs` is the only source of build availability. It remains **empty**:
no operational Module is advertised as "available" until its real functional surface, route and
server-side enforcement are registered (P2-T10 is open).

## Seams intentionally retained

- `Repositories/` stays **flat**: `IPesoRepository`, `IComparacaoRepository`, `IToolRepository`,
  `IJobOnRepository`, the settings/repairer/email repositories and the Boquilhas repository are
  cross-domain contracts and were not split into per-domain folders.
- `Persistence/` holds the shared persistence exception taxonomy (flat), referenced by the
  per-domain Infrastructure repositories.

## Boundaries

Application orchestration only. Persistence implementation lives in `DMO.Infrastructure`;
host/composition and HTTP surfaces live in `DMO.Web`. No industrial business rule is duplicated
across those projects.