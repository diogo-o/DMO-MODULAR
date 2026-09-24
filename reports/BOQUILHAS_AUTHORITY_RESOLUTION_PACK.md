# BOQUILHAS AUTHORITY RESOLUTION PACK — 2026-09-24

**Class:** Analysis / authority-recovery. **Not** an implementation plan, **not** a contract,
**not** a decision about which model is correct.

**Scope:** recover the complete decision history that produced the current Boquilhas
implementation, and separate recorded authority from interpretation/invention. No production
code, migration, test or behavior was modified. `reference` was **not** changed. The only file
added is this report.

**Related:** `reports/RECENT_BETA_AUTHORITY_DELTA_2026-09-24.md` (findings F-08, F-09), which this
pack resolves into provenance.

---

## 1. Baselines

| Item | Value |
|---|---|
| `app/main` HEAD (verified against `origin/main`) | `2206ce981cfdc8ea01020fc6d94d423f128bdc93` |
| `reference/main` HEAD (verified against `origin/main`) | `cca33d472830ee52953b3cfa90018d20251d4983` |
| `reference/main` Boquilhas contract commit | `6a27995` (2026-09-22 09:27) "Add canonical Beta Boquilhas contract" |
| `reference/main` repairer-ownership commit | `cca33d4` (2026-09-24 12:35) "Move repairer operational settings into Boquilhas" |
| `app` working tree at start | CLEAN |
| `reference` working tree at start | CLEAN |
| Implementation modified by this task | **NO** (report only) |
| `reference` modified by this task | **NO** |

---

## 2. Decision timeline

Chronological. All commits are in `dgains00-create/app` unless marked **[reference]**.

