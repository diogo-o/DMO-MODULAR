# RESUMO PERSISTENCE — PLANNING ANALYSIS (2026-09-24)

**Class:** Planning / analysis input only. **Not** authorization, **not** a contract, **not** an
implementation.

**Scope:** determine the smallest correct future implementation slice for a persisted `resumo_id`
without replacing or damaging the existing production Resumo read projection
(`IProductionResumoRead`). No production code, migration, frontend, contract, test or route was
modified. No `resumo_id` was implemented. The only artifact produced is this report.

**Baselines**

| Item | Value |
|---|---|
| `app/main` | `c56316ad46e53cbcce47b45cb4ac1b4d60dbf01d` |
| `reference/main` | `cca33d472830ee52953b3cfa90018d20251d4983` |

---

## 1. Current state

### 1.1 The traced flow (as implemented)

```text
Job On (jobon_id)                       job_ons row (reference, production_number, machine,
                                        production_date, version)   [P2-T04 owns the write]
   │
   ├─ GET /controlo/create/resumo/{jobonId}   (route; ControloCreateEndpoints)
   │     → IProductionResumoRead.GetResumoAsync(jobonId)
   │        → DmoProductionResumoRead: ONE statement
   │           job_ons LEFT JOIN cm_contexts LEFT JOIN tools, keyed by jobon_id,
   │           selecting exactly the entry-surface columns
   │        → ProductionResumoReadModel(reference, productionNumber, machine, productionDate,
   │                                     version, Cm?)
   │              Cm = CmResumoProjection(cm_id, tool_id, frozen triple, live reference/lot,
   │                                      processo, quantity)   or null
   │
   ├─ Controlo Create page (/controlo/create?jobonId=…) → LoadProductionAsync()
   │     builds the ProductionContextStrip + the CM context region + association candidates
   │
   └─ Peso create: CreatePesoCommand(CmId XOR PendingToolId, <Peso-owned measurement facts>)
          → peso_id → cm_id → jobon_id + tool_id     (the same persisted peso_id moves
                                                      draft → submit → approve)
```

### 1.2 What `IProductionResumoRead` actually is

| Fact | Evidence |
|---|---|
| A **read-only projection** over real P2-T04 rows | `DmoProductionResumoRead` doc: "read-only projection … never writes, never creates a production aggregate and never copies Job On data into a second authority"; `AsNoTracking()` |
| One context-specific statement (`job_ons LEFT JOIN cm_contexts LEFT JOIN tools`) | the LINQ in `DmoProductionResumoRead.GetResumoAsync` |
| Keyed by the real `jobon_id`; no global scan | interface doc: "no global scan, no loading of every Job On/Tool/Peso to filter later" |
| Carries **no** persistence of its own — no table, column or route | `IProductionResumoRead` doc: "`resumo_id` record itself remains an unimplemented P2-T05 handoff remainder — no table, column or route for a Resumo RECORD exists or is created" |
| Its purpose is the **Controlo entry** | `Create.cshtml.cs.LoadProductionAsync` doc: "R1/R3 — the production entry through the Resumo da produção (P2-T05 contract §31.1)" |
| Consumers | `ControloCreateEndpoints` route `GET /controlo/create/resumo/{jobonId}`; `Pages/Controlo/Create.cshtml.cs`; tests `P2T05TestHost`/`P2T05TestStore` |

### 1.3 Why it must NOT be deleted merely because a persisted `resumo_id` appears

1. **It is a projection, not a record.** It reads `job_ons` / `cm_contexts` / `tools` directly. A
   persisted `resumo_id` record is a *separate* business object that *references* `jobon_id`
   (`RECORD_LIFECYCLES.md` §8; `CONTROLLO_CREATE.md`: `resumo_id -> jobon_id + applicable
   contexts`). Removing the projection would not create the record; it would only remove the entry
   read.
2. **Deleting it would violate the "no duplication" rule.** The projection deliberately does *not*
   copy Job On facts into a second authority. A `resumo_id` record must also not re-own those
   facts (see §3, §7). If the projection is deleted, the entry surface must re-derive the same
   facts from somewhere — recreating the problem it solves.
3. **Its consumers are entry-surface consumers.** The page and the route are the Controlo entry;
   the record's own read is a *different* responsibility (the persisted Resumo business record).
