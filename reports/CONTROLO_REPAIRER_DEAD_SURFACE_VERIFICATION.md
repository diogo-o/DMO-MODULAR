# CONTROLO REPAIRER DEAD-SURFACE VERIFICATION

**Class:** Verification / planning only. **Not** a cleanup, **not** an authorization.

**Scope:** prove whether the residual Controlo repairer service surface identified by
`reports/RECENT_BETA_AUTHORITY_DELTA_2026-09-24.md` F-06 is genuinely unreachable in production,
and define the smallest safe future cleanup boundary. No production code, test, migration, schema,
route, frontend or namespace was modified. The only artifact produced is this report.

---

## 1. Baselines

| Item | Value |
|---|---|
| `app/main` SHA (verified against `origin/main`) | `c1898c106586b0ea00f9a4a35f92b2ec1c285539` |
| Source-code state examined for this verification | `6c39a481353f1ab9f6c71ac133d243424a55416f` |
| `reference/main` SHA (verified against `origin/main`) | `cca33d472830ee52953b3cfa90018d20251d4983` |
| `app` working tree at start | CLEAN |
| `reference` working tree at start | CLEAN |
| Application code modified by this task | **NO** (report only) |

Note: `app/main` advanced from the examined source-code state `6c39a48` to the current
`c1898c1` only by documentation commits (this report and the work journal). No `src/**`, `tests/**`
or `**/Migrations/**` file differs between the two states, so every line reference below remains
valid at the current `app/main`.

---

## 2. Residual Controlo API inventory

### 2.1 Service members (the six residual operations)

Declared in `src/DMO.Application/ControloCreate/IControloDefinicoesService.cs`, implemented in
`src/DMO.Application/ControloCreate/ControloDefinicoesService.cs`:

| # | Member | Line (interface) | Line (impl) | Purpose |
|---|---|---|---|---|
| 1 | `ListRepairersAsync(CancellationToken)` | 20 | 73 | lists the repairer register |
| 2 | `CreateRepairerAsync(CreateRepairerCommand, CancellationToken)` | 23 | 77 | adds a repairer |
| 3 | `RenameRepairerAsync(RenameRepairerCommand, CancellationToken)` | 26 | 105 | renames a repairer |
| 4 | `ListMachineAssignmentsAsync(CancellationToken)` | 30 | 153 | lists machine → repairer assignments |
| 5 | `SetMachineAssignmentAsync(SetMachineAssignmentCommand, CancellationToken)` | 33 | 157 | sets/changes/clears one machine assignment |
| 6 | `ClearMachineAssignmentAsync(ClearMachineAssignmentCommand, CancellationToken)` | 36 | 218 | clears one machine assignment |

### 2.2 Associated command/result models

| Symbol | Defined in | Used by the residual surface |
|---|---|---|
| `CreateRepairerCommand` | `ControloDefinicoesModels.cs:20` | member 2 |
| `RenameRepairerCommand` | `ControloDefinicoesModels.cs:23` | member 3 |
| `SetMachineAssignmentCommand` | `ControloDefinicoesModels.cs:34` | member 5 |
| `ClearMachineAssignmentCommand` | `ControloDefinicoesModels.cs:37` | member 6 |
| `SettingsResult.RepairersFound` | `ControloDefinicoesModels.cs:166` | member 1 |
| `SettingsResult.RepairerCreated` | `ControloDefinicoesModels.cs:169` | member 2 |
| `SettingsResult.RepairerRenamed` | `ControloDefinicoesModels.cs:172` | member 3 |
| `SettingsResult.AssignmentsFound` | `ControloDefinicoesModels.cs:175` | member 4 |
| `SettingsResult.AssignmentSet` | `ControloDefinicoesModels.cs:178` | member 5 |
| `SettingsResult.AssignmentCleared` | `ControloDefinicoesModels.cs:181` | members 5, 6 |

### 2.3 Validators / tokens / helpers