| # | commit | datetime | artifact | decision introduced | superseded previous rule? | evidence |
|---|---|---|---|---|---|---|
| 1 | `6a27995` **[reference]** | 2026-09-22 09:27 | `reference/modules/BOQUILHAS.md` | Canonical Beta Boquilhas contract: four movement types `Início / Saída / Entrada / Irreparável`; permanent standalone allowed (`boquilhas_id -> tool_id`); four-bucket balance; close/reopen lifecycle; repairer consumed, Admin-owned register | — (initial) | `git log -- reference/modules/BOQUILHAS.md` |
| 2 | `0f370e6` | 2026-09-22 21:51 | `reports/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_DELTA.md` | Repairer register + per-machine assignments owned by `Controlo_Create → Definições`; no line grouping; automatic Boquilhas repairer resolution; historical preservation | — (new authority) | delta §3/§4/§5/§6 |
| 3 | `dcca794` | 2026-09-24 02:36 | `plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md` (new) | Original P2-T07 contract authored: **four** types `inicio\|saida\|entrada\|irreparavel`; exclusive anchor `bq_id XOR tool_id` (incl. permanent standalone); four-bucket balance + excess facts; close/reopen on same `boquilhas_id`; repairer consumed from Controlo; **six tables**; **18 routes** | — (first contract) | authoring response §3/§4; commit message |
| 4 | `b884dd8` | 2026-09-24 03:56 | same contract | Focused **B1** correction only (partial unique indexes `IX_boquilhas_active_*`, 23505 mapping, K6–K8 race rows). Architect PLAN REJECT `542a08a1` — B1 only; 12 authority questions ACCEPT DEFAULT | no (implementation-contract defect) | authoring response §12 |
| 5 | `c6a87dd` | 2026-09-24 05:45 | `src/**`, `tests/**`, migration `20260924031924_BoquilhasDomain` | Implemented the **four-type / standalone / lifecycle** model: 6 tables, 2 active-anchor partial unique indexes, 18 routes, K6–K8 | no | response §2/§3/§4 |
| 6 | `3ab6dd4` | 2026-09-24 05:45 | `dev/responses/P2_T07_IMPLEMENTATION_RESPONSE.md` | Implementation record: unit 644/644, integration 646 | no | response §§1–20 |
| 7 | **`caad792`** | 2026-09-24 06:52 | contract **§33**; `src/DMO.Domain/Boquilhas/MovementKind.cs`; migration corrected to `20260924051151_BoquilhasDomain`; response **§21** | **NEW OWNER CLARIFICATION** (recorded as direct): Boquilhas = **production movement register**. Removes standalone, `Início`, `Irreparável` (→ **Entrada sem reparação**), the whole open/closed lifecycle, the B1 active-anchor machinery, the four-bucket balance (→ single replay-derived **outstanding**), register machine set/opening facts. Final: **3 types**, **3 tables**, **15 routes** | **YES** — explicitly supersedes §7.2 B1 correction and the affected §§; old verification gate NOT run | contract §33.1 (1–7); `git show caad792 -- MovementKind.cs` |
| 8 | `d00177d` | 2026-09-24 06:52 | master plan B3 rows + §7; workstream §16; response §21 | Governance records for §33 | no | commit `d00177d` |
| 9 | `a96814f` | 2026-09-24 07:19 | `reports/P2_T07_FINAL_INDEPENDENT_REVIEW.md` | Independent verification of the §33 model → **VERIFIED**, recommends **CLOSED**. Old 83/86 matrix deliberately not reconstructed; no Architect implementation review required under the simplified workflow | no | review §0/§15 |
| 10 | `43b7ab4` | 2026-09-24 07:55 | master plan / workstream plans | Cleanup of superseded implementation authority; P2-T04…T07 recorded CLOSED | no | commit `43b7ab4` |
| 11 | **`f7aa6ee`** | 2026-09-24 12:52 | contract **§34**; P2-T05 **§31**; P2-T04 **§24**; `dev/responses/OWNER_CLARIFICATION_PLANNING_CONTEXT_AND_ASSOCIATIONS_RESPONSE.md` | **NEW OWNER CLARIFICATION** (registered on clean baseline `c01d836`, authority only, no code): Boquilhas may start **pré-JobOn provisionally anchored on canonical `tool_id`**; association when a `bq_id` with the **same master `tool_id`** arrives; **same `boquilhas_id`** passes to `bq_id → jobon_id`; provisional anchor cleared. **Does NOT recreate a permanent standalone.** Also: repairer family moves to `Boquilhas > Definições` | **YES** — supersedes contract §33.1 item 1 ("there is NO standalone (`tool_id`) anchor") and the affected §33 wording; for repairer ownership, supersedes delta §3/§4 | contract §34.1/§34.2; response §2/§3 (C1–C6) |
| 12 | `a59d8c1` | 2026-09-24 14:11 | migration `20260924130151_BoquilhasPreJobonAssociation`; Boquilhas domain/app/web; `/boquilhas/definicoes` | **Implemented the §34 delta**: `bq_id` nullable + `tool_id` FK + `boquilhas_anchor_check ((bq_id IS NULL) <> (tool_id IS NULL))`; `AssociateAsync` with `already-associated` / `association-mismatch` refusals; repairer family physically re-homed to Boquilhas Definições; Controlo repairer routes 13/14 removed | executes §34 (which had superseded §33.1 item 1) | commit `a59d8c1` message; `BoquilhasModels.cs`; `BoquilhasDefinicoesEndpoints.cs` |

### 2.1 Recovered Owner-clarification commits (app)

| Clarification | Commit recorded | Where recorded | Implementation commit |
|---|---|---|---|
| §33 — production movement register (3 types, no lifecycle, no standalone) | `caad792` (+ governance `d00177d`) | contract `plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md` §33; response §21; workstream §16 | `caad792` (same commit) |
| §34 — pré-JobOn provisional `tool_id` anchor + association + repairer re-homing | `f7aa6ee` (authority only) | contract §34; P2-T05 §31; P2-T04 §24; `dev/responses/OWNER_CLARIFICATION_PLANNING_CONTEXT_AND_ASSOCIATIONS_RESPONSE.md` | `a59d8c1` |

**Note:** neither clarification was propagated to the `reference` repository. `reference/main`'s
Boquilhas document (`modules/BOQUILHAS.md`) is still at its 2026-09-22 state (`6a27995`), modified
only on 2026-09-24 12:35 by `cca33d4` for repairer ownership.

---

## 3. Movement-model comparison

Side by side, with provenance. **No preference is stated.**

### 3.0 Summary table