4. **`resumo_id` is defined as a record, not a read.** `DOCUMENTS_AND_FILES.md`: "`resumo_id` is a
   real persisted Controlo record … The PDF is an output of `resumo_id`, not its identity." A read
   projection can never be the PDF's owning record (`DOCUMENTS_AND_FILES.md` §1: the structured
   owning record is truth).

**Conclusion for §1:** the two coexist (§3). `IProductionResumoRead` stays as the Controlo entry
projection; a new persisted Resumo record is added alongside it. The only question is whether the
*entry surface* should additionally consult the persisted record — which is a UX/authority question,
not a reason to delete the projection.

---

## 2. Canonical persisted identity

### 2.1 What existing authority fixes

| Question | Authoritative answer | Source |
|---|---|---|
| Relationship | `resumo_id → jobon_id + applicable component contexts` | `contracts/IDENTITIES_AND_RELATIONSHIPS.md`; `architecture/BACKEND_FRONTEND_MODEL.md`; `modules/CONTROLO_CREATE.md` |
| What it is | "one persisted Resumo control record"; "a persisted Controlo record for one `jobon_id` context"; "summarizes control status/history per applicable component of one `jobon_id`" | `IDENTITIES_AND_RELATIONSHIPS.md`; `RECORD_LIFECYCLES.md` §8; `sources/dmo-master/global/DOCUMENT_FILE_MODEL.md` §5 |
| Owner | Controlo Create ("Controlo -> Peso/Pegamentos/Folha/Resumo"; "Resumo preparation") | `IMPLEMENTATION_MODEL.md` §5; `modules/CONTROLO_CREATE.md` §Purpose/§Owns |
| Distinct from Folha | "`resumo_id` is persisted and not a projection of Folha"; "`controlo_sheet_id` … distinct from `resumo_id`" | `ACCEPTANCE_MATRIX.md` §7; `RECORD_LIFECYCLES.md` §7/§8 |
| Distinct from Peso/Folha identities | "`pegamentos_id`, `controlo_sheet_id` and `resumo_id` remain distinct" | `ACCEPTANCE_MATRIX.md` §7 |
| PDF relation | "The PDF is an output of `resumo_id`, not its identity"; "The record remains authority even when the PDF is absent" | `DOCUMENTS_AND_FILES.md` §2; `RECORD_LIFECYCLES.md` §8 |
| Not implemented today | the app contract records `resumo_id` as an unimplemented P2-T05 handoff remainder (no table/column/route) | P2-T05 §2.3/§29/§31.1; `IProductionResumoRead` doc |
| Is a real record, not a path/filename | "`resumo_id` is a real persisted Controlo record"; directory/path is never a replacement identity | `DOCUMENTS_AND_FILES.md` §1/§2 |

### 2.2 Classification of the requested determinations

| Determination | Answer | Status |
|---|---|---|
| Exactly one Resumo per Job On production? | `resumo_id` is "for **one** `jobon_id` context" and "summarizes control status/history **per applicable component of one `jobon_id`**". The strongest defensible reading is **one Resumo per `jobon_id`** (cardinality 1:1 with the Job On context) — consistent with the Controlo entry role. However, authority states the singular relationship ("for one `jobon_id`") and does **not** state an explicit uniqueness constraint or the possibility of multiple Resumos per Job On (e.g. per revision/version). | **PARTIALLY DEFINED — 1:1 is the defensible reading; uniqueness enforcement is `UNDEFINED / AUTHORITY DECISION REQUIRED`** |
| What creates it? | Controlo Create owns "Resumo preparation" and "Folha/Resumo persistence". The **persisting actor is Controlo Create**. Whether it is human-triggered or automatic is not stated. | **OWNER DEFINED (Controlo Create); trigger `UNDEFINED / AUTHORITY DECISION REQUIRED`** |
| When is it created? | Not stated. The Resumo is the Controlo **entry** context, which suggests it exists no later than the Controlo entry. Whether it is created on entry, on Peso submission, or on demand is not fixed. | **`UNDEFINED / AUTHORITY DECISION REQUIRED`** |
| Can it be edited/rebuilt? | Not stated. It "summarizes control status/history", which implies it can change as Peso status/history changes. Whether that is an *edit*, a *rebuild/projection refresh*, or append-only is not fixed. | **`UNDEFINED / AUTHORITY DECISION REQUIRED`** |
| Lifecycle/state? | No lifecycle is authored. `RECORD_LIFECYCLES.md` §8 gives no state vocabulary (unlike Folha's `Rascunho → Submetida → Aprovada/Rejeitada` and Peso's `pendente/aprovado/nao_aprovado`). It does say "Do not collapse Folha lifecycle into Peso status or Resumo lifecycle" (`RECORD_LIFECYCLES.md` §7), implying a Resumo lifecycle may exist, but none is defined. | **PARTIALLY DEFINED (separate from Folha/Peso); its own lifecycle vocabulary is `UNDEFINED / AUTHORITY DECISION REQUIRED`** |
| Mutable after Peso approval? | Not stated. | **`UNDEFINED / AUTHORITY DECISION REQUIRED`** |
| References Peso(s), CM context, Folha, or only the production? | Relationship is `resumo_id → jobon_id + applicable component contexts`. "Applicable component contexts" is broader than "production only" and does not name `peso_id` or `controlo_sheet_id`. | **PARTIALLY DEFINED — `jobon_id`+contexts fixed; explicit `peso_id`/`controlo_sheet_id` FKs `UNDEFINED / AUTHORITY DECISION REQUIRED` (see §5)** |

