# RECENT BETA AUTHORITY DELTA — 2026-09-24

**Class:** Analysis / planning delta. **Not** an implementation plan, **not** a contract, **not**
a schema design, **not** an authorization to change code.

**Scope of this task:** ANALYSIS ONLY. No production code, migration, frontend, contract or
accepted report was modified. The only file added is this report.

**Method:** the canonical files in `dgains00-create/reference` @ `main` were read (not inferred
from commit messages) against the implementation state in `dgains00-create/app` @ `main`,
including plans, contracts, reports, development responses, source code and tests.

---

## 1. Baselines

| Item | Value |
|---|---|
| `app/main` HEAD SHA (this run, verified against `origin/main`) | `a59d8c13bb8de4dbe8a129217304d987dcfc0755` |
| `reference/main` HEAD SHA (this run, verified against `origin/main`) | `cca33d472830ee52953b3cfa90018d20251d4983` |
| Expected reference HEAD commit ("Move repairer operational settings into Boquilhas") | **MATCHES** `cca33d4` |
| `app` working tree at start | CLEAN |
| `reference` working tree at start | CLEAN |
| Application code modified by this task | **NO** (report only) |

### 1.1 Authority commits inspected (reference)

| Commit | Subject | Files touched |
|---|---|---|
| `398d50919f2938021e15a1b3e3d73c1554484244` | Document Job On-centered context flow | `architecture/CROSS_MODULE_FLOWS.md` (+46/−23) |
| `e160b8cc31fa67976bd2b5550a1521ae34699527` | Keep Beta cross-module flow within Beta scope | `architecture/CROSS_MODULE_FLOWS.md` (1 line) |
| `f977d86893e3db0c3420f60f7a7f212e9239ef5f` | Define Beta Tool creation without Armazem | `modules/FERRAMENTAS_LIGHT.md` (+18) |
| `cca33d472830ee52953b3cfa90018d20251d4983` | Move repairer operational settings into Boquilhas | `modules/BOQUILHAS.md` (+23/−7) |

Files read in full in `reference/main`: `AUTHORITY.md`, `BETA_SCOPE.md`, `IMPLEMENTATION_MODEL.md`,
`WORKFLOW.md`, `architecture/CROSS_MODULE_FLOWS.md`, `architecture/ACCESS_AND_NAVIGATION.md`,
`architecture/RECORD_LIFECYCLES.md`, `architecture/BACKEND_FRONTEND_MODEL.md` (part),
`contracts/IDENTITIES_AND_RELATIONSHIPS.md`, `implementation/BETA_INTEGRATION_SEAMS.md`,
`modules/JOB_ON_LIGHT.md`, `modules/FERRAMENTAS_LIGHT.md`, `modules/CONTROLO_CREATE.md`,
`modules/CONTROLO_APPROVE.md`, `modules/BOQUILHAS.md`.

### 1.2 App artifacts compared

Plans/contracts: `plans/BETA_IMPLEMENTATION_MASTER_PLAN.md`,
`plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md`,
`plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md`,
`plans/contracts/P2-T06_CONTROLO_APPROVE_CONTRACT.md`,
`plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md`, workstream plans P2-T04…P2-T10.
Reports: `reports/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_DELTA.md`,
`reports/BETA_MASTER_RECONCILIATION.md`, P2_T04–P2_T07 verification reports.
Responses: `dev/responses/OWNER_CLARIFICATION_PLANNING_CONTEXT_AND_ASSOCIATIONS_RESPONSE.md`,
`dev/responses/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_AUTHORITY_RESPONSE.md` and the P2-Txx
implementation responses.
Source/tests: `src/DMO.Application/**`, `src/DMO.Domain/**`, `src/DMO.Infrastructure/**`,
`src/DMO.Web/**`, `tests/DMO.UnitTests/**`, `tests/DMO.IntegrationTests/**`.

### 1.3 A pre-existing condition that must be stated up front

`app/main` HEAD (`a59d8c1`, committed 2026-09-24 14:11 LOCAL) is **chronologically later** than
`reference/main` HEAD (`cca33d4`, 2026-09-24 12:35). The app already contains several "OWNER
CLARIFICATION delta" commits whose wording is substantially aligned with the reference intent —
in some places the app has gone **further** than the reference (repairer family physically
re-homed to Boquilhas), and in other places the app has gone **further than a different track**
(Boquilhas movement vocabulary changed from the reference's four types to three).

Because the two repositories record different decisions on the same subject, this report
evaluates the app **against** `reference/main` as the stated canonical Beta authority. Where the
app is already aligned, it is recorded as ALREADY ALIGNED. Where the app carries a **different
model** (not merely stale wording), it is recorded as a divergence and flagged for escalation,
because a Beta document may not silently contradict the Beta canonical document
(`AUTHORITY.md` "Conflict rule").

---

## 2. New canonical rules

### 2.1 Job On is the centre of production context

- **Authority file/section:** `architecture/CROSS_MODULE_FLOWS.md` → "Core chain", "Tool selection
  and downstream context", "Context notifications and light reads" (commit `398d509`, refined by
  `e160b8c`).
