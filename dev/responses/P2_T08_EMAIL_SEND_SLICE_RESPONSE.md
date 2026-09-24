# P2-T08 (slice) — MANUAL EMAIL SEND OF THE PESO PDF — IMPLEMENTATION RESPONSE

**Slice:** documents handoff, second slice — connect the manual email send of the Peso PDF to
Controlo_Create, reusing the existing Definições email/template/list data and the Documents
infrastructure. **Task class:** directed functional slice (no new general audit; no PDF redesign;
no Approve change; no new tables/migrations). **Status:** IMPLEMENTED — focused tests green;
awaiting review per the STOP rules (no availability registration, no route registration in
`CurrentBuildAvailable`).

---

## 1. Baseline / authority

| Item | Value |
|---|---|
| Baseline commit | `3b6f513408dd6a52080a4a83b40da64ae72d0da3` (P2-T08 correction: action on Create + production date) — working tree clean |
| Authority consumed | delta report §8/§9/§10 (lists, routing INTENT, templates; §9.4 explicitly defers the machine→list rule and the transport to the implementing workstream); P2-T05 contract §13/§14 (lists/templates as the single configured data source; Q-PLACE: no placeholder syntax); P2-T08 handoff ("use known production context; avoid re-asking what configuration determines"); previous slice (Documents infra: deterministic PDF, no-overwrite, shared read) |
| Non-implemented (unchanged) | Resumo, Pegamentos, JobOn outputs, redesign, external notifications, new blocking rules, migrations/new tables, ANY change to Approve |

## 2. Scope of the slice (done)

1. **Action next to the Peso PDF output on Controlo Create (R8):** after the PDF outcome is shown
   (gerado / já disponível), the manual-send region reveals (no reload): the configured recipient
   lists from Definições (single list applied automatically; several → operator selects the
   applicable one; zero → state info with the send button disabled), the send button and the send
   outcome. The user decides WHEN to send.
2. **Automatic template resolution (the only applicability rule the model fixes — Q-DOCTYPE):**
   the `peso` template wins; absent that, a `generic` (document_type NULL) template is used; more
   than one template at the winning precedence → typed `email-template-ambiguous` (the model
   fixes no further selection precedence — delta §9.4/§10.5; nothing invented). No template →
   `email-template-not-configured`.
3. **Recipients resolution:** exclusively from the configured email lists (never hardcoded, no
   second source of truth): an operator selection is honoured; with none, exactly ONE configured
   list applies automatically; several lists without selection → `email-list-selection-required`
   (the app never guesses — it only avoids re-asking for what configuration determines). Unknown
   id → `email-list-not-found`; empty list → `email-list-empty`.
4. **The attached document is the EXISTING Peso PDF** stored by the Documents infrastructure
   (deterministic target): read through the file store's new attachment read; `pdf-not-generated`
   when the file is absent — the Peso is NEVER regenerated/recalculated for sending (no
   regeneration path exists in the send flow).
5. **Transport:** a thin SMTP adapter driven by the `Email:Transport` configuration section
   (host/port/username/password/from/enableSsl — the only NEW configuration; there was NO email
   transport configuration anywhere in the repository). Missing/incomplete configuration →
   typed `email-transport-not-configured` WITHOUT network access and WITHOUT host startup
   failure (sending is optional operational work). Transport uses the BCL `SmtpClient`
   (dependency-free; no mail package exists in the repo).
6. **Subject/body verbatim:** no placeholder syntax exists in this repository (Q-PLACE /
   delta §10.4) — no substitution is ever performed; the operator authors concrete texts.
7. **Evidence:** the send result (file, template, recipients, timestamp) is returned to the page
   and recorded as status-level application logging only. The current persistence model has NO
   send table and no migration is authorized → **no send row is written** (model does not
   support it).
8. **Failure semantics:** a transport/routing failure is a typed 409 (`email-send-failed` /
   typed config refusals) — the approval, the `peso_id` and the existing document are NEVER
   altered (proved at service and HTTP level; the shared store is untouched).

## 3. Routing/template resolution — exact rules (all from the configured data)

| Input | Rule | Refusal when unresolvable |
|---|---|---|
| Template | `document_type = 'peso'` wins; else `document_type IS NULL` (generic); >1 at the winning precedence | `email-template-not-configured` / `email-template-ambiguous` |
| Recipients | operator-selected list id honoured; else exactly 1 configured list may apply; >1 lists → operator choice; 0 lists → refused | `email-list-not-found` / `email-list-selection-required` / `email-list-not-configured` / `email-list-empty` |
| Machine/line | the machine is the known production context carried in the evidence; **no machine→list association exists in the current schema** (delta §9.4 deferred it) — the slice deliberately does NOT invent one and does NOT migrate | — |
| Transport | `Email:Transport` (host + from required; port default 587; SSL default on) | `email-transport-not-configured` |