### 2.3 Explicitly unresolved (do not invent)

- `UNDEFINED / AUTHORITY DECISION REQUIRED` — strict uniqueness (DB constraint) of `resumo_id` per
  `jobon_id`.
- `UNDEFINED / AUTHORITY DECISION REQUIRED` — creation trigger and timing.
- `UNDEFINED / AUTHORITY DECISION REQUIRED` — editability/rebuild semantics and whether Resumo
  history is append-only.
- `UNDEFINED / AUTHORITY DECISION REQUIRED` — Resumo lifecycle/state vocabulary (if any).
- `UNDEFINED / AUTHORITY DECISION REQUIRED` — mutability after Peso approval.
- `UNDEFINED / AUTHORITY DECISION REQUIRED` — the exact set of "applicable component contexts"
  (CM only? CM/MF/BQ? and whether Peso/Folha are referenced or traversed).

---

## 3. Resumo vs production read projection

### 3.1 Separation

| | A. Production-context projection (exists today) | B. Persisted Resumo business record (to be authored) |
|---|---|---|
| Type | read-only projection | persisted record with its own identity |
| Identity | none (`jobon_id` is the key) | `resumo_id` |
| Source of the facts | live `job_ons`/`cm_contexts`/`tools` rows | own stored facts + traversal to `jobon_id`/contexts |
| Purpose | Controlo **entry**: select the production and populate the Peso surface | the persisted Controlo record whose PDF is `Resume_<reference>_<line>.pdf` |
| Mutability | none (never writes) | `UNDEFINED / AUTHORITY DECISION REQUIRED` (§2.2) |
| Authority over Job On facts | none — Job On remains the only written authority | none — must not duplicate Job On truth |

### 3.2 Minimum responsibility of each

- **A (projection).** Exactly what it does today: resolve, for one `jobon_id`, the production facts
  and the CM context needed to enter Controlo and populate Peso. It owns **nothing**.
- **B (record).** Own only what no other record owns: the **Resumo-specific persisted content** —
  i.e. the persisted result of "summarizes control status/history per applicable component of one
  `jobon_id`" (`DOCUMENT_FILE_MODEL.md` §5) plus its own attribution/identity. It must **not** own
  reference/production-number/machine/processo (Job On truth), Tool nominal/lot truth (Tool truth),
  Peso measurement/calculation truth (Peso truth), or Folha component decisions (Folha truth).

### 3.3 Should they coexist?

**Yes.** They are two different responsibilities. The projection is a *view*; the record is a
*persisted business object*. Deleting the projection to "make room" for the record would remove the
Controlo entry read and would tempt the record into owning Job On facts it must not own.

### 3.4 The "two authorities" risk (explicitly avoided)

