# P2-T08 (slice) — EMAIL GROUP ROUTING B/C — IMPLEMENTATION RESPONSE

**Slice:** documents handoff, third slice — replace the manual list selection of the Peso email
send with the simple group routing <b>máquina → grupo B/C → template de email → destinatários
associados</b>, configured clearly in Controlo → Definições. **Task class:** directed functional
slice (no new general audit; no PDF redesign; no Approve change; no generic rule architecture).
**Status:** IMPLEMENTED — focused tests green; awaiting review per the STOP rules (no
availability registration, no workflow engine).

---

## 1. Baseline / authority

| Item | Value |
|---|---|
| Baseline commit | `82c1642286b7939a67be4264a750cf033dcb33ee` (manual email send slice) — working tree clean |
| Authority consumed | previous slice (send flow, existing PDF reuse, transport); delta §9.4 (the machine→list rule was explicitly deferred to the implementing workstream — this slice FIXES the small concrete rule: two groups by machine letter); P2-T05 §13/§14 (lists/templates are the single configured recipient/template source) |
| Non-implemented (unchanged) | JobOn outputs, Resume, Pegamentos, preview, new placeholders, persistent send history, redesign; NO per-machine (B1…C3) associations; no manual list selection anywhere |

## 2. Schema / migration delta (the smallest persistent change that removes the ambiguity)

**Migration `20260924182527_EmailTemplateGroupRouting`** (EF-generated; index + CHECKs + FK from
the entity configuration; snapshot updated by EF) — two columns on the existing `email_templates`
row (no new table, no generic rules):

- `machine_group` text NULL, CHECK `IN ('B','C')` — the ONE group the template serves
  (B resolves B1/B2/B3; C resolves C1/C2/C3; no per-machine rows).
- `email_list_id` uuid NULL, FK RESTRICT → `email_lists` — the template's associated recipients,
  reusing the existing list mechanism as the recipient container ("Template B + emails
  associados"). The RESTRICT FK makes a silently-broken routing impossible (a list referenced by
  a template can never be deleted).

The relationship lives ON the template row — the smallest change that makes "grupo → template →
lista" unambiguous. `email_lists`/`email_list_recipients` are untouched.

## 3. Resolução B/C (PesoPdfSendService — agora automática, sem seleção)

1. `machine` (da sheet partilhada) → `EmailMachineGroupTokens.FromMachine`: `B1/B2/B3 → B`,
   `C1/C2/C3 → C`; **qualquer outra máquina/valor → `machine-group-unsupported` — fail closed,
   sem adivinhar** (mensagem informativa com os grupos válidos).
2. Template do grupo: os templates configurados com `machine_group = grupo`; **0 →**
   `email-group-not-configured` (informativo — "defina o Template do grupo e a lista associada");
   **>1 →** `email-template-ambiguous` (não existe regra de precedência no modelo — nada
   inventado).
3. Destinatários: a lista associada do template (`email_list_id`) → endereços ASC; sem lista no
   template (defensivo — as Definições recusam routing incompleto) → `email-group-not-configured`;
   lista inexistente → `email-list-not-found`; lista vazia → `email-list-empty`. Nunca hardcoded.
4. O resto da slice anterior mantém-se: anexo = **PDF existente** (nunca regenerado), subject/
   body **verbatim**, transporte SMTP configurado, evidência devolvida (agora inclui o grupo),
   falha de transporte = 409 tipada **sem alterar aprovação/peso_id**; envio sempre manual no
   Controlo_Create, sem dropdowns.

## 4. Definições (configuração clara)

- **Secção Templates de email** ganha: colunas "Grupo de máquinas" e "Lista de destinatários" e
  dois novos campos no formulário (grupo B = "B (B1/B2/B3)" / C = "C (C1/C2/C3)"; lista
  configurada), com hint de que ambos viajam juntos.
- Regra de integridade (serviço `ControloDefinicoesService`): grupo XOR lista → 400
  `EMAIL_LIST_NOT_FOUND`; lista desconhecida → `EMAIL_LIST_NOT_FOUND`; grupo fora de B/C →
  `MACHINE_GROUP_UNKNOWN` (validator). Templates sem grupo/lista continuam templates genéricos
  normais.