| Aspect | REFERENCE MODEL | CURRENT APP MODEL |
|---|---|---|
| Write movement types | `Início`, `Saída`, `Entrada`, `Irreparável` (four) | `Saída`, `Entrada`, `Entrada sem reparação` (three) |
| Register creation | creates an `Início` opening quantity with the aggregate | creates identity only; manufactures no movement |
| Balance | four derived buckets: Disponível / Em reparação / Irreparável / Entrada excecional; `Saída ≤ Disponível`, `Irreparável ≤ Em reparação`; excess Entrada recorded with expected/excess facts | single derived value: `outstanding = Σ(Saída) − Σ(Entrada) − Σ(Entrada sem reparação)`; no upper-bound rules; negative visible/non-blocking |
| Lifecycle | open/close/reopen on the same `boquilhas_id`, immutable close snapshot, reopening history, aggregate `status` | none — no status, no close, no reopen, no snapshot, no reopening history |
| Standalone | permanent standalone allowed (`boquilhas_id → tool_id`) | no permanent standalone; transitional pré-JobOn anchor only (see §4) |
| Tables | 6 (`boquilhas`, `boquilha_machines`, `boquilha_movements`, `boquilha_movement_audit`, `boquilha_close_snapshots`, `boquilha_reopenings`) | 3 (`boquilhas`, `boquilha_movements`, `boquilha_movement_audit`) |
| Routes | 18 (3 pages + 15 endpoints) | 15 (3 pages + 12 endpoints) |

### 3.1 `Início`

- **Previous behavior (reference + app original):** `Início` was a write movement type whose
  quantity was the aggregate's opening production quantity; created once with the aggregate
  (`only-one-inicio` refusal on later appends).
- **Later Owner clarification:** contract §33.1 item 2 — "**Início as a movement type** — removed.
  Creating/accessing the register MUST NOT generate a quantity movement merely to establish
  existence."
- **Reason recorded:** a register row must not manufacture stock to exist; existence is identity,
  not quantity (contract §33.1 (2); response §21.1).
- **Current implementation consequence:** `MovementKind` has no `Inicio`; `CreateAsync` writes the
  identity row only; the DB CHECK admits only three tokens.
- **Tests proving it:** unit `Tokens_AreExactAndClosed`, `NoLifecycleOrSupersededKinds_Exist`;
  endpoint `T3_RegisterCreation_DoesNotManufactureAMovement`; persistence
  `P1_…CreationManufacturesNoMovement`; endpoint `T7` (superseded types refused),
  direct-SQL 23514.
- **Reference status:** `reference/modules/BOQUILHAS.md` §"Movement vocabulary" and
  `RECORD_LIFECYCLES.md` §9 still list `Início`.

### 3.2 `Irreparável` → `Entrada sem reparação`

- **Previous behavior:** `Irreparável` was a write type meaning "declared irreparable"
  (constrained `≤ Em reparação`), feeding an Irreparável balance bucket.
- **Later Owner clarification:** contract §33.1 item 3 — "Irreparável … removed and REPLACED by
  **Entrada sem reparação**: X boquilhas returned from the repairer but NOT repaired … it does NOT
  mark the Tool irreparable, … does NOT create an irreparable bucket/entity." Recorded rationale
  example: "Entraram 6 T173 que não foram reparadas (não cobráveis)."
- **Reason recorded:** the real-world event is a *return* (quantity comes back), not a permanent
  Tool destruction; no billing mechanic in P2-T07 (contract §33.1 (3)).
- **Current implementation consequence:** token `entrada_sem_reparacao`; subtracts from
  outstanding like an Entrada; Tool row untouched.
- **Tests proving it:** unit `EntradaSemReparacao_ReturnsQuantity`; persistence
  `E1_EntradaSemReparacaoDoesNotMutateTheTool` (real PG); endpoint
  `T11_…DoesNotMutateTheTool`; vocabulary `Tokens_AreExactAndClosed`.
- **Reference status:** `BOQUILHAS.md` still lists `Irreparável`; no `entrada_sem_reparacao` token.

### 3.3 Balance semantics

