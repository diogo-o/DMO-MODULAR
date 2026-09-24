# P2-T08 (slice) — PESO PDF GENERATION + STORAGE — IMPLEMENTATION RESPONSE

**Slice:** documents handoff — close generation + storage of the Peso PDF under the existing
`peso_id` authority, consuming the operator-configured base directory (P2-T05 `Definições`).
**Task class:** directed functional slice (no new general audit; no PDF redesign; no new
tables/migrations). **Status:** IMPLEMENTED — focused tests green; awaiting review per the STOP
rules (no availability registration, no route registration in `CurrentBuildAvailable`).

---

## 1. Baseline / authority

| Item | Value |
|---|---|
| Baseline commit | `c3d3d89527cf146fdbca649177df78b40648a6ad` (`Controlo Create: state the decision result of the same peso_id clearly`) — working tree clean |
| Authority consumed | P2-T05 contract §12 (configured PDF-directory setting + server-host probe, Q-PDF); delta report §7.4 (directory convention unchanged); P2-T08 handoff (documents: deterministic naming, `<reference>/<production-number>/`, no parallel path, frozen outputs never silently regenerated); P2-T06 §23 (send/execution boundaries) |
| Non-implemented (unchanged) | Resume, Pegamentos, email/send, JobOn outputs, visual redesign, workflow engine, new tables/migrations |

## 2. Scope of the slice (done)

1. Located the existing document/directory mechanism: `PdfDirectorySettings` +
   `IPdfDirectorySettingsRepository` (`pdf_directory_settings`) + `IPdfDirectoryProbe`
   (server-host check) — **no PDF generation existed anywhere** (P2-T08 was not implemented).
2. Connected the existing Peso to that mechanism through the **shared read**:
   `IControloCreateService.GetAsync` — the exact `PesoSheetReadModel` Create and Approve render.
   No parallel read, no parallel calculation path (frozen per-row results pass through verbatim).
3. Generated the PDF from the `peso_id` authority: `PesoPdfService` (orchestration) +
   `PesoPdfComposer` (pure composition) + `PesoPdfRenderer` (dependency-free PDF 1.4, A4,
   Helvetica/WinAnsi, auto-pagination for N readings).
4. Created `<base>/<reference>/<production-number>/` automatically (never asks the operator;
   no `production_id` minted; reference/production/machine come ONLY from the Job On traversal
   facts of the shared read model).
5. Stored the PDF atomically (temp file + `File.Move` overwrite:false) at
   `Peso_<reference>_<machine>.pdf`; an existing target is reported `already-available` and is
   NEVER overwritten (frozen output immutability).
6. Returned/shown the generated output: `POST /controlo/approve/pesos/{pesoId}/peso-pdf`
   (gated by the owning Controlo Approve policy) + the "Gerar PDF do Peso" action on the Approve
   review sheet for decided records, with the outcome (file name + relative convention target —
   never a filesystem path) rendered by the page-owned adapter.
7. History/immutability preserved: generation is read-only on the Peso (no version bump, no
   record write); content derives only from the frozen sheet; decided records are offered the
   action; undecided/submitted records are refused `not-decided`.

## 3. Decision points (recorded)