- **Create:** o dropdown de listas foi **removido**; a região de envio mostra só o estado (grupo
  da máquina) + botão + evidência; o request de envio **não tem body**.

## 5. Ficheiros alterados

- **Domain:** `EmailMachineGroup.cs` (novo — enum B/C + tokens + mapeamento B1-B3/C1-C3 fail
  closed), `EmailTemplate.cs` (+`MachineGroup`, +`EmailListId`).
- **Infrastructure:** `EmailTemplateEntity.cs`/`EmailTemplateEntityConfiguration.cs` (+colunas,
  CHECK, FK RESTRICT); `EmailTemplateRepository.cs` (project/apply); migration
  `20260924182527_EmailTemplateGroupRouting` + Designer; snapshot (EF).
- **Application:** `ControloDefinicoesModels/Validator/Service` (commands com grupo+lista,
  tokens `MACHINE_GROUP_UNKNOWN`/`EMAIL_LIST_NOT_FOUND`, regra do par completo); `Documents/
  PesoPdfSendModels.cs` (command sem lista; refusals `machine-group-unsupported`/
  `email-group-not-configured`; evidência com grupo; removidos os tokens de seleção manual);
  `Documents/PesoPdfSendService.cs` (resolução automática B/C).
- **Web:** `DocumentsEndpoints.cs` (rota sem body, tokens novos); `ControloDefinicoesEndpoints.cs`
  (requests/responses com machineGroup/emailListId); `Create.cshtml/.cs` (sem dropdown, hint do
  grupo, `PdfMachineGroup`); `Definicoes.cshtml` (colunas/campos); `dmo-controlo.js` (payload e
  prefill de template com grupo/lista; envio sem body); css.
- **Testes:** unit (EmailMachineGroupTests, PesoPdfSendServiceTests reescrito com B/C, SET7b do
  serviço Definições, SET7 do validator); integration (PesoPdfSendEndpointsTests reescrito B/C,
  ControloDefinicoesEndpointsTests com o routing); harness JS (S11 envio automático sem body,
  S12 refusal de grupo); allow-list P2-T05 com a migration nova.

## 6. Testes executados

- **Build:** 0 warnings / 0 erros.
- **DMO.UnitTests full:** **750/750** — B1/B2/B3→B, C1/C2/C3→C (serviço + tokens), máquinas fora
  do conjunto (X1/B0/C4/desconhecida) → fail closed, grupo sem template/emails → refusais
  informativas, routing incompleto no Definições, destinatários corretos (ASC), anexo = bytes
  existentes, verbatim, transporte, sem writes em falha.
- **DMO.IntegrationTests focados:** ControloCreate + ControloApprove **118 passed / 14 skipped**
  (env-gated) — happy path B e C sobre o stack real (gerar → enviar sem body, template/lista do
  grupo), email-group-not-configured, pdf-not-generated, transport-failure-immutability,
  approve-only 403, round-trip HTTP do routing (grupo+lista), BND2/regressões Approve intactas.
- **Harness JS:** create **13/13 PASSED** (S11/S12 novos), approve PASSED (inalterado).
- Full suite não corrida (regra da slice).

## 7. Gaps reais

1. **Um template por grupo** é o desenho fechado (sem precedência inventada): >1 template B → 
   `email-template-ambiguous`. Se no futuro se quiserem múltiplos templates por grupo com regra
   de escolha, isso é um contrato novo.
2. **Sem persistência de envio** (mantém-se — sem tabela, sem migração autorizada): evidência =
   resultado na página + log de status.
3. **Transporte real não exercitado em testes** (sem servidor SMTP disponível) — o mesmo da
   slice anterior; orquestração provada com double gravador.
4. **Grupo derivado da máquina da produção (Job On)**: a regra "B1/B2/B3→B" aplica-se ao valor
   da sheet; um registo com máquina fora do conjunto fica simplesmente sem envio (recusa
   tipada), sem fallback.
5. **Placeholders/preview** continuam não implementados (sintaxe inexistente; nenhum contrato o
   exige neste caminho).