| Symbol | Location | Repairer-related? | Consumers |
|---|---|---|---|
| `ControloDefinicoesValidator.Validate(CreateRepairerCommand)` | `ControloDefinicoesValidator.cs:71` | yes | residual member 2; unit test |
| `ControloDefinicoesValidator.Validate(RenameRepairerCommand)` | `:81` | yes | residual member 3; unit test |
| `ControloDefinicoesValidator.Validate(SetMachineAssignmentCommand)` | `:101` | yes | residual member 5 |
| `ControloDefinicoesValidator.Validate(ClearMachineAssignmentCommand)` | `:122` | yes | residual member 6; unit test |
| `ControloDefinicoesValidationErrors.NameRequired` | `ControloDefinicoesValidator.cs:14` | **shared token** | Controlo member 2/3 **and** `BoquilhasDefinicoesValidator` / `BoquilhasDefinicoesEndpoints` |
| `ControloDefinicoesValidationErrors.RepairerNotFound` | `:20` | **shared token** | Controlo member 5 **and** Boquilhas service/endpoints |
| `ControloDefinicoesValidationErrors.MachineUnknown` | `:29` | **shared token** | Controlo member 5 **and** Boquilhas validator/endpoints |
| `ControloDefinicoesValidationErrors.*` (the other 9) | `:17–60` | no (PDF/email/template/glass) | the Controlo PDF/email/template/glass members — stay |
| `ControloDefinicoesService.AssertVersion(...)` / `Refuse(...)` / `Map(...)` | `ControloDefinicoesService.cs` | shared helpers | **still used** by the retained PDF/email/glass members |

**Inventory note:** `ControloDefinicoesValidator.Validate(ClearMachineAssignmentCommand)` exists but
is **only** reached by the residual member 6 and by a unit test; the refused-`CLEAR` path does not
appear in production.

### 2.4 DI dependencies of the residual surface

| Dependency | Registered | Role in the residual surface | Still needed after cleanup? |
|---|---|---|---|
| `IControloDefinicoesService` → `ControloDefinicoesService` | `src/DMO.Web/Program.cs:149` (`AddScoped`) | the whole Controlo Definições service | **YES** — resolves the retained PDF/email/glass members |
| `IRepairerRepository` → `RepairerRepository` | persistence registration | member 1–3 reads/writes; member 5 repairer existence check | **YES** (shared with Boquilhas); only the Controlo service's **field** becomes unused |
| `IMachineRepairerAssignmentRepository` → `MachineRepairerAssignmentRepository` | persistence registration | member 4–6 reads/writes | **YES** (shared with Boquilhas); only the Controlo service's **field** becomes unused |
| `IBoquilhasDefinicoesService` → `BoquilhasDefinicoesService` | `src/DMO.Web/Program.cs:171` (`AddScoped`) | the live owner surface over the same two repositories | **YES** (untouched) |

The Controlo service's constructor takes seven dependencies; the two repairer repositories
(`_repairers`, `_assignments`) are used **only** by the six residual members. Removing those members
leaves those two constructor parameters/fields unused (a cleanup detail, not a DI registration
change). The repository **registrations** themselves stay, because Boquilhas resolves them.

### 2.5 Private helpers of the Controlo service (repairer path)

| Helper | Location | Used by the residual surface | Still needed after cleanup? |
|---|---|---|---|
| `AssertVersion(int persisted, int expected, string label)` | `ControloDefinicoesService.cs` | members 3, 5 (version guard) | **YES** — also used by the retained PDF/email members |
| `Refuse(SettingsRefusalReason, string)` | `ControloDefinicoesService.cs` | members 3, 5, 6 (stale-version refusal) | **YES** — also used by retained members |
| `Map(ControloPersistenceException)` | `ControloDefinicoesService.cs` | members 2, 3, 5 (23505/23503 mapping) | **YES** — also used by retained members |

**Helper conclusion:** no private helper is exclusive to the repairer family; all three helpers are
shared with the retained members, so none is a removal candidate. (This matches §8.2.)

---

## 3. Production reachability proof

Method: exhaustive symbol search over `src/**` and `tests/**` for every residual member, plus a
route/handler/DI/reflection audit. Each member is traced to its concrete callers.

### 3.1 Caller table (production = `src/**`; tests = `tests/**`)