The only new authority the record may introduce is **the Resumo-specific content** (the persisted
summary/status facts per component). It must be the **single authority** for that content — no
projection may be a second storage of it. Conversely, Job On/context facts must stay single-sourced
from the Job On; the record must *traverse* (`resumo_id → jobon_id`) rather than copy
(`BACKEND_FRONTEND_MODEL.md`; `APPLICATION_FOUNDATION.md` "a feature does not create a private
replacement identity").

---

## 4. Resumo vs Folha

### 4.1 Fixed distinctions

- `controlo_sheet_id → jobon_id`; `resumo_id → jobon_id + applicable contexts`
  (`CONTROLO_CREATE.md` §Folha and Resumo).
- "`controlo_sheet_id` is a persisted record, **distinct from** `resumo_id`"; "Do not collapse Folha
  lifecycle into Peso status or Resumo lifecycle" (`RECORD_LIFECYCLES.md` §7).
- "Resumo is persisted and **not a projection of Folha**" (`ACCEPTANCE_MATRIX.md` §7).
- "Folha and Resumo are separate persisted records … must not merge them into one record merely
  because both are control outputs" (`CONTROLO_CREATE.md`).
- Folha owns the five family items CM/MF/BQ/PU/CS with OK/NOK state + observation and its own
  `Rascunho → Submetida → Aprovada/Rejeitada` lifecycle (P2-T06 §18.1, from master §13/§15).

### 4.2 Field/ownership matrix (existing authority + implemented facts only)

| Fact / field | Owner | Resumo | Folha | Storage in a future Resumo |
|---|---|---|---|---|
| `resumo_id` (own identity) | Controlo Create | ✔ owns | — | **STORED** (PK) |
| `controlo_sheet_id` (own identity) | Controlo Create | — | ✔ owns | Traversed only, **not stored** unless §5 decides a relation |
| `jobon_id` | Job On | references | references | **STORED as FK** (required — "for one `jobon_id` context") |
| reference | Job On (`job_ons.reference`) | projects | traverses | **NOT stored** on Resumo (Job On truth) |
| production_number | Job On | projects | traverses | **NOT stored** |
| machine | Job On | projects | traverses | **NOT stored** |
| processo | Tool (via `cm_id`/`jobon_id`) | traverses | — | **NOT stored** |
| production_date | Job On | projects | — | **NOT stored** |
| CM/MF/BQ contexts | Job On (`cm_contexts`/`mf_contexts`/`bq_contexts`) | traverses ("applicable component contexts") | traverses | **NOT stored** (traversed), unless authority fixes a snapshot — see open decision |
| Tool nominal/lot/quantity | Tool | — | — | **NOT stored** (Tool truth) |
| Peso measurement/calculation/status | Peso | traversal/"summarizes" | — | **NOT stored** as Peso truth; only aggregated summary facts if authority fixes them (§5) |
| Folha five-item OK/NOK states + observations | Folha | — | ✔ owns | **NOT stored** on Resumo (would violate the Folha/Resumo distinction) |
| Resumo-specific summary content (e.g. persisted control status/history summary per component) | Resumo (future) | ✔ owns | — | **STORED** — the only genuinely new content (shape `UNDEFINED / AUTHORITY DECISION REQUIRED`) |
| Resumo actor/creation time | Controlo Create (future) | ✔ owns | — | **STORED** (P2-T05 attribution convention: backend actor/time) |
| PDF bytes / filename / directory | derived | output | output | **NOT stored as identity** (`DOCUMENTS_AND_FILES.md` §1; metadata only where genuinely required, §4) |

### 4.3 Must-not-duplicate list

- Do **not** store Job On production facts on the Resumo (reference/production_number/machine/
  production_date/processo) — they stay Job On truth and are traversed.
- Do **not** store Tool nominal/lot/quantity — Tool truth.
- Do **not** store Folha's five-item decisions/observations — Folha truth; Resumo must not project
  Folha.
- Do **not** store Peso measurement rows/calculations — Peso truth.
- Do **not** introduce a document metadata table "merely for symmetry" (`DOCUMENTS_AND_FILES.md`
  §8; P2-T08 non-scope).

---

## 5. Resumo and Peso

### 5.1 What authority says (and does not)

- **No** authority statement names `resumo_id` ↔ `peso_id` as a foreign key.
- The Resumo relationship is `resumo_id → jobon_id + applicable component contexts`
  (`IDENTITIES_AND_RELATIONSHIPS.md`, `BACKEND_FRONTEND_MODEL.md`, `CONTROLO_CREATE.md`).
- The Peso relationship is `peso_id → cm_id → jobon_id + tool_id` (production) or `peso_id →
  tool_id` (pending) (`DOCUMENTS_AND_FILES.md` §2; `CROSS_MODULE_FLOWS.md`).
- `DOCUMENT_FILE_MODEL.md` §5: Resumo "summarizes control status/history per applicable component of
  one `jobon_id`" — i.e. it consumes Peso **state/results by traversal through the Job On**, not by
  owning a Peso FK.

### 5.2 Determination (minimum-relation rule)

```text
resumo_id  ->  jobon_id          (fixed, stored)
jobon_id   ->  cm_id / mf_id / bq_id   (Job On contexts, traversed)
peso_id    ->  cm_id -> jobon_id + tool_id   (Peso truth, unchanged)
Resumo reads Peso state/results by traversal through jobon_id/contexts
```

- **Does Resumo own a `peso_id`?** No authority statement supports it. Minimum-relation rule ⇒
  **NO**.
- **Does Peso own a `resumo_id`?** No authority statement supports it; Peso must not be changed
  (P2-T05/P2-T06 are closed, and adding a `resumo_id` column to `pesos` would be a second
  production relation). ⇒ **NO**.
- **Both independently point to Job On/CM context?** Yes — each points to its own anchor
  (`resumo_id → jobon_id`; `peso_id → cm_id → jobon_id + tool_id`). This is not a duplication; each
  keeps its own identity and the Job On is the shared context.
- **Does Resumo simply read Peso state/results?** Yes — by traversal through the Job On/contexts.
  This is consistent with "summarizes control status/history".

**Conclusion:** the minimum relation is **traversal through `jobon_id`**, with **no `peso_id` FK and
no `resumo_id` FK on `pesos`**. A UI link between a Resumo and a Peso is a traversal link, not a
foreign key.

**Open:** whether a specific "applicable component context" set (CM only vs CM/MF/BQ) should be
persisted, and whether an explicit Peso relation is ever wanted. ⇒ `UNDEFINED / AUTHORITY DECISION
REQUIRED` (see §8/§11).

---

## 6. Document implications (P2-T08)

### 6.1 The three separated layers

Per `DOCUMENTS_AND_FILES.md` §1/§7 and `APPLICATION_FOUNDATION.md` ("record != generated document !=
filesystem path"):

| Layer | What it is | Authority |
|---|---|---|
| **Database record** | the persisted `resumo_id` Resumo record | truth (the owning structured record) |
| **Rendered PDF** | derived output bytes | derived; must correspond to the frozen state used (`DOCUMENTS_AND_FILES.md` §4) |
| **Filesystem path** | `<configured base>/<reference>/<production-number>/Resume_<reference>_<line>.pdf` | presentation/storage organization only — never an identity/join key |

### 6.2 How `Resume_<reference>_<machine>.pdf` should be derived

1. Resolve the persisted Resumo record for the `jobon_id` (the record is the authority).
2. Derive the filename from **persisted operational context**: `reference` + `<line>`
   (`DOCUMENTS_AND_FILES.md` §2/§3; in the current app vocabulary `<line>` is the machine code
   `B1..C3` — P2-T05 `machine`).
3. Derive the directory as `<configured base directory>/<reference>/<production-number>/`, where the
   configured base comes from the operator-configured `Controlo_Create → Definições` setting
   (`pdf_directory_settings`), never a hardcoded constant (P2-T08 §4.9).
4. Render the PDF from the record's preserved state; historical rendering must not regenerate from
   today's mutable Tool fields (`DOCUMENTS_AND_FILES.md` §6).

### 6.3 What must exist before the PDF is authoritative

- A **persisted `resumo_id` record** must exist first; the PDF is an output, not the identity
  (`DOCUMENTS_AND_FILES.md` §2; `RECORD_LIFECYCLES.md` §8: "The record remains authority even when
  the PDF is absent").
- The record must already be in the required state for an *official/frozen* output; metadata and
  bytes must correspond to the same frozen state; opening a frozen document must not silently
  regenerate it (`DOCUMENTS_AND_FILES.md` §4).
- No document metadata table may be added merely for symmetry; add persisted metadata only where an
  immutable output genuinely requires it (`DOCUMENTS_AND_FILES.md` §4/§8; P2-T08 §4.8/§6).
- Availability is an explicit state (`Disponível` / `Ainda não gerado` / `A aguardar aprovação` /
  `Workspace indisponível` / `Ficheiro em falta` / `Versões disponíveis`), already modeled by the
  app's `AvailabilityState` presentation contract.

**Conclusion for §6:** P2-T08 is blocked on the Resumo record existing (a record without content
cannot be rendered authoritatively). The Resumo record slice precedes the Resumo output slice.
No PDF layout is designed here.

---

## 7. Schema impact candidate

Without writing a migration — the **minimum likely** additions. Fields marked STORED are the
candidate own facts; everything else is projected/traversed.

| Candidate field | Authoritative requirement | Why needed | Source of truth | STORED or PROJECTED |
|---|---|---|---|---|
| `resumo_id` | "one persisted Resumo control record"; own identity | the record's identity; PDF is an output of it | backend-allocated (P2-T05 `PesoId.New()` convention) | **STORED** (PK) |
| `jobon_id` (FK RESTRICT) | "`resumo_id → jobon_id`"; "for one `jobon_id` context" | the record must bind to exactly one Job On context; deletion must not orphan it | Job On | **STORED** (FK) |
| Resumo-specific summary content | "summarizes control status/history per applicable component of one `jobon_id`" | the only genuinely new content; the reason the record exists | Resumo | **STORED** (shape `UNDEFINED`) |
| actor (creation) | P2-T05 attribution convention (backend actor) | truth of who produced the record | backend | **STORED** |
| `created_at` | P2-T05 convention | immutable backend timestamp | backend | **STORED** |
| `version` (optimistic token) | P2-T05/P2-T07 convention if the record is editable | concurrency if edits are allowed | backend | **STORED only if editable** (else not needed) |
| uniqueness on `jobon_id` | derives from "one `jobon_id` context" | prevents two Resumos for one production | — | **STORED only if 1:1 confirmed** (`UNDEFINED`) |
| `reference`, `production_number`, `machine`, `production_date`, `processo` | none | — | Job On / Tool | **PROJECTED (must NOT be stored)** |
| context ids (cm/mf/bq) | "applicable component contexts" | traversal target | Job On | **PROJECTED/traversed** (stored only if a snapshot is later mandated) |
| `peso_id`, `controlo_sheet_id` | none | — | Peso / Folha | **NOT stored** (traversal only) |
| PDF bytes/metadata/path | only if an immutable output genuinely requires it | frozen-output correspondence | derived | **NOT stored** unless §6.3 requires it |

**Explicitly excluded (do not add):** any convenience column duplicating Job On or Tool truth
(reference/production_number/machine/processo/nominal/lot/quantity), any `peso_id` FK, any
`controlo_sheet_id` FK, any document-id table for symmetry.

---

## 8. Service/API boundary candidate

Smallest likely application contracts (names illustrative; **not** production endpoints):

| Contract | Shape (minimum) | Notes |
|---|---|---|
| Get/Create persisted Resumo | a create carrier keyed by `jobon_id` (+ the Resumo-owned summary content once defined) returning the record identity | backend allocates `resumo_id` (P2-T05 convention); no client-minted id |
| Update Resumo | only if authority allows edits — same `resumo_id` + observed `version`, one transaction | `UNDEFINED` until §2.2 editability is fixed |
| Read Resumo | a read model carrying `resumo_id`, `jobon_id`, the Resumo-owned content, and **traversed** production context | reuses the P2-T04 traversal pattern; no second Job On model |
| Document rendering input | a read that gives P2-T08 the record's **preserved** state + deterministic output context (reference, production number, machine, configured base directory) | record ≠ PDF ≠ path; no renderer forked from Peso |

**Boundary rules:** the Resumo service lives in the **Controlo Create** application area (owner);
it composes the existing P2-T04 reads for context rather than introducing a second Job On read; it
adds **no** route or availability entry (P2-T10 owns registration); it must not fork the Peso
renderer.

---

## 9. Existing code reuse

| Artifact | Disposition | Reason |
|---|---|---|
| `IProductionResumoRead` / `DmoProductionResumoRead` | **PRESERVE UNCHANGED** | the Controlo entry projection; not the persisted record (§1, §3) |
| `ProductionResumoReadModel` / `CmResumoProjection` | **PRESERVE**; optionally **CONSUME** from the new Resumo read | already the traversal shape for production context |
| `ControloCreateEndpoints` route `GET /controlo/create/resumo/{jobonId}` | **PRESERVE** | the entry read route; unchanged by a persisted record |
| `Pages/Controlo/Create.cshtml.cs` (`LoadProductionAsync`) | **PRESERVE**; **EXTEND** only if the entry must surface the persisted record | entry surface already correct |
| `ControloCreateService` / `IControloCreateService` | **CONSUME** the new Resumo contract additively | no forked logic |
| `PesoSheetReadModel` / `PesoContextProjection` / `PesoProductionProjection` | **CONSUME** (read-only) | the Resumo summarizes Peso state by traversal; no Peso FK |
| `pesos` / `peso_measurement_rows` / `peso_review_decisions` | **DO NOT MODIFY** | closed P2-T05/P2-T06 schema |
| `job_ons` / `cm_contexts` / `tools` (+ Job On reads) | **CONSUME unchanged**; **extend read-only** only if a narrower traversal is needed | Job On truth; no duplication |
| `pdf_directory_settings` (Definições) | **CONSUME** for the output base | P2-T08 §4.9 |
| `AvailabilityState`/`AvailabilityPresentation` (shared frontend) | **CONSUME** | the six availability states already exist |
| `PesoJobOnDependencyProbe` pattern | **CONSUME** as the model for a Resumo dependency probe | additive registration pattern |
| A document metadata table | **DO NOT CREATE** | forbidden for symmetry |
| A second Job On/production read model | **DO NOT CREATE** | forbidden duplication |

---

## 10. Acceptance matrix (draft — for a future implementation)

| ID | Case | Assertion |
|---|---|---|
| R-01 | A Resumo cannot silently become another production's Resumo | a Resumo bound to `jobon_id_A` never resolves/serves production B's context; the `jobon_id` relation is immutable; no "latest production" substitution |
| R-02 | Known Job On facts are not re-entered as independent Resumo authority | reference/production_number/machine/processo/production_date are read by traversal; no Resumo column stores them; changing the Job On row is reflected (no stale copy) |
| R-03 | Resumo remains distinct from Folha | `resumo_id` and `controlo_sheet_id` are separate records; no Resumo table stores Folha five-item decisions/observations; no merge |
| R-04 | Peso history is not rewritten when Resumo changes | persisted Peso rows/status/attribution/version unchanged after any Resumo create/update; no write path from Resumo to `pesos` |
| R-05 | Historical production context is preserved per existing snapshot rules | the Resumo's rendered/held context uses the frozen CM/MF/BQ triple semantics already established (P2-T04), never refreshed from today's mutable Tool |
| R-06 | Document rendering consumes the correct persisted record | the `Resume_<reference>_<machine>.pdf` output is produced from the persisted `resumo_id` record's preserved state; path/filename is never used as a join key; the configured base directory is used; no local path is printed |
| R-07 | Authorization follows Controlo Create/Approve boundaries | every Resumo read/write route is gated by the owning Controlo policy (`controlo-create` / `controlo-approve` as applicable); no new policy; no availability entry (`CurrentBuildAvailable` stays `[]`) |
| R-08 | No duplicate authority introduced | schema/source scan proves: no Job On fact column on the Resumo, no `peso_id`/`controlo_sheet_id` FK, no document metadata table, no second production read model |

---

## 11. Open authority decisions

Only decisions that genuinely cannot be resolved from existing reference/app authority.

| # | Question | Why required | Affected implementation | Safe default possible? |
|---|---|---|---|---|
| D1 | Is there **exactly one** Resumo per `jobon_id` (DB-enforced uniqueness), or may a production have multiple Resumos (e.g. per revision)? | fixes the cardinality and whether a unique constraint is authoritative | schema (unique key), create semantics, acceptance R-01 | **YES** — implement 1:1 with a unique key, re-openable; matches "for one `jobon_id` context" |
| D2 | What **creates** a Resumo and **when** (automatic on Controlo entry, on Peso submission, or explicit operator action)? | fixes the command trigger and whether a route/action is needed | service/API boundary, routes, tests | **YES** — explicit operator action (matches the accepted explicit-human-choice principle); automatic would need authority |
| D3 | Is the Resumo **editable/rebuildable**, and is its change-history append-only? Does it change **after Peso approval**? | fixes mutation semantics, version token, audit/history | schema (`version`), service, acceptance R-04/R-05 | **YES** — treat as rebuild-from-traversal, no independent editing until authority says otherwise; no `version` needed if never edited in place |
| D4 | Does the Resumo have its **own lifecycle/state** vocabulary? | `RECORD_LIFECYCLES.md` §7 implies a Resumo lifecycle may exist but defines none | schema (state column), UI states, availability mapping (`A aguardar aprovação`) | **NO** — must not invent states; the Resumo record should ship without an invented state machine |
| D5 | What exactly are the **"applicable component contexts"** the Resumo references (CM only, or CM/MF/BQ), and should any be **snapshotted** on the Resumo? | fixes the reference set and whether context ids are stored | schema (context columns), traversal read | **YES** — traverse all contexts via `jobon_id`, store none, until a snapshot is mandated |
| D6 | Is any **explicit Peso relation** intended on the Resumo (e.g. `resumo_id ↔ peso_id`), beyond traversal? | determines whether a FK exists (minimum-relation rule currently says none) | schema, service | **YES** — no FK; traversal through `jobon_id` only |

Non-questions (already fixed; do **not** escalate as owner decisions): the Resumo is persisted; it is
distinct from Folha; its PDF is derived; it is owned by Controlo Create; it references `jobon_id`.

---

## 12. Recommended implementation boundary

**One future slice: "persisted Resumo record core" (Controlo Create), no PDF rendering.** Planning
input only — **NOT authorization.**

- **Owned files/areas**
  - `src/DMO.Domain/Controlo/` (or a new `src/DMO.Domain/Resumo/`) — the Resumo identity/record
    types only.
  - `src/DMO.Application/ControloCreate/` — the new Resumo application contracts + read models
    (additive to the existing Controlo Create area).
  - `src/DMO.Infrastructure/Persistence/` — a new `ResumoEntity` + configuration + repository, and
    one additive migration owning exactly the Resumo table(s).
- **Expected new files**
  - `ResumoId`, `Resumo` (domain), `IResumoService`/`ResumoService`, `ResumoModels`,
    `ResumoEntity`, `ResumoEntityConfiguration`, `IResumoRepository`/`ResumoRepository`,
    one `20xxxxxx_ResumoDomain` migration, and one dependency-probe registration line (pattern of
    `PesoJobOnDependencyProbe`).
- **Protected areas (must stay byte-identical / untouched)**
  - `IProductionResumoRead` / `DmoProductionResumoRead` and the entry route/page;
  - `pesos`, `peso_measurement_rows`, `peso_review_decisions` (and migrations 001–008);
  - `job_ons`/`cm_contexts`/`tools` and the P2-T04 contract surface;
  - `DmoDbContext.cs` structure beyond an additive `DbSet` + configuration;
  - `ModuleRegistrations.CurrentBuildAvailable` (stays `[]`); no new route registration.
- **Tests**
  - R-01…R-08 above, plus: the Resumo table schema facts; one-record-per-`jobon_id` (if D1
    confirmed); no Job On fact columns; no `peso_id`/`controlo_sheet_id` FK; migrations 001–008 +
    `DmoDbContext` byte-identical; positive + negative-scope scans.
- **Migration scope**
  - exactly one additive migration creating the Resumo table(s) and its constraints/indexes
    (including the `jobon_id` FK RESTRICT); `Down` removes exactly that state; no touch to prior
    migrations.
- **Explicit non-scope**
  - **no** `resumo_id` PDF rendering (that is P2-T08, after this record exists);
  - no PDF layout, no filename/directory code, no email/send;
  - no modification of Peso/Folha/Pegamentos/Comparação;
  - no new availability/navigation/route registration;
  - no document metadata table;
  - no change to the existing production Resumo read projection.

---

## 13. Stop

Analysis only. The single change of this task is this report, committed and pushed to
`dgains00-create/app/main`. No code, migration, frontend, schema, route, contract or test was
modified. No `resumo_id` was implemented.

---

## 14. Verification statement

- Application code modified: **NO**.
- Database schema/migration modified: **NO**.
- Frontend modified: **NO**.
- Existing contracts modified: **NO**.
- Module availability/routes modified: **NO**.
- `resumo_id` implemented: **NO**.
- Only this report was added, committed and pushed.