- **Previous behavior:** four replay-derived buckets with two upper-bound refusals
  (`saida-exceeds-available`, `irreparavel-exceeds-in-repair`) and excess-Entrada
  expected/excess facts.
- **Later Owner clarification:** contract §33.1 item 6 — four-bucket model removed; **only**
  derived value is `outstanding = Σ(Saída) − Σ(Entrada) − Σ(Entrada sem reparação)`, replayed at
  read time, never stored; no `Saída ≤ Disponível` / `Irreparável ≤ Em reparação` rules; negative
  outstanding valid and non-blocking.
- **Reason recorded:** movement facts remain the sole authority; no stock validation is
  authoritative in the register model (contract §33.2).
- **Current implementation consequence:** `OutstandingProjection.Replay`; no balance column/table;
  `BalanceProjection.cs` and the bucket tokens deleted (`git show caad792` deletes
  `BalanceProjection.cs`).
- **Tests proving it:** unit `OwnerExample_ReplaysToZero` (Saída 10 / Entrada 4 / Entrada sem
  reparação 6 → 0), `NegativeOutstanding_IsAValidProjection`; endpoint `T9`, `T10`; repository
  `R1`; `B1_NoSecondBalanceAuthorityExists`.
- **Reference status:** `BOQUILHAS.md` §Balance and `ACCEPTANCE_MATRIX.md` §7 still describe the
  four buckets and the expected/excess facts.

### 3.4 Close / reopen lifecycle

- **Previous behavior:** `active`/`closed` `status`; `Close` writes an immutable snapshot
  (replay-computed buckets + manual utilisation + backend actor/time); `Reopen` requires reason,
  eligibility = closed ∧ last-closed ∧ no other active aggregate; six tables include
  `boquilha_close_snapshots` and `boquilha_reopenings`.
- **Later Owner clarification:** contract §33.1 items 4 and 5 — the open/closed lifecycle and the
  B1 active-aggregate race machinery removed in full, including the two partial unique indexes,
  the 23505 → `ActiveAggregateExists` mapping, `HasActiveAggregateForAnchorAsync` and K6–K8.
  "They are NOT replaced by another locking mechanism — the problem they solved no longer exists."
- **Reason recorded:** with no lifecycle there is no active/closed state and no one-active-anchor
  invariant to serialise (contract §33.1 (5)).
- **Current implementation consequence:** no `status`; files `BoquilhaStatus.cs`, `CloseSnapshot.cs`,
  `ReopeningRecord.cs` and the two entities/configurations deleted (`git show caad792`); one plain
  `boquilhas_bq_id_key` UNIQUE remains; refusals reduced to `stale-version` (per-movement edit) and
  `register-exists` (the only 23505 mapping).
- **Tests proving it:** source/schema scans `MG1`, `MG2`, `MG3`, `S1`; negative-scope rows in the
  final review §9/§12; `N1R4_NoSettingsOrRepairerAdministrationSurfaceExists`;
  `B1Superseded` row.
- **Reference status:** `BOQUILHAS.md` §Close/reopen, `RECORD_LIFECYCLES.md` §9 (Close/Reopen) and
  `ACCEPTANCE_MATRIX.md` §7 (Close/reopen) still describe the lifecycle.

### 3.5 What did NOT change

Preserved under both models (contract §33.2): edit replaces the SAME `movement_id` + one
before/after audit row + backend actor/time + no second quantity event; `recorded_at` immutable,
`business_date` editable; repairer resolution (machine → current assignment → suggested) with
historical preservation and no administration; shared Tool orchestration; local Histórico;
fixed 1366×768 desktop; every route gated `dmo.module.boquilhas`; `CurrentBuildAvailable = []`.

---

## 4. Standalone / provisional-anchor comparison

### 4.1 REFERENCE

```text
production-linked:  boquilhas_id -> bq_id -> jobon_id + tool_id
standalone:         boquilhas_id -> tool_id          (permanent, legitimate)
                    "No fake Job On or fake bq_id is created."
```