| Member | Production caller in `src/**` | Test caller in `tests/**` | HTTP route | Page handler | DI consumer | Classification |
|---|---|---|---|---|---|---|
| `ListRepairersAsync` | none (only the Boquilhas-namespace `IBoquilhasDefinicoesService.ListRepairersAsync`, a **different** method) | `ControloSettingsRepositoryIntegrationTests.cs:80` | none (Controlo `/controlo/create/definicoes` has no repairer route; Boquilhas `/boquilhas/definicoes/repairers` uses the Boquilhas service) | none | `ControloDefinicoesService` is DI-registered (`Program.cs:149`) but no consumer calls this member | **DEAD / UNREACHABLE** (in production) · TEST ONLY (via the Controlo service instance) |
| `CreateRepairerAsync` | none (Boquilhas service has its own member) | `ControloSettingsRepositoryIntegrationTests.cs:70,136,181,225,238,285,287,289,341,385` | none | none | same | **DEAD / UNREACHABLE** · TEST ONLY |
| `RenameRepairerAsync` | none | `ControloSettingsRepositoryIntegrationTests.cs:74,88` | none | none | same | **DEAD / UNREACHABLE** · TEST ONLY |
| `ListMachineAssignmentsAsync` | none | `ControloSettingsRepositoryIntegrationTests.cs:233,246,352` | none | none | same | **DEAD / UNREACHABLE** · TEST ONLY |
| `SetMachineAssignmentAsync` | none | `ControloSettingsRepositoryIntegrationTests.cs:139,184,192,229,241,292,294,303,344,388` | none | none | same | **DEAD / UNREACHABLE** · TEST ONLY |
| `ClearMachineAssignmentAsync` | none (the Boquilhas clear path is `SetMachineAssignmentCommand` with a null `RepairerId`, a **different** code path) | `ControloSettingsRepositoryIntegrationTests.cs:348,396` | none | none | same | **DEAD / UNREACHABLE** · TEST ONLY |

### 3.2 Concrete proof per requested channel

| Channel | Finding | Evidence |
|---|---|---|
| **HTTP route** | No Controlo route exposes any repairer operation. The Controlo Definições group maps only: `GET|PUT /pdf-directory`, `POST /pdf-directory/check`, `GET|POST /email-lists`, `GET|PUT|DELETE /email-lists/{id}`, `GET|POST /email-templates`, `GET|PUT|DELETE /email-templates/{id}`, `GET /glass-densities`, `PUT /glass-densities/{processo}`. | `ControloDefinicoesEndpoints.cs` grep of `Map*` — no `repairer`, no `machine-assignment` |
| **Razor Page handler** | The Controlo Definições page loads/saves only PDF directory, email lists, email templates and glass densities. The repairing sections were removed (supersession comment present). | `Pages/Controlo/Definicoes.cshtml.cs` / `.cshtml` — repairer mentions are **comments only** ("the repairer register … left this surface") |
| **Endpoint mapping** | The Boquilhas Definições group owns the real routes: `GET /boquilhas/definicoes/repairers`, `POST /boquilhas/definicoes/repairers`, `PUT /boquilhas/definicoes/repairers/{repairerId}`, `GET /boquilhas/definicoes/machine-assignments`, `PUT /boquilhas/definicoes/machine-assignments/{machine}` — all bound to `IBoquilhasDefinicoesService`. | `BoquilhasDefinicoesEndpoints.cs` |
| **Service-to-service production call** | None. No `src/**` file calls any of the six members on `IControloDefinicoesService`/`ControloDefinicoesService`. The only `src/**` hits for those names are their own declarations and the Boquilhas-namespace homonyms. | symbol grep across `src/**` |
| **Background process** | There is no hosted service / timer / queue worker in `src/DMO.Web` or `src/DMO.Infrastructure` that resolves `IControloDefinicoesService`. | no `IHostedService`/`BackgroundService` references to the service |
| **DI-created consumer** | `ControloDefinicoesService` **is** registered (`Program.cs:149`) and **is** resolved for the retained PDF/email/glass operations, but resolution alone does not touch the six members; no constructor-injected consumer calls them. | `Program.cs:149`; consumer search |
| **Reflection / dynamic invocation** | No reflection over service members. The only `Invoke` occurrences are a delegate call in `BoquilhasEndpoints.cs:472` (`success?.Invoke(result)`, a local typed delegate) and a UI action-key dispatch in `DecisionBarInteraction.cs:42` (unrelated). | `grep GetMethod/Invoke/Activator/GetType().Get` over `src/**` |