- **Previous assumption (reference's own earlier text, and reflected in the app):** Tool might be
  searched/selected independently per module; `Job On → Controlo` was modelled as a direct
  `jobon_id → cm_id → tool_id → peso_id` chain without an explicit production-entry surface.
- **Current rule:** Job On selects/creates the canonical Tool and creates the production-specific
  `cm_id`/`mf_id`/`bq_id` snapshots. Downstream modules **consume** the resulting production
  context; they must not independently reselect the same Tool to reconstruct production identity.
  A module must not require the operator to re-enter facts already known from the Job On context.
- **Affected workstreams:** P2-T04 (B), P2-T05 (C), P2-T07 (E), P2-T10.

### 2.2 Controlo entry flow goes through the Resumo da produção

- **Authority file/section:** `architecture/CROSS_MODULE_FLOWS.md` → "Job On → Controlo Create"
  (commit `398d509`); `modules/CONTROLO_CREATE.md` → "Peso workflow".
- **Previous assumption:** Controlo received the production through a `jobon_id → cm_id → Peso`
  chain; the Resumo was an output record only.
- **Current rule:** for a production with Job On context the flow is
  `jobon_id → Resumo da produção → cm_id → Peso`. Peso is **populated** from the CM/Job On context
  with the already-known production facts (machine, reference, lot, process, CM identity/context).
  The operator enters **only Peso-owned measurement data**.
- **Affected workstreams:** P2-T05 (C), P2-T06 (D, consumes the same record), P2-T10.

### 2.3 Tool creation without Armazém

- **Authority file/section:** `modules/FERRAMENTAS_LIGHT.md` → "Tool creation without Armazém"
  (commit `f977d86`); `architecture/CROSS_MODULE_FLOWS.md` (light reads).
- **Previous assumption:** no explicit statement; permission to require a warehouse position could
  be inferred.
- **Current rule:** a canonical Tool must be creatable and usable in Beta with **no** Armazém
  position, movement or relation. Absence of Armazém context is not an error and must not block
  Tool creation, Tool selection, Job On planning or downstream use.
- **Affected workstreams:** P2-T04 (B), P2-T05 (C), P2-T07 (E).

### 2.4 Boquilhas / Definições owns the repairer family

- **Authority file/section:** `modules/BOQUILHAS.md` → "Repairers and line assignments" (commit
  `cca33d4`); `IMPLEMENTATION_MODEL.md` Workstream E ("repairer/line context consumption").
- **Previous assumption (the one the app recorded via its own delta):** the repairer register and
  the machine/line → repairer assignments were owned by `Controlo_Create → Definições`; not Admin.
- **Current rule:** Boquilhas / Definições owns the repairer directory **and** the machine/line →
  repairer assignment. These are **not** owned by Admin, **not** by Controlo Create, **not** by
  Controlo Approve. Assignments are independent per machine/line with no cascade. Every external
  Saída stores the final selected canonical `repairer_id`, and historical movements retain their
  stored repairer relation even if the directory or line assignment changes later.
- **Affected workstreams:** P2-T05 (C), P2-T07 (E), P2-T10.

### 2.5 Beta scope must not absorb non-Beta modules

- **Authority file/section:** `architecture/CROSS_MODULE_FLOWS.md` → "Core chain" (commit
  `e160b8c` changed `mf_id ──→ Controlo / Reparação Interna where required` to
  `mf_id ──→ Controlo where required`); `BETA_SCOPE.md` → "Explicitly not implied by this scope".
- **Previous assumption:** the cross-module flow diagram implied Reparação Interna as a direct
  consumer of `mf_id`.
- **Current rule:** the Beta cross-module flow stays inside Beta. Armazém, Reparação Interna,
  Reparação Programada and other non-Beta modules are not pulled into the Beta flow.
- **Affected workstreams:** P2-T04, P2-T05, P2-T07, P2-T10.

---

## 3. Implementation delta

Classification key: **ALREADY ALIGNED** · **DOCUMENTATION STALE ONLY** · **TEST STALE ONLY** ·
**IMPLEMENTATION MISMATCH** · **FUTURE WORK ONLY** · **NO ACTION REQUIRED**.

### F-01 — Controlo entry through the Resumo da produção — ALREADY ALIGNED

- **Exact file(s):** `src/DMO.Application/ControloCreate/IProductionResumoRead.cs`,
  `src/DMO.Infrastructure/Persistence/DmoProductionResumoRead.cs`,
  `src/DMO.Web/Pages/Controlo/Create.cshtml.cs`,
  `plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md` §31.1.
- **Current behavior:** `IProductionResumoRead.GetResumoAsync(jobOnId)` resolves ONE surgical
  statement over `job_ons LEFT JOIN cm_contexts LEFT JOIN tools`; `Create.cshtml.cs`
  (`LoadProductionAsync`) builds the production context (reference, production number, machine,
  production date, processo) and the CM context from it, and the Peso carriers
  (`ControloCreateModels.cs`) carry `CmId`/`PendingToolId` plus Peso-owned measurement data — not
  re-typed machine/reference/lot/processo.
- **Expected behavior (reference):** `jobon_id → Resumo da produção → cm_id → Peso`, Peso populated
  from the context, operator enters only measurement data.
- **Why it matches:** the entry surface and read model implement exactly the reference flow; the
  reference additionally fixes `resumo_id` as a *persisted* record (see F-04).
- **Smallest correction boundary:** none.

### F-02 — Job On as the Tool-selection centre — ALREADY ALIGNED

- **Exact file(s):** `src/DMO.Application/Tools/ToolService.cs`,
  `src/DMO.Application/ControloCreate/ControloCreateService.cs` (uses `IToolService.GetAsync` for
  context reads only), `src/DMO.Web/Pages/Boquilhas/Novo.cshtml.cs` (consumes the shared
  `ToolPickerPresentation`), `tests/DMO.UnitTests/Tools/ToolOrchestrationTests.cs` (single
  orchestration proof).
- **Current behavior:** one shared search/select/create orchestration (`IToolService`) is consumed
  by Job On, Controlo and Boquilhas. Controlo and Boquilhas do **not** create a private Tool
  registry; Boquilhas reuses the shared picker; Controlo reads Tool facts through `cm_id → tool_id`.
- **Expected behavior (reference):** same.
- **Why it matches:** no module independently reselects a Tool to reconstruct production identity.
- **Smallest correction boundary:** none.

### F-03 — Tool creation without Armazém — ALREADY ALIGNED

- **Exact file(s):** `src/DMO.Domain/Tools/Tool.cs` (identity tuple Type/Reference/Lot; no
  warehouse member), `src/DMO.Application/Tools/ToolModels.cs` (`CreateToolCommand` carries no
  location), `plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md` §24.2.
- **Current behavior:** Tool creation has no Armazém dependency; no warehouse field, table or
  relation is consulted or required. Armazém exists only as an inert `ModuleCatalog` identity
  (`armazem`, no route, no availability).
- **Expected behavior (reference):** same.
- **Why it matches:** implemented model and contract agree with the new reference rule.
- **Smallest correction boundary:** none.

### F-04 — Resumo as a persisted record — DOCUMENTATION STALE ONLY (contract) + FUTURE WORK ONLY (record)

- **Exact file(s):** `plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md` §2.3 / §29 / §31.1 and
  Appendix `Q-SCOPE`; `src/DMO.Application/ControloCreate/IProductionResumoRead.cs`.
- **Current behavior:** the app implements the Resumo as a **read projection** only. There is no
  `resumo` table, column or route; the app contract records the persisted `resumo_id` record as an
  "unimplemented P2-T05 handoff remainder".
- **Expected behavior:** reference `architecture/RECORD_LIFECYCLES.md` §8 and
  `contracts/IDENTITIES_AND_RELATIONSHIPS.md` define `resumo_id` as a **persisted** Controlo record
  for one `jobon_id` ("Resumo is persisted and not a projection of Folha",
  `ACCEPTANCE_MATRIX.md`), distinct from `controlo_sheet_id`. The read-projection entry surface does
  not contradict this; the missing persisted record does.
- **Why it conflicts:** the app's "role as entry point is fixed by authority only" is consistent
  for the *entry role*, but the reference treats the Resumo as a real record with its own identity.
- **Smallest correction boundary:** one authored, reviewed contract slice (same P2-T05 family,
  separate PLAN ACCEPT) covering the `resumo_id` persistence + route. No schema or code change is
  authorized by this report.

### F-05 — Repairer family ownership re-homed to Boquilhas Definições — ALREADY ALIGNED

- **Exact file(s):** `src/DMO.Application/Boquilhas/IBoquilhasDefinicoesService.cs`,
  `src/DMO.Application/Boquilhas/BoquilhasDefinicoesService.cs`,
  `src/DMO.Application/Boquilhas/BoquilhasDefinicoesModels.cs`,
  `src/DMO.Web/Endpoints/BoquilhasDefinicoesEndpoints.cs` (`/boquilhas/definicoes`),
  `src/DMO.Web/Pages/Boquilhas/Definicoes.cshtml(.cs)`, `src/DMO.Web/Program.cs` (DI),
  P2-T05 §31.3 / P2-T07 §34.3.
- **Current behavior:** the repairer register and the independent per-machine (`B1..C3`) assignments
  are served and gated under `Boquilhas > Definições`; the Controlo repairer routes were removed
  (the Controlo service methods still exist but have **no production route** and **no UI**).
- **Expected behavior (reference):** Boquilhas / Definições owns the repairer directory and the
  machine/line → repairer assignment; not Admin, not Controlo.
- **Why it matches:** the app already implements the ownership re-homing (it went beyond the
  reference's wording-only state, which is compatible with it).
- **Smallest correction boundary:** none functional; see F-06 for the residual dead surface.

### F-06 — Residual unreachable Controlo repairer service surface — IMPLEMENTATION MISMATCH (dead surface / residual ownership) — LOW SEVERITY

- **Exact file(s):** `src/DMO.Application/ControloCreate/IControloDefinicoesService.cs`
  (`ListRepairersAsync`, `CreateRepairerAsync`, `RenameRepairerAsync`,
  `ListMachineAssignmentsAsync`, `SetMachineAssignmentAsync`, `ClearMachineAssignmentAsync`),
  `src/DMO.Application/ControloCreate/ControloDefinicoesService.cs` (same methods),
  `src/DMO.Application/ControloCreate/ControloDefinicoesModels.cs` (repairer commands/results),
  `src/DMO.Application/ControloCreate/ControloDefinicoesValidator.cs` (repairer/machine validation).
- **Current behavior:** the Controlo Definições **service** still exposes and implements the full
  repairer family; only its **routes and page sections** were removed. No production caller remains
  (the routes are gone, the Controlo page sections are gone), but the interface keeps advertising
  Controlo-owned repairer operations.
- **Expected behavior (reference):** repairer directory + machine/line assignment are Boquilhas
  concerns; Controlo configuration should not present a repairer surface.
- **Why it conflicts:** an unreachable interface surface that still declares Controlo ownership of
  the repairer family is residual authority leakage and a future re-introduction hazard; it is not
  a runtime behavior defect.
- **Smallest correction boundary:** one narrow cleanup slice that removes the six repairer-family
  members (and their now-unused carriers/validators) from the Controlo Definições service, keeping
  PDF directory / email lists / email templates / glass density. No schema change (the physical
  `repairers`/`machine_repairer_assignments` tables stay; they are shared with Boquilhas
  repositories). This report does **not** implement it.

### F-07 — Repairer domain namespace remains `DMO.Domain.Controlo` — DOCUMENTATION/CONVENTION STALE ONLY — LOW SEVERITY

- **Exact file(s):** `src/DMO.Domain/Controlo/Repairer.cs`,
  `src/DMO.Domain/Controlo/RepairerId.cs`, `src/DMO.Domain/Controlo/MachineRepairerAssignment.cs`;
  `src/DMO.Application/Boquilhas/BoquilhasService.cs` and
  `src/DMO.Application/Boquilhas/BoquilhasDefinicoesService.cs` (both `using DMO.Domain.Controlo;`).
- **Current behavior:** the canonical repairer types live in the `DMO.Domain.Controlo` namespace and
  their XML docs still say "owned by Controlo_Create → Definições" (e.g. `RepairerId.cs`), while the
  owning surface is now Boquilhas.
- **Expected behavior (reference):** the repairer directory is a Boquilhas operational concern.
- **Why it conflicts:** naming/docs assert the superseded owner; misleading for future work.
- **Smallest correction boundary:** documentation/namespace-note update only; a physical namespace
  move is a larger, separately-authorized refactor (must not break IDs or the shared tables).

### F-08 — BOQUILHAS movement vocabulary DIVERGES from canonical reference — IMPLEMENTATION MISMATCH — HIGH SEVERITY

- **Exact file(s):** `src/DMO.Domain/Boquilhas/MovementKind.cs` (three tokens
  `saida | entrada | entrada_sem_reparacao`; `inicio` and `irreparavel` explicitly "Superseded"),
  `src/DMO.Application/Boquilhas/BoquilhasModels.cs` §"Superseded (Owner clarification)",
  `plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md` §33.1, `reports/P2_T07_FINAL_INDEPENDENT_REVIEW.md`.
- **Current behavior:** exactly **three** write movement types (Saída, Entrada, Entrada sem
  reparação); no Início; no Irreparável; no close/reopen; outstanding derived by replay.
- **Expected behavior (reference):** `modules/BOQUILHAS.md` §"Movement vocabulary" fixes **exactly
  four** write movement types — `Início`, `Saída`, `Entrada`, `Irreparável` — with `Editar` as an
  action, and a four-bucket derived balance (Disponível / Em reparação / Irreparável / Entrada
  excecional) plus open/close/reopen in `architecture/RECORD_LIFECYCLES.md` §9.
- **Why it conflicts:** the app implements a **different Boquilhas domain model** than the current
  canonical reference. This is not stale wording; it is a semantic contradiction of both the
  movement vocabulary and the balance model.
- **Escalation:** under `AUTHORITY.md` "Conflict rule", a Beta document vs the canonical Beta
  document conflict must be escalated and resolved explicitly. The app's model appears to originate
  from a later-dated local Owner clarification that is **not present in `reference/main`**. Either
  the reference must be updated to record the same clarification, or the app must be corrected to
  the reference's four types. This report does **not** choose; it records the conflict.
- **Smallest correction boundary:** authority-level first (one Owner/reference decision recording
  which vocabulary is canonical), then either a reference doc update or a scoped P2-T07 correction.
  No code change is authorized here.

### F-09 — BOQUILHAS standalone flow wording vs provisional anchor — DOCUMENTATION STALE ONLY (resolved by reference's own v2 clarification, still to be recorded) — MEDIUM

- **Exact file(s):** `plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md` §34,
  `src/DMO.Application/Boquilhas/BoquilhasModels.cs` (transitional `pending_tool_id` anchor),
  `src/DMO.Domain/Boquilhas/BoquilhaRegister.cs`.
- **Current behavior:** the app implements the **provisional pré-JobOn `tool_id` anchor** with
  human-confirmed association at the matching `bq_id` — i.e. the *newer* model.
- **Expected behavior (reference):** `contracts/IDENTITIES_AND_RELATIONSHIPS.md` and
  `modules/BOQUILHAS.md` §"Standalone flow" still describe a **legitimate permanent standalone**
  (`boquilhas_id → tool_id`, "No fake Job On or fake `bq_id` is created"), with no provisional/
  transitional wording and no association step.
- **Why it conflicts:** the reference and the app describe two different standalone semantics.
- **Smallest correction boundary:** record the association clarification in `reference/main`
  (`modules/BOQUILHAS.md` + `contracts/IDENTITIES_AND_RELATIONSHIPS.md`) or correct the app; this is
  an authority-recording decision, not a code defect per se. No code change authorized here.

### F-10 — Beta scope: Reparação Interna not pulled into the flow — ALREADY ALIGNED

- **Exact file(s):** `plans/BETA_IMPLEMENTATION_MASTER_PLAN.md` §14.1/§14.2 (protected identities
  `armazem`, `reparacao-interna`, `reparacao-programada-*`, `tampoes`, `historia`; "no routes, no
  availability, no deletion"), `src/DMO.Application/Access/ModuleCatalog.cs` (identities only).
- **Current behavior:** Armazém / Reparação Interna / Reparação Programada are inert identities
  with no route and no availability; the cross-module flow contains no Reparação Interna
  dependency.
- **Expected behavior (reference):** same (the reference removed the Reparação Interna arrow).
- **Why it matches:** the app never pulled these modules into the Beta flow.
- **Smallest correction boundary:** none.

### F-11 — Master plan Appendix A audit-trail rows still assert Controlo repairer ownership — DOCUMENTATION STALE ONLY

- **Exact file(s):** `plans/BETA_IMPLEMENTATION_MASTER_PLAN.md` Appendix A rows
  `9.14` ("register/assignments owned by `Controlo_Create → Definições`"), `delta §3`
  ("owner = Controlo_Create → Definições"), `delta §4` ("P2-T05 (configuration)"); likewise
  `plans/beta-workstreams/P2-T05-CONTROLO-CREATE.md` line ~175.
- **Current behavior:** these audit rows record the superseded ownership, while the operationally
  binding rows (§1491/§1493) and the contracts P2-T05 §31.3 / P2-T07 §34.3 already record the
  Boquilhas ownership.
- **Expected behavior:** the audit rows should not assert Controlo ownership of the repairer family.
- **Why it conflicts:** documentation inconsistency inside the same file set; lower risk because the
  binding rows are correct.
- **Smallest correction boundary:** edit those Appendix A rows to point at the Boquilhas ownership
  and the supersession record. Documentation-only.

### F-12 — `reports/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_DELTA.md` §3/§4/§12 assert Controlo repairer ownership — DOCUMENTATION STALE ONLY (deliberately unedited)

- **Exact file(s):** `reports/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_DELTA.md` lines ~163, ~205–208,
  ~251, ~561 (V3), ~612.
- **Current behavior:** the delta still states "the repairer register is owned by
  `Controlo_Create → Definições`" and lists repairers under the extended P2-T05 scope.
- **Expected behavior:** the repairer family is owned by Boquilhas. The OWNER-clarification response
  explicitly chose **not** to edit this report (it is a historical report, not a contract/plan), so
  the staleness is recorded, not edited.
- **Why it conflicts:** a reader may take the report at face value.
- **Smallest correction boundary:** none required (report is historical evidence); optionally add a
  supersession header pointing to P2-T05 §31.3 / P2-T07 §34.3. Handled by F-14.

### F-13 — `P2_T07_FINAL_INDEPENDENT_REVIEW.md` "VERIFIED" status — DOCUMENTATION STALE ONLY

- **Exact file(s):** `reports/P2_T07_FINAL_INDEPENDENT_REVIEW.md`,
  `plans/BETA_IMPLEMENTATION_MASTER_PLAN.md` Appendix A `9.13`/`9.14`.
- **Current behavior:** records P2-T07 as VERIFIED/CLOSED under the **three-type** model.
- **Expected behavior:** against the canonical four-type reference model, a "VERIFIED — CLOSED"
  claim needs a supersession note pointing at the unresolved vocabulary conflict (F-08).
- **Why it conflicts:** the closure evidence rests on a model that now conflicts with the reference.
- **Smallest correction boundary:** add a supersession pointer to this report; do not rewrite the
  original verification (it must remain auditable).

### F-14 — `BETA_MASTER_RECONCILIATION.md` and historical reports — NO ACTION REQUIRED

- **Exact file(s):** `reports/BETA_MASTER_RECONCILIATION.md` and the dated verification reports.
- **Current behavior:** these are audited current-state reports of specific commits.
- **Expected behavior:** remain untouched; rewriting them would corrupt their evidence.
- **Smallest correction boundary:** none (already the recorded policy).

### F-15 — `CurrentBuildAvailable` stays empty; no routes registered — ALREADY ALIGNED

- **Exact file(s):** `src/DMO.Application/Access/ModuleRegistrations.cs`
  (`CurrentBuildAvailable { get; } = []`), `Program.cs` (no availability/destination
  registration).
- **Current behavior:** no Beta operational module is marked available; every operational route is
  server-gated and denied until P2-T10.
- **Expected behavior:** same.
- **Smallest correction boundary:** none.

---

## 4. Workstream impact

### P2-T04 Job On / Ferramentas

- F-01, F-02, F-03, F-10, F-15: **ALREADY ALIGNED**. Job On is the Tool-selection centre, Tool
  creation has no Armazém dependency, non-Beta modules are inert.
- F-04 (Resumo persisted record): the Resumo's *role* is registered in this workstream's contract
  §24; the persisted record is a P2-T05 slice.

### P2-T05 Controlo Create

- F-05: repairer family already removed from the Controlo surface (routes/page); **ALREADY
  ALIGNED**.
- F-06: residual unreachable repairer members in `IControloDefinicoesService` — **IMPLEMENTATION
  MISMATCH (dead surface)**.
- F-07: repairer domain namespace still `DMO.Domain.Controlo` — **CONVENTION STALE**.
- F-04: persisted `resumo_id` record still absent — **FUTURE WORK ONLY** (own PLAN ACCEPT).
- F-11, F-12: **DOCUMENTATION STALE ONLY**.

### P2-T06 Controlo Approve

- No settings surface; consumes the same submitted `peso_id` — **ALREADY ALIGNED**. The reference
  change does not reopen D. Only the audit wording in `P2-T06_CONTROLO_APPROVE_CONTRACT.md` §"no
  settings here" remains correct under both owners.

### P2-T07 Boquilhas

- F-05: repairer ownership + Definições surface — **ALREADY ALIGNED**.
- F-06/F-07: source-level ownership residuals affecting E's consumed register — **LOW severity**.
- F-08: movement vocabulary (4 vs 3) — **IMPLEMENTATION MISMATCH / AUTHORITY CONFLICT (HIGH)**.
- F-09: standalone vs provisional anchor — **DOCUMENTATION/AUTHORITY STALE (MEDIUM)**.
- F-13: verification status wording — **DOCUMENTATION STALE ONLY**.

### P2-T08 Documents

- Not implemented. F-04 (Resumo persistence) and the reference's repaired "Resumo is persisted and
  not a projection of Folha" (`ACCEPTANCE_MATRIX.md`) directly constrain the DMO/P2-T08 document
  workstream, which must derive the Resumo document from the persisted record rather than a
  projection. **FUTURE WORK ONLY**, but a hard input to the P2-T08 contract.
- The PDF/email settings remain under Controlo (unchanged by this delta).

### P2-T10 Integration

- F-15: no availability/route registered — **ALREADY ALIGNED** (nothing to change for this delta).
- F-08/F-09 must be resolved before end-to-end acceptance, otherwise P2-T10 would verify a
  Boquilhas model that conflicts with the canonical reference.

---

## 5. Repairer ownership migration delta — inventory

Everything below currently **assigns repairer settings to Controlo** and must be treated as
migrating conceptually to Boquilhas (nothing here is implemented by this report).

### 5.1 Contracts / plans that still assert Controlo ownership

| Artifact | Location | Current statement | Status |
|---|---|---|---|
| Master plan Appendix A | `plans/BETA_IMPLEMENTATION_MASTER_PLAN.md` row `9.14` | "register/assignments owned by `Controlo_Create → Definições`" | STALE (F-11) |
| Master plan Appendix A | same, row `delta §3` | "owner = Controlo_Create → Definições" | STALE (F-11) |
| Master plan Appendix A | same, row `delta §4` | "P2-T05 (configuration)" for machine assignments | STALE (F-11) |
| P2-T05 workstream plan | `plans/beta-workstreams/P2-T05-CONTROLO-CREATE.md` ~L175 | `Controlo_Create → Definições` with the repairer register | STALE (F-11) |
| P2-T05 contract scope | `plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md` §2.1/§1.1/§10/§11 authority table | register/assignment ownership by Controlo; already superseded by §31.3 | SUPERSEDED in-file; audit rows still stale |
| P2-T04 contract | `plans/contracts/P2-T04_DOMAIN_CORE_TOOL_JOBON_CONTRACT.md` ~L1796 | "owned by `Controlo_Create → Definições` (P2-T05)" | STALE (F-11) |
| P2-T07 contract | `plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md` ~L113/114/478/1634 | "owned by `Controlo_Create → Definições`"; superseded by §34.3 | SUPERSEDED in-file; audit rows still stale |
| P2-T06 contract | `plans/contracts/P2-T06_CONTROLO_APPROVE_CONTRACT.md` ~L284 | "settings owned by `Controlo_Create → Definições`" (repairer family included) | STALE (owner now Boquilhas) |
| Delta report | `reports/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_DELTA.md` §3/§3.6/§4/§12 V3/§14 | "repairer register is owned by `Controlo_Create → Definições`" | STALE, deliberately unedited (F-12) |
| Dual authority ref | `reports/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_DELTA.md` | "1. Controlo_Create owns the settings area" (umbrella) | Partially stale (repairer family only) |

### 5.2 Source code / persistence objects

| Object | Location | Current owner-facing namespace/route | Migration note |
|---|---|---|---|
| `IControloDefinicoesService` repairer members | `src/DMO.Application/ControloCreate/IControloDefinicoesService.cs` | Controlo (unreachable) | remove/relocate (F-06) |
| `ControloDefinicoesService` repairer methods | `src/DMO.Application/ControloCreate/ControloDefinicoesService.cs` | Controlo (unreachable) | remove/relocate (F-06) |
| Repairer commands/results in Controlo | `src/DMO.Application/ControloCreate/ControloDefinicoesModels.cs` | Controlo | duplicated by Boquilhas carriers (F-06) |
| Ready-only | `src/DMO.Common/...` repairer carriers | Controlo | relocate/keep as shared (F-06/F-07) |
| Repairer/machine validation | `src/DMO.Application/ControloCreate/ControloDefinicoesValidator.cs` | Controlo | still referenced by Boquilhas validator for tokens (F-06/F-07) |
| `Repairer`, `RepairerId`, `MachineRepairerAssignment` | `src/DMO.Domain/Controlo/*` | `DMO.Domain.Controlo` | namespace/docs stale (F-07) |
| `IRepairerRepository`, `IMachineRepairerAssignmentRepository` | `src/DMO.Application/Repositories/*` | shared | fine (shared seam) |
| `RepairerRepository`, `MachineRepairerAssignmentRepository`, entities/configurations | `src/DMO.Infrastructure/Persistence/*` | shared tables `repairers`, `machine_repairer_assignments` | keep (no schema change) |
| `ControloDefinicoesEndpoints` repairer routes | `src/DMO.Web/Endpoints/ControloDefinicoesEndpoints.cs` | removed | already moved |
| `BoquilhasDefinicoesEndpoints` | `src/DMO.Web/Endpoints/BoquilhasDefinicoesEndpoints.cs` | `/boquilhas/definicoes` | current owner |

### 5.3 Pages / UI

| Surface | Location | Status |
|---|---|---|
| Controlo Definições repairer sections | `src/DMO.Web/Pages/Controlo/Definicoes.cshtml(.cs)` | removed; remaining page has a supersession note (correct) |
| Boquilhas Definições | `src/DMO.Web/Pages/Boquilhas/Definicoes.cshtml(.cs)` | current owner surface |
| Boquilhas movement form (repairer field) | `src/DMO.Web/Pages/Boquilhas/Novo.cshtml(.cs)`, `Index.cshtml(.cs)` | consumes resolved repairer |

### 5.4 Tests still naming Controlo repairer ownership

| Test | Location | Status |
|---|---|---|
| `ControloSettingsRepositoryIntegrationTests` | `tests/DMO.IntegrationTests/Persistence/ControloSettingsRepositoryIntegrationTests.cs` | exercises repairer/machine methods through `IControloDefinicoesService` (name/location stale; methods still exist) — TEST STALE ONLY |
| `ControloDefinicoesValidatorTests` | `tests/DMO.UnitTests/ControloCreate/ControloDefinicoesValidatorTests.cs` | repairer/machine validation tokens — TEST STALE ONLY |
| `MachineAndSettingsShapeTests` | `tests/DMO.UnitTests/ControloCreate/MachineAndSettingsShapeTests.cs` | repairer/assignment shapes under `ControloCreate` namespace — TEST STALE ONLY |
| `ControloDefinicoesEndpointsTests` | `tests/DMO.IntegrationTests/ControloCreate/ControloDefinicoesEndpointsTests.cs` | already documents the move (correct) |
| `BoquilhasDefinicoesEndpointsTests` | `tests/DMO.IntegrationTests/Boquilhas/BoquilhasDefinicoesEndpointsTests.cs` | current-owner proofs (correct) |
| `ControloCreateAccessTests` / `ControloApproveAccessTests` | `tests/DMO.IntegrationTests/Controlo*/…` | assert `/controlo/create/definicoes/repairers` is gone / `/boquilhas/definicoes/repairers` denied without grant (correct) |
| `P2T05ProductionScan` | `tests/DMO.IntegrationTests/ControloCreate/P2T05ProductionScan.cs` | still scans Controlo for repairer machinery; carries a move note |

---

## 6. Job-On-centered flow delta — current code path trace

### 6.1 Job On → CM → Resumo → Peso

```text
Job On (jobon_id)
  → Job On Create/Edit selects/creates canonical tool_id
  → cm_id context created under jobon_id (P2-T04)
  → Controlo entry: /controlo/create?jobonId={jobon_id}
      Create.cshtml.cs LoadProductionAsync(jobOnId)
        → IProductionResumoRead.GetResumoAsync(jobOnId)
            ONE statement: job_ons LEFT JOIN cm_contexts LEFT JOIN tools
            returns ProductionResumoReadModel(reference, productionNumber, machine, date, cm)
        → production context + CM context populate the surface
        → ToolSummaryRow built from the CM's live Tool facts (reference, lot, processo)
  → Peso create carrier: CmId (or PendingToolId when truthful CM unknown)
      operator supplies ONLY Peso measurement data
  → same peso_id submitted; Approve reviews the same record
```

**Matches the reference at:** entry through the Resumo-da-produção projection; Peso populated from
Job On/CM context; no redundant re-entry of machine/reference/lot/processo; pending `tool_id` anchor
for a truthful pre-JobOn Peso with human-confirmed association on the same UUID; no global scans
(single surgical statement).

**Diverges from the reference at:** the Resumo is a read projection, not the persisted `resumo_id`
record the reference defines (`RECORD_LIFECYCLES.md` §8, `ACCEPTANCE_MATRIX.md`). See F-04.

### 6.2 Job On → BQ → Boquilhas

```text
Job On (jobon_id)
  → bq_id context created under jobon_id (P2-T04) for the canonical BQ tool_id
  → Boquilhas consumes:
       production-linked register anchor = REAL bq_contexts row (bq_id)
       OR, pré-JobOn, provisional pending_tool_id anchor
  → when a bq_id whose bq_contexts.tool_id == pending tool_id arrives:
       AssociationCandidates presented → human confirmation (AssociateAsync)
       SAME boquilhas_id → bq_id → jobon_id; provisional tool_id cleared
  → movements recorded against the register
```

**Matches the reference at:** Job On is the planning centre; Boquilhas consumes the BQ context and
does not reselect the Tool; production-linked flow via `bq_id`.

**Diverges from the reference at:**
1. The reference's canonical Boquilhas movement vocabulary is `Início | Saída | Entrada |
   Irreparável` (four) and its record lifecycle includes open/close/reopen; the app implements
   `saida | entrada | entrada_sem_reparacao` (three) and no lifecycle (F-08, HIGH).
2. The reference still describes a legitimate permanent standalone flow
   (`boquilhas_id → tool_id`, no association); the app implements a transitional provisional anchor
   with association and explicitly no permanent standalone (F-09).

---

## 7. Safe next tasks

Ordered; each is analysis/planning/verification only and changes no production behavior. Each
resolves a concrete finding above.

1. **Resolve F-08 (authority decision).** Produce an authority comparison note that places the
   reference's four-movement model and the app's three-movement model side by side for
   Owner/Architect resolution, and decide whether `reference/main` records the newer clarification
   or `app` returns to the reference vocabulary. Output: a decision record; no code.
2. **Resolve F-09 (authority recording).** Add the provisional-anchor / association semantics to
   `reference/main` (`modules/BOQUILHAS.md`, `contracts/IDENTITIES_AND_RELATIONSHIPS.md`) or record
   the reference wording as superseded in the app. Output: authority wording only.
3. **F-04 Resumo persistence analysis.** Analyse the persisted `resumo_id` record requirements
   (identity, `resumo_id → jobon_id`, Folha distinction, document derivation) and produce the
   scoped planning input for its own P2-T05-family contract. Output: planning slice; no schema.
4. **F-06 dead-surface verification.** Produce a verification note proving the six Controlo
   repairer service members are unreachable from every production route/page/DI path, and define
   the minimal removal boundary for a later authorized cleanup. Output: verification report.
5. **F-11/F-12/F-13 stale-wording sweep.** Enumerate the exact audit rows/report lines that still
   assert Controlo repairer ownership and add supersession pointers. Output: documentation diff
   plan (no behavior change).
6. **F-07 ownership naming note.** Define the convention for the repairer domain types under a
   Boquilhas-owned surface (namespace/doc note) and the constraints that a later move must not break
   shared tables or IDs. Output: convention note.

---

## 8. Stop condition

STOP after writing and committing this report. No discovered mismatch was fixed. This report
changes no production code, migration, frontend, route, authorization, contract or accepted report.

---

## 9. Verification statement

- Application code modified: **NO**.
- Database schema/migration modified: **NO**.
- Frontend modified: **NO**.
- Existing accepted reports or contracts modified: **NO**.
- Module availability / routes registered: **NO**.
- Only this report was added (and committed/pushed as instructed).