- **Filename convention:** the repo's settled authority is `Peso_<reference>_<machine>.pdf`
  (P2-T05 contract §12.1 and delta §7.4, both in-repo, three consistent mentions); the master
  plan/P2-T08 handoff prose says `Peso_<reference>_<line>.pdf` (external
  `DOCUMENTS_AND_FILES.md` is not in this repo). Used **`<machine>`** (the only convention
  closed inside this repository; the value is the read model's `Production.Machine`).
- **"Data" in the identification block:** the shared read model carries no production date
  (Create's strip shows `—` when opening via `pesoId`); the PDF uses **`SubmittedAt`** (the
  record's completion date, always present on decided records).
- **Generation precondition:** DECIDED (`aprovado`/`nao_aprovado`) + production-bound
  (`Production` projection present). A pending `Job On por associar` Peso has no document target
  (`production-binding-missing`); documents become available after the decision (Create R8 seam
  statement unchanged).
- **Identification order (fixed):** Referência → Produção → Máquina → Data → CM → Processo →
  Estado → Lote; no Boquilha in the block; lot rendered as plain value (no `L` prefix).
- **Comparison:** per-reading comparison = **Peso** + **Volume de água** (capacity), with
  deviations and summary computed from THIS sheet's own rows (per-CM); the water MASS is
  rendered as the calculation input, never a second comparison.
- **N readings:** all rows render; nothing fixed at 4 lines; pagination repeats the table header.

## 4. Files changed / added

**New (application — `src/DMO.Application/Documents/`):**
`PesoPdfModels.cs` (document model, output, typed results/refusals),
`PesoPdfNaming.cs` (deterministic naming, fail-closed segments),
`PesoPdfComposer.cs` (pure composition from the shared sheet),
`PesoPdfRenderer.cs` (`IPesoPdfRenderer` + dependency-free PDF 1.4 renderer),
`PesoPdfFileStore.cs` (`IPesoPdfFileStore` + `ServerHostPesoPdfFileStore`),
`PesoPdfService.cs` (`IPesoPdfService` + orchestration).

**New (web):** `src/DMO.Web/Endpoints/DocumentsEndpoints.cs` (the route).

**Changed (web):** `src/DMO.Web/Program.cs` (DI + `MapDocumentsEndpoints`),
`src/DMO.Web/Pages/Controlo/Approve/Index.cshtml` + `.cs` (PDF action + outcome region),
`src/DMO.Web/wwwroot/js/dmo-controlo-approve.js` (adapter: one POST, pending state, outcome),
`src/DMO.Web/wwwroot/css/dmo-controlo-approve.css` (styles).

**New (tests):** `tests/DMO.UnitTests/Documents/` (naming, composer, renderer incl. xref-walk +
pagination + no-path, service orchestration, real file store over temp dirs);
`tests/DMO.IntegrationTests/ControloApprove/PesoPdfEndpointsTests.cs` (HTTP: generate/store on
disk, already-available, not-decided, not-configured, workspace-unavailable, create-only 403).

**Changed (tests, regression):** BND2 boundary row updated (Peso PDF execution sanctioned on the
Approve surface; email/send/regeneration/document-identity still forbidden everywhere) +
`P2T06ProductionScan` token split + P2-T05 allow-list disclosed extension for
`src/DMO.Application/Documents/` and `DocumentsEndpoints.cs` (same pattern as P2-T06/P2-T07) +
approve adapter `.mjs` harness scenarios (Peso PDF) + `P2T06RenderingTests` decided-record render
row.

## 5. Final path / filename used

```text
<base>/<reference>/<production-number>/Peso_<reference>_<machine>.pdf
```

- `<base>` = the operator-configured `pdf_directory_settings.base_directory` (Definições;
  probed server-side before any write — `workspace-unavailable` is distinct from a missing file).
- Example from the HTTP test: `…/ref-PDF1/pn-PDF1/Peso_ref-PDF1_B1.pdf`.

## 6. Where each PDF data block comes from (all `PesoSheetReadModel` members)

| Block | Source |
|---|---|
| Identificação: Referência / Produção / Máquina | `Production.Reference / .ProductionNumber / .Machine` (Job On traversal facts) |
| Identificação: Data | `SubmittedAt` (decision-completion date; fallback `CreatedAt` defensive) |
| Identificação: CM | `Context.FrozenToolReference` (frozen CM triple) |
| Identificação: Processo | `Context.Tool.Processo` token (live projection) |
| Identificação: Estado | `Status` token → `PesoStatusTokens.DisplayLabel` |
| Identificação: Lote | `Context.FrozenToolLot` verbatim (no `L` prefix) |
| Entradas de cálculo | `WaterTemperature`, `VolumeMarisaBq`, `VolumePuncaoPu`, `GlassDensityGCm3`, `PreviousProductionEndReference`, `PreviousAverageWeightReference` |
| Leituras (N) | `Rows` verbatim: `WaterWeightG` (input) + `CapacityCm3` (Volume de água) + `GlassWeightG` (Peso); deviations vs this sheet's own average capacity |
| Resumo | This sheet's own averages (`AverageWaterWeightG/AverageCapacityCm3/AverageGlassWeightG`) + frozen `GlassDensityGCm3` |
| Footer | `PesoId`, `Version`, page number, generation timestamp (render-time fact, not a record fact) |

No member is recomputed, re-resolved or fetched elsewhere; the renderer never receives the base
directory and never prints paths.

## 7. Tests executed

- `dotnet build DMO.slnx` — **0 warnings / 0 errors**.
- `DMO.UnitTests` full project — **710/710 green** (incl. the new 47 Documents tests: naming,
  composer, renderer with xref-table walk, multi-page, no-path, determinism; service refusals +
  no-overwrite; real file store over temp dirs incl. no temp debris).
- `DMO.IntegrationTests` focused:
  - `ControloApprove` (Approve regression + new endpoint/render tests) — **53 passed, 4 skipped**
    (env-gated DB/JS; the node harness ran separately and passed).
  - `ControloCreate` (P2-T05 regression touched only by the disclosed allow-list extension) —
    **57 passed, 10 skipped** (env-gated DB).
- Adapter behavioral harness (`node …/dmo-controlo-approve-adapter.behavior.mjs`) — **PASSED**,
  incl. the new Peso PDF scenarios (one request, no body, pending state, outcome shown,
  refusal into errors presentation, no reload).
- Full suite intentionally NOT run (per slice rules).

## 8. Gaps / notes

- **`<line>` vs `<machine>` filename token:** external `DOCUMENTS_AND_FILES.md` is not in this
  repo; the in-repo closed convention (`<machine>`) was followed. If the external contract later
  fixes `<line>` as a different value, only `PesoPdfNaming.TryCompose` changes (one unit-test
  surface).
- **"Data" source:** the shared read model has no production date; `SubmittedAt` was chosen.
  If the production date is required, it must come from a future read-model extension (no new
  traversal was added per the slice's single-read rule).
- **Availability states:** the full P2-T08 availability vocabulary (Disponível / Ainda não
  gerado / Ficheiro em falta / Versões disponíveis) is not implemented — this slice closes
  generation + storage + outcome display only; the outcome tokens (`generated`,
  `already-available`) leave the availability mapping seam clean.
- **No `production_id`, no document metadata table, no migration** — the deterministic
  filename makes the target derivable; immutability is enforced by the no-overwrite write.