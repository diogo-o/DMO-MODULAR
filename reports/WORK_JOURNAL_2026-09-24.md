# WORK JOURNAL — 2026-09-24

Running handoff journal. Append-only. One short entry per task. Not an audit report — each entry
points to the detailed artifact instead of duplicating it.

Repo roles: `dgains00-create/app` = IMPLEMENTATION + historical decisions; `dgains00-create/reference`
= CANONICAL BETA REFERENCE. `diogo-o/*` retained as `upstream` remotes.

---

## ~13:30 — Workspace / Git setup (repos + remotes)

* **Task:** Prepare a GitHub workspace for the DMO Beta work; mirror the two source repos into
  `dagains00-create` with full history, preserving a clean mapping.
* **Files/reports created:** none (Git state only).
* **What was learned or changed:** cloned `diogo-o/DMO-MODULAR` → `dagains00-create/app` and
  `diogo-o/dmo-beta-master` → `dagains00-create/reference` at full history; `audit` clone present.
* **Important authority/architecture impact:** none (no code); establishes the two-repo working model
  (app = implementation, reference = canonical Beta authority).
* **Decisions resolved:** confirmed mapping DMO-MODULAR → `app`, dmo-beta-master → `reference`.
* **Decisions still open:** none.
* **Follow-up recommended:** keep `upstream` remotes for provenance; never push agent work to
  `reference` without explicit instruction.

---

## ~13:57 — Recent Beta authority delta analysis

* **Task:** Identify the 2026-09-24 authority delta in `reference` and classify its impact on `app`
  plans/contracts/reports/code (analysis only).
* **Files/reports created:** `reports/RECENT_BETA_AUTHORITY_DELTA_2026-09-24.md` (commit `2206ce9`).
* **What was learned or changed:** the four reference commits (`398d509` Job On-centred flow,
  `e160b8c` Beta-scope boundary, `f977d86` Tool without Armazém, `cca33d4` repairer settings →
  Boquilhas) were mapped against `app`. Job On entry, Controlo-through-Resumo, Tool-without-Armazém,
  and Beta-scope findings are ALREADY ALIGNED. `app` HEAD (`a59d8c1`) is chronologically later than
  `reference` HEAD (`cca33d4`).
* **Important authority/architecture impact:** two real divergences found — F-08 (Boquilhas movement
  vocabulary: reference four types vs app three types) and F-09 (standalone vs provisional anchor);
  plus F-06 (residual Controlo repairer surface) and F-04 (persisted Resumo absent).
* **Decisions resolved:** none — recorded findings only.
* **Decisions still open:** which Boquilhas movement vocabulary is canonical; standalone vs
  transitional anchor; persisted-Resumo slice.
* **Follow-up recommended:** recover the decision history behind the app's Boquilhas model.

---

## ~14:07 — Boquilhas authority-resolution pack

* **Task:** Recover exactly how and why `app` moved away from the `reference` Boquilhas model
  (analysis only, no model chosen).
* **Files/reports created:** `reports/BOQUILHAS_AUTHORITY_RESOLUTION_PACK.md` (commit `c56316a`).
* **What was learned or changed:** recovered the decision timeline from the original four-type/
  standalone/lifecycle contract (`dcca794`, `b884dd8`, `c6a87dd`, `3ab6dd4`) through two Owner
  clarifications (`caad792` contract §33 = production movement register; `f7aa6ee` contract §34 =
  pré-JobOn provisional anchor + repairer re-homing) and their implementation (`a59d8c1`).
* **Important authority/architecture impact:** all movement/lifecycle/standalone differences trace to
  Owner directives recorded **only in `app`**; only repairer ownership has independent canonical
  corroboration in `reference` (`cca33d4`). Not an implementation-agent invention; not test-driven.
* **Decisions resolved:** provenance of each difference documented (Owner instruction / Architect /
  contract correction). No model preference asserted.
* **Decisions still open:** three binary Owner decisions (movement vocabulary; lifecycle; standalone
  semantics) + where the confirming clarification should be recorded.
* **Follow-up recommended:** obtain the Owner binary decisions before any Boquilhas authority or code
  change.

---

## ~14:18 — Resumo persistence planning analysis

* **Task:** Determine the smallest future slice for a persisted `resumo_id` without replacing the
  existing `IProductionResumoRead` projection (planning only).