## 4. What is attached

The bytes of the EXISTING deterministic file
`<base>/<reference>/<production-number>/Peso_<reference>_<machine>.pdf` — the same file the
Documents infrastructure generated/stored (read-only; MIME `application/pdf`; filename =
deterministic convention name). No regeneration, no recalculation, no second document authority.

## 5. Files changed / added

**Application (`src/DMO.Application/Documents/` — stays inside the P2-T05 owned allow-list):**
`EmailTransport.cs` (`EmailTransportOptions`, `EmailMessage`, `IEmailTransport`,
`SmtpEmailTransport`), `PesoPdfSendModels.cs` (command, evidence, typed result/refusals),
`PesoPdfSendService.cs` (`IPesoPdfSendService` + orchestration), `PesoPdfFileStore.cs` (added
`ReadAsync` — the existing-file attachment read; typed Found/Missing/Failed).

**Web:** `src/DMO.Web/Endpoints/DocumentsEndpoints.cs` (route
`POST /controlo/create/pesos/{pesoId}/peso-pdf/send`, body = optional `emailListId` only;
14 typed refusal tokens; send evidence response), `src/DMO.Web/Program.cs` (DI + `Email:Transport`
binding — no fail-fast), `src/DMO.Web/Pages/Controlo/Create.cshtml` + `.cs` (R8 send region:
lists from `IControloDefinicoesService.ListEmailListsAsync` — presentation of the configured
data, never a second source; `EmailListOptionPresentation`), `src/DMO.Web/wwwroot/js/dmo-controlo.js`
(send wiring: reveal after PDF output, one POST, pending, evidence outcome, typed refusals, no
reload; local selection-required notice), `src/DMO.Web/wwwroot/css/dmo-controlo.css`.

**Tests:** `tests/DMO.UnitTests/Documents/PesoPdfSendServiceTests.cs` (+13),
`SmtpEmailTransportTests.cs` (+3, no network), `ServerHostPesoPdfFileStoreTests.cs` (attachment
read); `tests/DMO.IntegrationTests/ControloCreate/PesoPdfSendEndpointsTests.cs` (+6, HTTP over
the real stack with a recording transport double: generate→send happy path, selection-required,
pdf-not-generated, template-not-configured, transport-failure-never-changes-the-Peso,
approve-only denied); create-adapter `.mjs` harness (+3 scenarios: S11 single-list auto send,
S12 multi-list selection gate, S13 typed refusal).

## 6. Tests executed

- `dotnet build DMO.slnx` — **0 warnings / 0 errors**.
- `DMO.UnitTests` full project — **729/729** (incl. the 66 Documents tests: resolution rules,
  verbatim subject/body, existing-bytes attachment, typed refusals, no-write-on-failure,
  transport validation without network, store read states).
- `DMO.IntegrationTests` focused — `ControloCreate` + `ControloApprove` **117 passed / 14
  skipped** (env-gated DB/JS only; Approve regression untouched — BND2 stays green because the
  send machinery lives in the Documents area, never in Approve sources).
- Adapter harnesses (node): create **14/14 PASSED** (S11–S13 new), approve **PASSED** (unchanged).
- Full suite intentionally NOT run (per slice rules).

## 7. Gaps / notes (real)

1. **Machine/line → email-list association does not exist** in the current schema (delta §9.4
   defers it; no table/column anywhere). Automatic recipient resolution is therefore limited to
   "single configured list" or operator selection among configured lists. A real
   machine→list binding needs its own authored contract + migration (explicitly out of scope;
   no migration authorized).
2. **No send-table evidence:** the model has no send persistence; the slice registers the
   minimum the model supports — the returned evidence + status-level application logging. A
   durable send history needs a migration (future slice).
3. **Transport/account:** no email transport configuration existed in the repository; the slice
   adds the minimal `Email:Transport` SMTP section (no hardcoded account anywhere; Supabase
   SMTP would need its private credentials — not present). A real send against a live SMTP
   server was not exercised in tests (no server available); the orchestration is proven over the
   recording double and the adapter validation is unit-proven.
4. **Placeholder syntax:** none exists (Q-PLACE / delta §10.4) — the subject/body travel
   verbatim; contextual values (reference/production/machine/date) are NOT substituted until a
   syntax is contracted.
5. **Preview** is not implemented (no contract requires it on this path); the flow is
   generate → (optional list selection) → send → evidence, all in-page without reload.