### 3.3 Important distinction (avoid a false "LIVE")

`BoquilhasDefinicoesService` / `IBoquilhasDefinicoesService` declare members with the **same names**
(`ListRepairersAsync`, `CreateRepairerAsync`, `RenameRepairerAsync`, `ListMachineAssignmentsAsync`,
`SetMachineAssignmentAsync`). Those are **different types in a different namespace**
(`DMO.Application.Boquilhas`) and are the ones wired to the live Boquilhas routes. They must not be
counted as evidence that the Controlo members are live. The Controlo members have **no production
caller of their own type**.

**Conclusion for §3:** all six residual members are **DEAD / UNREACHABLE in production** and
**TEST ONLY** in coverage. No member is `LIVE` or `INDIRECTLY LIVE`.

---

## 4. Current canonical Boquilhas path

### 4.1 Trace (production)

| Operation | Route / page | → application service | → repository | → persistence |
|---|---|---|---|---|
| Repairer list | `GET /boquilhas/definicoes/repairers` (`BoquilhasDefinicoesEndpoints.cs:45`); page `Pages/Boquilhas/Definicoes.cshtml.cs:74` | `IBoquilhasDefinicoesService.ListRepairersAsync` (`BoquilhasDefinicoesService.cs:109`) | `IRepairerRepository.ListAsync` | `repairers` table |
| Repairer create | `POST /boquilhas/definicoes/repairers` (`:62`) | `CreateRepairerAsync` (`:123`) | `IRepairerRepository.CreatedAsync` | `repairers` |
| Repairer rename | `PUT /boquilhas/definicoes/repairers/{repairerId}` (`:86`) | `RenameRepairerAsync` (`:146`) | `IRepairerRepository.GetByIdAsync` + `RenamedAsync` | `repairers` |
| Machine assignment list | `GET /boquilhas/definicoes/machine-assignments` (`:115`); page `Definicoes.cshtml.cs:88` | `ListMachineAssignmentsAsync` (`:190`) | `IMachineRepairerAssignmentRepository.ListAsync` | `machine_repairer_assignments` |
| Machine assignment set/clear | `PUT /boquilhas/definicoes/machine-assignments/{machine}` (`:132`) | `SetMachineAssignmentAsync` (`:210`) | `IMachineRepairerAssignmentRepository.SetAsync` / `ClearedAsync` (null `RepairerId` = clear) | `machine_repairer_assignments` |

- The group is gated by the canonical Boquilhas module policy (`BoquilhasDefinicoesEndpoints` group
  authorization), matching every other Boquilhas route.
- `BoquilhasDefinicoesService` is DI-registered at `Program.cs:171`
  (`AddScoped<IBoquilhasDefinicoesService, BoquilhasDefinicoesService>()`).

### 4.2 Shared repository types

Both services use the **same** repository interfaces (so the physical path is shared, not
duplicated):

- `IRepairerRepository` / `RepairerRepository`
- `IMachineRepairerAssignmentRepository` / `MachineRepairerAssignmentRepository`

`ControloDefinicoesService` constructs/consumes the same two repositories for its dead members;
`BoquilhasDefinicoesService` consumes them for the live members.

**Conclusion for §4:** the real feature is fully served by the Boquilhas path. Removing the Controlo
duplicate would **not** remove the feature.

---

## 5. Shared persistence boundary

### 5.1 Must remain shared and untouched

| Item | Status | Evidence |
|---|---|---|
| `repairer_id` values (canonical IDs) | shared; must not change | `RepairerId` in `DMO.Domain.Controlo`; rows in `repairers` |
| `repairers` table | shared; untouched | migration `20260923045054_ControloCreateDomain.cs:128` |
| `machine_repairer_assignments` table | shared; untouched | same migration `:192`, with `FK_machine_repairer_assignments_repairers_repairer_id` |
| `IRepairerRepository` / `IMachineRepairerAssignmentRepository` (+ implementations) | shared; untouched | used by both services |
| Historical movement FKs to repairers | shared; untouched | `boquilha_movements.repairer_id` + `FK_boquilha_movements_repairers_repairer_id` (`20260924051151_BoquilhasDomain.cs:88–91`), `IX_boquilha_movements_repairer_id` (`:160`), and the audit columns `before_repairer_id`/`after_repairer_id` (`:115–116`) |
| Machine-assignment persisted state | shared; untouched | the same table and repository |
| Repairer domain types | shared; untouched | `Repairer`, `RepairerId`, `MachineRepairerAssignment` in `DMO.Domain.Controlo` (used by both) |