- Source: `reference/contracts/IDENTITIES_AND_RELATIONSHIPS.md` ("Standalone Boquilhas"),
  `reference/modules/BOQUILHAS.md` §"Standalone flow", `reference/architecture/CROSS_MODULE_FLOWS.md`
  ("Standalone Boquilhas remains valid"), `reference/ACCEPTANCE_MATRIX.md` §7 ("standalone aggregate
  may use direct `tool_id`").
- No association step, no provisional wording, no `pending_tool_id` concept.

### 4.2 APP (current)

```text
pré-JobOn:   register created with bqId XOR pendingToolId (migration 008 CHECK
             boquilhas_anchor_check ((bq_id IS NULL) <> (tool_id IS NULL)))
association: when a bq_contexts row whose tool_id == the register's pending tool_id arrives
             -> candidates presented (register-side read + Job-On-incoming light packet, both
                tool-keyed; never a global scan)
             -> EXPLICIT HUMAN confirmation (AssociateAsync)
             -> SAME boquilhas_id, bq_id set, provisional tool_id cleared, version += 1,
                history untouched
settled:     boquilhas_id -> bq_id -> jobon_id
             ("This does NOT recreate a permanent standalone.")
refusals:    already-associated (409), association-mismatch (409)
```

- Source: `plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md` §34.1/§34.2;
  `src/DMO.Application/Boquilhas/BoquilhasModels.cs`, `BoquilhasService.cs`
  (`AssociateAsync`, `GetAssociationCandidatesAsync`); migration
  `20260924130151_BoquilhasPreJobonAssociation`.

### 4.3 The decision/evidence that introduced it

| Step | Commit | Artifact | What it established |
|---|---|---|---|
| Original | `dcca794` / `c6a87dd` | contract §22/§15 + implementation | **permanent** standalone (`tool_id` anchor), matching the reference |
| First clarification | `caad792` | contract §33.1 item 1 | permanent standalone **removed**; production-linked only |
| Second clarification | `f7aa6ee` | contract §34.1/§34.2; response `OWNER_CLARIFICATION_…` §2 decision 4, §3 C1 | a **provisional, transitional** `tool_id` anchor may exist pré-JobOn; resolves to `bq_id → jobon_id` on master-`tool_id` match; briefly recorded as superseding §33.1 item 1 |
| Implementation | `a59d8c1` | migration 008, domain/app/web | executed the §34 model (including repairer re-homing) |

**Note on the reference's own §34 counterpart:** there is none. The reference still describes a
**permanent** standalone, and (separately) never received the §33 removal either. So the current
app and the reference differ twice over on this axis: permanent-standalone (reference) vs
transitional-anchor-with-association (app).

---

## 5. Authority provenance

**This section is critical.** For every material difference, the provenance class and the exact
local artifact are cited. Provenance classes used: **explicit Owner instruction** (as recorded),
**Architect interpretation**, **implementation-agent invention**, **contract correction**,
**test-driven assumption**.

### 5.1 Movement vocabulary (four → three; `Irreparável` → `Entrada sem reparação`)

| Item | Finding |
|---|---|
| Provenance class | **explicit Owner instruction — as recorded by the implementation agent** (not independently verifiable in `reference`) |
| Proving artifact | `plans/contracts/P2-T07_BOQUILHAS_CONTRACT.md` §33, opening: "**Authority:** direct OWNER clarification after the P2-T07 implementation was pushed but while the workstream is NOT closed"; §33.1 items 2–3 |
| Corroborating artifact | `dev/responses/P2_T07_IMPLEMENTATION_RESPONSE.md` §21 opening: "a NEW OWNER CLARIFICATION issued after the implementation above was pushed"; commit `caad792` message |
| Independent corroboration in `reference` | **NONE.** `reference/modules/BOQUILHAS.md` still carries the four-type vocabulary; no §33/§34 equivalent exists |
| Counter-evidence of invention | The change is surgical and fully consistent with the stated rationale, carries an explicit supersession list, a clean (pre-closure) migration correction, and a matching test suite — behavior is *implemented*, not merely asserted. However, **the Owner directive itself is not recorded in the canonical reference repository.** |
| Test-driven assumption? | **NO.** The tests were written *after* the clarification to prove it (response §21.6: "tests were kept small per the clarification"), not to define it |

### 5.2 Lifecycle removal (close/reopen, status, active-anchor machinery)

| Item | Finding |
|---|---|
| Provenance class | same **explicit Owner instruction — as recorded** |
| Proving artifact | contract §33.1 items 4–5; response §21.1 |
| Note | The B1 active-anchor machinery was itself an **Architect interpretation** artifact: it was introduced by the focused B1 correction (`b884dd8`) after Architect PLAN REJECT `542a08a1` — i.e. an **Architect-required implementation-contract fix**, not an Owner rule. §33 removed it because the underlying invariant no longer exists. |
| Independent corroboration in `reference` | **NONE**; `RECORD_LIFECYCLES.md` §9 and `BOQUILHAS.md` still describe close/reopen |

### 5.3 Balance model (four buckets → single outstanding)

| Item | Finding |
|---|---|
| Provenance class | **explicit Owner instruction — as recorded** |
| Proving artifact | contract §33.1 item 6, §33.2; response §21.1 |
| Independent corroboration in `reference` | **NONE**; `BOQUILHAS.md` §Balance still lists the four buckets |

### 5.4 Provisional pré-JobOn anchor + association

| Item | Finding |
|---|---|
| Provenance class | **explicit Owner instruction — as recorded**, then implementation |
| Proving artifact | contract §34.1/§34.2; `dev/responses/OWNER_CLARIFICATION_PLANNING_CONTEXT_AND_ASSOCIATIONS_RESPONSE.md` §1 ("Clarification source: Direct OWNER clarification (dictated design directives, registered on the clean baseline `c01d836`)"), §2 decision 4, §3 C1 |
| Implementation artifact | `a59d8c1`; migration `20260924130151_BoquilhasPreJobonAssociation` |
| Independent corroboration in `reference` | **NONE**; reference still has the permanent standalone |
| Implementation-agent invention? | **NO** for the concept; the *edge semantics* (candidate selection by the same UUID, light packets, refusal tokens `already-associated`/`association-mismatch`, concurrency) are implementation detail explicitly justified by the accepted P2-T05 §4.4 Peso-associate pattern, which **is** an existing accepted authority in the app. |
| Note | The concept itself is a **recording** of an Owner directive, not an invention; but the recording lives only in `app`. |

### 5.5 Repairer ownership (Controlo → Boquilhas Definições)

| Item | Finding |
|---|---|
| Provenance — first move (Controlo ownership) | **explicit Owner instruction** recorded earlier: `reports/CONTROL_SETTINGS_REPAIRERS_EMAIL_PDF_DELTA.md` §3/§4 (`0f370e6`, 2026-09-22) |
| Provenance — second move (Boquilhas ownership) | **explicit Owner instruction — as recorded**: contract §34.3 (+ P2-T05 §31.3), response `OWNER_CLARIFICATION_…` §2 decision 5 / §3 C2 |
| **Independent corroboration in `reference`** | **YES.** `reference/modules/BOQUILHAS.md` was updated by commit `cca33d472830ee52953b3cfa90018d20251d4983` (2026-09-24 12:35) — **the same day**, ~17 minutes before the app's `f7aa6ee` (12:52) — to state "Boquilhas owns the operational repairer configuration" and "not Admin configuration and not Controlo configuration". |
| Conclusion | This is the **only** material difference with a confirmed independent canonical record. The app's later docs describe it as coming from the `f7aa6ee` clarification, but the reference independently records the same decision in `cca33d4`. |
| Implementation artifact | `a59d8c1` (`/boquilhas/definicoes`; Controlo routes removed) |

### 5.6 Provenance summary

| Difference | Owner instruction (recorded) | Reference corroboration | Architect | Agent invention | Contract correction | Test-driven |
|---|---|---|---|---|---|---|
| Three-type vocabulary | YES (§33) | NO | — | NO | YES (migration 007 corrected pre-closure) | NO |
| No lifecycle / no close-reopen | YES (§33) | NO | YES (B1 was Architect-required; later removed) | NO | YES | NO |
| Single outstanding balance | YES (§33) | NO | — | NO | YES | NO |
| Provisional anchor + association | YES (§34) | NO | — | NO (concept); edge semantics are implementation detail grounded in accepted P2-T05 §4.4 | — | NO |
| Repairer → Boquilhas Definições | YES (§34.3 / earlier delta) | **YES (`cca33d4`)** | — | NO | — | NO |

**Bottom line of §5:** every material difference traces to an Owner clarification **as recorded in
`app`**, except repairer ownership, which is independently corroborated in `reference`. The
movement-model and lifecycle differences rest entirely on an Owner directive that exists **only in
`app`**; they are not an implementation-agent invention in the code sense (the app explicitly
records them as Owner clarifications with supersession lists and a matching clean migration
correction), but they are **unverified against the canonical reference**.

---

## 6. Reference update candidate (DRAFT — DO NOT APPLY)

**Conditional.** Only applies IF the current app model is confirmed as the intended Owner model.
This section is a draft map, not a change request.

### 6.1 Exact reference files affected

| File | Concept/section requiring replacement |
|---|---|
| `reference/modules/BOQUILHAS.md` | §"Included in Beta" (four-type bullet); §"Explicitly outside Beta"; §"Movement vocabulary" (`Início | Saída | Entrada | Irreparável` → three types); §"Balance" (four buckets → single outstanding); §"Excess Entrada" (expected/excess facts); §"Close/reopen" (remove); §"Standalone flow" (permanent → transitional anchor + association); §"Canonical identities" (note the provisional anchor); §"Backend contracts required" (remove close/reopen; add association); §"Acceptance criteria" (movement selector count, remove close/reopen criterion, add association criterion); §"Required evidence" (remove lifecycle/close/reopen tests; add association tests) |
| `reference/architecture/RECORD_LIFECYCLES.md` | §9 "Boquilhas" — "Active" movement list; "Close" subsection; "Reopen" subsection; the lead sentence "`boquilhas_id` owns one repair-flow aggregate" (retain identity, drop lifecycle) |
| `reference/contracts/IDENTITIES_AND_RELATIONSHIPS.md` | "Production-linked Boquilhas" / "Standalone Boquilhas" blocks — replace the permanent-standalone block with the transitional-anchor + association block, or mark the permanent block as then-resolved |
| `reference/architecture/CROSS_MODULE_FLOWS.md` | "Job On → Boquilhas" — the "Standalone Boquilhas remains valid" paragraph and "No fake Job On is created for the standalone case" |
| `reference/ACCEPTANCE_MATRIX.md` | §7 "Workstream E — Boquilhas": "standalone aggregate may use direct `tool_id`"; the Movements list (four → three); the Balance bullet list; the "Close/reopen" block |

Unrelated Boquilhas documentation (purpose, repairer/line assignments, repairer historical
preservation, Histórico, edit/audit, Tool orchestration, the repairer rule itself) is **not**
touched.

### 6.2 Wording that becomes superseded

- the four-type vocabulary statement and its acceptance evidence;
- the `Início`-with-aggregate opening rule;
- the `Irreparável` type and its "declared irreparable" meaning + upper-bound constraint;
- the four-bucket balance names and the expected/excess Entrada facts;
- the "permanent standalone" allowance and "no fake Job On for standalone";
- the whole close/reopen subsection and its acceptance rows;
- the "remote standalone" identity block in the identity map.

### 6.3 New wording needing to be recorded

- a single, correctly-attributed **Owner clarification** block in `reference/modules/BOQUILHAS.md`
  (mirroring the app's §33/§34 records) — with the date and the directive source, so the canonical
  reference itself carries the authority (this is the missing piece today);
- three-type vocabulary: `Saída | Entrada | Entrada sem reparação`, with the "not repaired, not
  chargeable, Tool untouched, no irreparable bucket" meaning;
- `outstanding = Σ(Saída) − Σ(Entrada) − Σ(Entrada sem reparação)`, replay-derived, never stored,
  negative visible/non-blocking, no upper-bound rules;
- register creation is identity-only (no manufactured movement);
- BOQUILHAS is a production-linked movement register with **no** lifecycle;
- pré-JobOn provisional `tool_id` anchor + explicit human association at the matching `bq_id`,
  settling as `boquilhas_id → bq_id → jobon_id`, with **no** permanent standalone;
- updated `RECORD_LIFECYCLES.md` §9 and `ACCEPTANCE_MATRIX.md` §7 rows.

### 6.4 Explicit non-goals of the draft

Do not rewrite repairer ownership (already canonical via `cca33d4`), do not touch Controlo,
Job On, Ferramentas, Documents or access sections, and do not renumber unrelated sections.

---

## 7. Alternative correction boundary

**Conditional.** Only applies IF the reference four-movement model is instead confirmed as
authoritative. This section shows the consequence; it does **not** design or implement it.

Approximate implementation areas that would need correction in `app`:

| Area | Approximate scope |
|---|---|
| Domain | `src/DMO.Domain/Boquilhas/MovementKind.cs` (re-add `Inicio`, `Irreparavel`); a reinstated balance/bucket projection (`BalanceProjection.cs` was deleted); aggregate `status` + `BoquilhaStatus.cs`; `CloseSnapshot.cs`, `ReopeningRecord.cs` |
| Application | `BoquilhasModels.cs` / `BoquilhasService.cs` / `BoquilhasValidator.cs` / `IBoquilhasService.cs` / `BoquilhasReadModels.cs` — movement commands, balance computation, close/reopen commands, refusal vocabulary (`saida-exceeds-available`, `irreparavel-exceeds-in-repair`, `active-aggregate-exists`, `already-closed`, `not-closed`, `not-last-closed`, `only-one-inicio`), opening-facts |
| Persistence | a new migration adding back `boquilha_machines`, `boquilha_close_snapshots`, `boquilha_reopenings`, `status`, opening-facts columns and the two partial unique indexes; entity/configurations; `BoquilhasRepository` |
| Web | re-add close/reopen/opening-facts/standalone endpoints and pages; movement selector (4 types); `dmo-boquilhas.js`/`.css` |
| Tests | reconstruct the four-type vocabulary, bucket, upper-bound, close/reopen, race (K6–K8) and schema rows — the old 83/86 certification matrix was deliberately not reconstructed and would need re-authoring |
| Route/table counts | 15 routes → 18; 3 tables → 6; physical table count 23/24 → 27/28 |
| Also unresolved | whether the standalone model returns to **permanent** (reference) or keeps the transitional anchor (§34) — these are independent axes |

Consequence: this is a **large, cross-layer correction touching a CLOSED, independently VERIFIED
workstream** (P2-T07, closed at `a96814f`), plus a second migration over already-shipped
migrations 007/008 — which the acceptance discipline restricts.

---

## 8. Recommendation to Owner

No model is chosen here. The minimum binary decisions:

1. **Movement vocabulary.** Is the authoritative Boquilhas write vocabulary the reference's four
   types (`Início | Saída | Entrada | Irreparável`, with a four-bucket balance and an
   `Início`-with-aggregate opening) **or** the app's three types
   (`Saída | Entrada | Entrada sem reparação`, with the single replay-derived outstanding and no
   `Início`)? — *This also settles whether `entrada_sem_reparacao` is canonical.*

2. **Aggregate lifecycle.** Does Boquilhas keep an open/close/reopen aggregate lifecycle (with an
   immutable close snapshot and a `status`), **or** is the register lifecycle-free as implemented
   in the app?

3. **Standalone semantics.** Is a Boquilhas register **permanently** standalone-able from a
   `tool_id` (reference), **or** only **provisionally** anchored pré-JobOn and settled to
   `bq_id → jobon_id` on explicit human association (app)?

An optional fourth, if the Owner wants the audit trail closed: **where should the confirming
clarification be recorded** — only in `app` (status quo), or canonically in `reference`
(recommended if decisions 1–3 confirm the app model, since the current reference would otherwise
remain authoritative against the shipped implementation).

---

## 9. Stop

This report is analysis only. It was committed and pushed to `dgains00-create/app/main` as the
single change of this task. `reference` was not modified, no code/migration/test was changed, and
no model was chosen.

---

## 10. Verification statement

- Application code modified: **NO**.
- Database schema/migration modified: **NO**.
- Tests modified: **NO**.
- `reference` modified: **NO**.
- Existing reports/contracts modified: **NO**.
- Only this report was added, committed and pushed.