* **Files/reports created:** `reports/RESUMO_PERSISTENCE_PLANNING_ANALYSIS.md` (commit `6c39a48`).
* **What was learned or changed:** the Resumo is canonical as `resumo_id → jobon_id + applicable
  component contexts` (persisted record, distinct from Folha) but is implemented only as a read
  projection. The projection is a view and must be preserved; the record is a separate business
  object. The shared `P2T05TestStore` also implements `IProductionResumoRead`.
* **Important authority/architecture impact:** minimum relation to Peso is **traversal through
  `jobon_id`** — no `peso_id` FK and no `resumo_id` on `pesos`. Document rule: record != PDF != path;
  base directory comes from configured `Controlo_Create → Definições`.
* **Decisions resolved:** projection and record coexist; schema candidate (own facts only) and
  service boundary drafted; reuse dispositions fixed.
* **Decisions still open:** D1 uniqueness, D2 create trigger/timing, D3 editability/rebuild,
  D4 lifecycle vocabulary (no safe default), D5 context set/snapshot, D6 explicit Peso relation.
* **Follow-up recommended:** author the persisted Resumo record core slice (no PDF rendering) after
  the open decisions, especially D4.

---

## ~14:26 — Controlo repairer dead-surface verification

* **Task:** Prove whether the residual Controlo repairer service surface (finding F-06) is genuinely
  dead in production and define the smallest safe cleanup boundary (verification only).
* **Files/reports created:** `reports/CONTROLO_REPAIRER_DEAD_SURFACE_VERIFICATION.md` (commit
  `90925bf`).
* **What was learned or changed:** all six Controlo members (`ListRepairersAsync`,
  `CreateRepairerAsync`, `RenameRepairerAsync`, `ListMachineAssignmentsAsync`,
  `SetMachineAssignmentAsync`, `ClearMachineAssignmentAsync`) have **zero production callers** — no
  route, page handler, endpoint mapping, service-to-service call, background process, DI consumer or
  reflection; only two tests construct `ControloDefinicoesService` directly.
* **Important authority/architecture impact:** Boquilhas Definições fully serves the live feature
  over the **same shared repositories/tables**. Cleanup needs **no** migration/schema/data work.
  However `BoquilhasDefinicoesService`/`BoquilhasDefinicoesEndpoints` import
  `ControloDefinicoesValidationErrors.{NameRequired, RepairerNotFound, MachineUnknown}`, so the dead
  surface cannot be deleted without first extracting those shared tokens.
* **Decisions resolved:** verdict = **B. CLEANUP REQUIRES SHARED EXTRACTION FIRST**; dead members and
  dead carriers enumerated; cleanup boundary (remove / delete-if-unused / extract / keep) defined.
* **Decisions still open:** which extraction option (move the whole errors class vs extract the three
  repairer codes); when to schedule the cleanup.
* **Follow-up recommended:** do the small shared error-token extraction, then a narrow member removal
  slice; migrate the repairer cases of `ControloSettingsRepositoryIntegrationTests` and
  `ControloDefinicoesValidatorTests` to the Boquilhas service.

---

## ~14:35 — Controlo repairer dead-surface verification (expanded checklist re-run)

* **Task:** Re-run the F-06 dead-surface verification against an expanded checklist (private helpers,
  DI dependencies, module availability as a separate risk row) and anchor the baseline at current
  `app/main`.
* **Files/reports created:** updated `reports/CONTROLO_REPAIRER_DEAD_SURFACE_VERIFICATION.md`
  (commit `3d955fc`); this journal entry.
* **What was learned or changed:** no change to the verdict or findings — the re-run **confirmed**
  the prior result. Added §2.4 (DI dependencies: the two repository registrations stay because
  Boquilhas resolves them; only the Controlo service's `_repairers`/`_assignments` fields become
  unused), §2.5 (private helpers `AssertVersion`/`Refuse`/`Map` are all shared with the retained
  PDF/email/glass members — none is exclusive to the repairer family), and a distinct §9
  module-availability row (`CurrentBuildAvailable` stays `[]`).
* **Important authority/architecture impact:** none new. Confirms `Boquilhas > Definições` is the
  sole operational owner and the Controlo residual is dead; cleanup is a code move, not a schema
  change.
* **Decisions resolved:** none new (verdict unchanged at **B. CLEANUP REQUIRES SHARED EXTRACTION
  FIRST**).
* **Decisions still open:** the extraction option for the three shared error tokens; cleanup
  scheduling.
* **Follow-up recommended:** unchanged from the prior entry.