### 5.2 Does cleanup require schema changes / migration / data migration?

**NO.**

Proof:
- The cleanup removes **application members and their command/result carriers only**. It deletes no
  table, column, index, constraint or FK.
- The physical tables `repairers` and `machine_repairer_assignments` (migration 007 of the Controlo
  Create domain — the `20260923045054_ControloCreateDomain` migration) remain owned by the same
  migration and are still used by the Boquilhas service.
- `boquilha_movements.repairer_id` and its FK keep referencing `repairers`; removing a service
  member cannot affect a database constraint.
- No data is moved or rewritten; `repairer_id` values are untouched.
- No new migration is introduced, and migrations 001–008 stay byte-identical.

---

## 6. Validator dependency analysis

### 6.1 Does Boquilhas import/reuse Controlo validation code?

**YES** — this is the one genuine coupling that blocks a naive "delete the Controlo
repairer surface" cleanup.

| Shared symbol | Defined in (Controlo) | Boquilhas callers | Would removal break Boquilhas? |
|---|---|---|---|
| `ControloDefinicoesValidationErrors.NameRequired` | `ControloDefinicoesValidator.cs:14` | `BoquilhasDefinicoesService.cs:22,35,40` (`BoquilhasDefinicoesValidator`); `BoquilhasDefinicoesEndpoints.cs:70,95` (alias `ControloDefinicoesErrors`) | **YES** |
| `ControloDefinicoesValidationErrors.RepairerNotFound` | `:20` | `BoquilhasDefinicoesService.cs:64,243` | **YES** |
| `ControloDefinicoesValidationErrors.MachineUnknown` | `:29` | `BoquilhasDefinicoesService.cs:59`; `BoquilhasDefinicoesEndpoints.cs:141` | **YES** |
| `ControloDefinicoesValidator.Validate(...)` (repairer overloads) | `:71,81,101,122` | **not** called by Boquilhas — Boquilhas has its own `BoquilhasDefinicoesValidator` (`BoquilhasDefinicoesService.cs:14`, called at `:129,152,216`) | no (Boquilhas does not call the Controlo validator) |

Additional notes:
- `BoquilhasDefinicoesService.cs:1` has `using DMO.Application.ControloCreate;` — needed **only** for
  `ControloDefinicoesValidationErrors` (the Boquilhas command carriers are defined locally in
  `BoquilhasDefinicoesModels.cs`).
- `BoquilhasDefinicoesEndpoints.cs:2` aliases the same class.
- The **exact token strings** (`NAME_REQUIRED`, `REPAIRER_NOT_FOUND`, `MACHINE_UNKNOWN`) are the
  contracted transport vocabulary proving the moved surface keeps the same tokens.

### 6.2 Smallest safe extraction/re-home needed BEFORE deleting old code

**Required (small).** Because the three error-code constants are shared, the future cleanup must
**extract** the repairer-related error codes (or the whole `ControloDefinicoesValidationErrors`
class) out of the Controlo repairer surface **before** removing that surface. Candidate minimal
options (not implemented here):

1. Move the whole `ControloDefinicoesValidationErrors` class to a neutral location
   (e.g. a shared errors module) and update both Controlo and Boquilhas references; or
2. Extract only `NameRequired`, `RepairerNotFound`, `MachineUnknown` into a repairer-family errors
   type owned by Boquilhas, keeping the Controlo PDF/email/template/glass codes where they are; or
3. (least invasive) leave `ControloDefinicoesValidationErrors` in place and delete only the six
   service members + their unused command/result carriers, accepting that three constants of an
   otherwise Controlo-named class remain referenced by Boquilhas.

Options 1/2 are cleaner; option 3 is the smallest diff. This report does not choose or implement.
**No extraction is implemented here.**

### 6.3 Symbols that are NOT shared and are safe candidates for removal

Confirmed not referenced by Boquilhas (or by the retained Controlo PDF/email/glass members):
`CreateRepairerCommand`, `RenameRepairerCommand`, `SetMachineAssignmentCommand`,
`ClearMachineAssignmentCommand`, `SettingsResult.RepairersFound/RepairerCreated/RepairerRenamed/
AssignmentsFound/AssignmentSet/AssignmentCleared`, and the four repairer `Validate` overloads
(production usage limited to the dead members).

---

## 7. Test dependency analysis

| Test | File | What it does | Classification |
|---|---|---|---|
| `ControloSettingsRepositoryIntegrationTests` | `tests/DMO.IntegrationTests/Persistence/ControloSettingsRepositoryIntegrationTests.cs` | constructs `ControloDefinicoesService` directly (`Definicoes(...)` helper, `:35`) over the real repositories and exercises repairer register, machine assignments, PDF directory, email lists/templates | **STALE OWNERSHIP TEST** — must migrate the repairer/machine-assignment cases to the Boquilhas service; the PDF/email cases are legitimate |
| `ControloDefinicoesValidatorTests` | `tests/DMO.UnitTests/ControloCreate/ControloDefinicoesValidatorTests.cs` | validates `ControloDefinicoesValidator` incl. repairer overloads and `ClearMachineAssignmentCommand` | **STALE OWNERSHIP TEST** (repairer/machine cases) + legitimate PDF/email/template/glass validation cases |
| `MachineAndSettingsShapeTests` | `tests/DMO.UnitTests/ControloCreate/MachineAndSettingsShapeTests.cs` | asserts the shape of `Repairer`/`MachineRepairerAssignment` etc. under the `ControloCreate` namespace | **REGRESSION GUARD** for the shared domain shapes — likely keep, but the namespace/ownership framing is stale |
| `ControloDefinicoesEndpointsTests` | `tests/DMO.IntegrationTests/ControloCreate/ControloDefinicoesEndpointsTests.cs` | exercises the **remaining** Controlo routes (PDF/email); carries a supersession comment for the moved repairer routes; does not call repairer routes | **LEGITIMATE** (already correct) |
| `BoquilhasDefinicoesEndpointsTests` | `tests/DMO.IntegrationTests/Boquilhas/BoquilhasDefinicoesEndpointsTests.cs` | exercises the live Boquilhas Definições repairer/machine-assignment routes | **LEGITIMATE (current owner)** |
| `BoquilhasPreJobonAssociationIntegrationTests` | `tests/DMO.IntegrationTests/Persistence/BoquilhasPreJobonAssociationIntegrationTests.cs` | uses `BoquilhasDefinicoesService` (`:534`) to arrange repairers/assignments; also tests historical preservation (`:528–555`) | **LEGITIMATE SHARED-PERSISTENCE / REGRESSION GUARD** |
| `ControloCreateAccessTests` / `ControloApproveAccessTests` | `tests/DMO.IntegrationTests/Controlo*/…` | assert `/controlo/create/definicoes/repairers` is gone and `/boquilhas/definicoes/repairers` is gated | **REGRESSION GUARD** (keep) |
| `P2T05ProductionScan` / `P2T07ProductionScan` | `tests/DMO.IntegrationTests/ControloCreate|Boquilhas/…` | negative-scope scans referencing the repairer move | **REGRESSION GUARD** (keep; may need the scan's expectations updated only if source tokens disappear) |
| `Migration007BoquilhasDomainTests` | `tests/DMO.IntegrationTests/Persistence/Migration007BoquilhasDomainTests.cs` | Saída-required CHECK (machine/repairer) | **LEGITIMATE SHARED-PERSISTENCE** (keep) |

**Key observation:** the only tests that exercise the **Controlo** repairer members directly are
`ControloSettingsRepositoryIntegrationTests` and the repairer cases of
`ControloDefinicoesValidatorTests`. Both reach the members by constructing the Controlo service
directly — i.e. they are the sole remaining consumers of the dead surface. **No test was deleted or
rewritten by this task.**

---

## 8. Minimal future cleanup boundary

Goal: leave **Boquilhas = the only operational owner** while preserving canonical repairer IDs,
shared repositories, existing tables/data, historical movement repairer relations, machine
assignments and existing Boquilhas behavior.

### 8.1 REMOVE members from

| File | Remove |
|---|---|
| `src/DMO.Application/ControloCreate/IControloDefinicoesService.cs` | the six members (`ListRepairersAsync`, `CreateRepairerAsync`, `RenameRepairerAsync`, `ListMachineAssignmentsAsync`, `SetMachineAssignmentAsync`, `ClearMachineAssignmentAsync`) |
| `src/DMO.Application/ControloCreate/ControloDefinicoesService.cs` | the six implementations, the repairer/machine-assignment field usage, and the now-unused `_repairers`/`_assignments` fields (and their constructor parameters) |
| `src/DMO.Application/ControloCreate/ControloDefinicoesModels.cs` | `CreateRepairerCommand`, `RenameRepairerCommand`, `SetMachineAssignmentCommand`, `ClearMachineAssignmentCommand`, and the six `SettingsResult` repairer/assignment members |
| `src/DMO.Application/ControloCreate/ControloDefinicoesValidator.cs` | the four repairer `Validate` overloads (repairer create/rename, assignment set/clear) |

### 8.2 DELETE if now-unused

- Nothing in `ControloDefinicoesService.cs`'s helper surface (`AssertVersion`, `Refuse`, `Map`) —
  still used by the retained PDF/email/glass members.
- Nothing under `DMO.Domain.Controlo` (repairer types are shared with Boquilhas).
- Nothing in `DMO.Infrastructure.Persistence` (repositories/entities are shared).

### 8.3 MOVE / EXTRACT (required before removing the old code)

- The shared error tokens `NameRequired`, `RepairerNotFound`, `MachineUnknown` (currently
  `ControloDefinicoesValidationErrors`). Extract/re-home per §6.2. **Boquilhas depends on these
  today**; removing them without extraction breaks the build.

### 8.4 KEEP untouched

- `IRepairerRepository`, `IMachineRepairerAssignmentRepository` + implementations.
- `repairers`, `machine_repairer_assignments` tables; `boquilha_movements.repairer_id` FK and index.
- `DMO.Domain.Controlo` repairer types.
- All Controlo PDF-directory / email-list / email-template / glass-density members, routes and page.
- The entire Boquilhas Definições surface (`IBoquilhasDefinicoesService`,
  `BoquilhasDefinicoesService`, `BoquilhasDefinicoesValidator`, endpoints, page, models).
- `Program.cs` DI registrations for the retained Controlo service and the Boquilhas service.
- `ModuleRegistrations.CurrentBuildAvailable` (stays `[]`); all routes; migrations 001–008.

---

## 9. Risk analysis

| Area | Could the cleanup affect it? | Why / why not |
|---|---|---|
| PDF/email settings in Controlo Definições | **NO** | same service class, but the PDF-directory/email members, routes and page are untouched; only the six repairer members and their carriers are removed. The retained members keep their helpers (`AssertVersion`/`Refuse`/`Map`). |
| Glass density | **NO** | `ListGlassDensitiesAsync`/`UpdateGlassDensityAsync` and routes 18/19 are untouched; the glass validation overload and codes stay. |
| Peso | **NO** | Peso is a separate service (`IControloCreateService`), separate tables, no reference to the Controlo Definições repairer members. |
| Job On | **NO** | Job On owns its own service/contracts; the cleanup touches no Job On code. |
| Boquilhas movement history | **NO** | Historical preservation is the stored `boquilha_movements.repairer_id` (+ audit before/after columns) and its FK to `repairers`. The cleanup removes no table/column/FK and does not touch `BoquilhasService` or the Boquilhas repository. |
| Database migrations | **NO** | No migration is added, changed or removed; migrations 001–008 stay byte-identical; no column/table/FK is dropped. |
| Authorization / module gates | **NO** | The six dead members are not bound to any route; the Controlo group policy (`controlo-create`) and the Boquilhas group policy are unchanged. Removing an unbound service member changes no gate. |
| Module availability | **NO** | `ModuleRegistrations.CurrentBuildAvailable` stays `[]`; the cleanup registers nothing and removes no availability entry. No module becomes available or unavailable. |
| **Build (the one real risk)** | **YES (must handle)** | Boquilhas references `ControloDefinicoesValidationErrors.{NameRequired,RepairerNotFound,MachineUnknown}`. Deleting that class without extracting the three constants would break the build. This is the sole blocker and it is a code-move, not a behavior change. |

---

## 10. Acceptance tests for later cleanup

Draft, minimum executable checks (for the future authorized cleanup — not implemented here):

| ID | Check | Expected |
|---|---|---|
| C-01 | No Controlo production route exposes repairer configuration | route inventory scan over `src/DMO.Web/Endpoints/**` finds no Controlo-repairer route; `/controlo/create/definicoes/repairers` does not exist (404) |
| C-02 | Boquilhas Definições still provides all repairer operations | `GET/POST /boquilhas/definicoes/repairers`, `PUT /boquilhas/definicoes/repairers/{id}`, `GET /boquilhas/definicoes/machine-assignments`, `PUT /boquilhas/definicoes/machine-assignments/{machine}` all succeed under the granted policy (existing `BoquilhasDefinicoesEndpointsTests` stays green) |
| C-03 | Existing repairer IDs remain unchanged | pre/post cleanup: same `repairer_id` values for the same rows; a rename keeps the same id (existing REP2 assertion) |
| C-04 | Historical Saída retains its stored repairer relation | `boquilha_movements.repairer_id` for an existing Saída is byte-identical before/after; the FK/index still present |
| C-05 | Changing an assignment does not rewrite historical movements | change a machine's assignment; the existing movement's stored `repairer_id` is unchanged (existing `T14`/`R2` semantics) |
| C-06 | No schema/data migration occurs | migrations 001–008 byte-identical (`git diff` empty); migration count unchanged; `information_schema` table/column/FK set unchanged |
| C-07 | Controlo PDF/email/glass-density settings still work | existing `ControloDefinicoesEndpointsTests`, `ControloDefinicoesValidatorTests` (non-repairer cases), `GlassDensitySettings*Tests` green |
| C-08 | Full regression suite remains green | build 0 errors; unit + integration suites unchanged in count/pass (except intentionally migrated repairer cases) |
| C-09 | Shared code compiles without the Controlo repairer surface | the extracted/re-homed error tokens resolve; Boquilhas references compile; no dangling Controlo repairer symbol remains |

---

## 11. Verdict

**B. CLEANUP REQUIRES SHARED EXTRACTION FIRST.**

Rationale with concrete evidence:

- **The dead surface is confirmed dead.** All six Controlo members (`ListRepairersAsync`,
  `CreateRepairerAsync`, `RenameRepairerAsync`, `ListMachineAssignmentsAsync`,
  `SetMachineAssignmentAsync`, `ClearMachineAssignmentAsync`) have **zero production callers** in
  `src/**`, no Controlo HTTP route, no page handler, and no service-to-service caller; the only
  consumers are two tests that construct `ControloDefinicoesService` directly
  (`ControloSettingsRepositoryIntegrationTests`, `ControloDefinicoesValidatorTests`). The live
  feature runs entirely through the Boquilhas service/endpoints over the same repositories.
- **But it is not a "delete-only" cleanup.** `BoquilhasDefinicoesService.cs` (lines 22, 35, 40, 59,
  64, 243) and `BoquilhasDefinicoesEndpoints.cs` (lines 70, 95, 141, via the alias `ControloDefinicoesErrors`)
  **import Controlo symbols** — `ControloDefinicoesValidationErrors.{NameRequired, RepairerNotFound,
  MachineUnknown}`. Removing the Controlo repairer surface without first extracting those three
  shared error tokens (or the whole class) would break the build. Therefore the smallest *safe*
  cleanup must perform a small shared extraction first — hence verdict **B**, not **A**.
- **No migration/schema work is required** (§5.2) and the sole risk is the compile-time symbol
  coupling (§9), which is a code move.

---

## 12. Stop

Verification only. The single change of this task is this report, committed and pushed to
`dgains00-create/app/main`. Nothing else was modified: no production code, no tests, no
migrations/schema, no service method removed, no frontend, no routes, no namespaces.

---

## 13. Verification statement

- `app/main` SHA recorded; `reference/main` SHA recorded.
- Production code modified: **NO**.
- Tests modified: **NO**.
- Migrations/schema modified: **NO**.
- Service methods removed: **NO**.
- Frontend / routes / namespaces modified: **NO**.
- Only this report was added, committed and pushed.
