/* P2-T05 Controlo Create page-owned adapter (contract §8, §21.3).
 *
 * Two surfaces:
 *   create     — Novo controlo: calculate (route 8), save-create (route 5), save-edit (route 7),
 *                submit (route 9), associate (route 10), open-draft support.
 *   definicoes — the six Definições sections (routes 13–17; glass densities routes 18/19,
 *                post-closure correction contract §5.3).
 *
 * The adapter performs NO domain decision: backend validation is authoritative, entered values are
 * retained on refusal, and every persisted-state refusal surfaces its typed reason. No width
 * listener exists anywhere (LAY1/AC-N1): the page renders its fixed-desktop composition and
 * scrolling is handled by the layout, not by script.
 *
 * Idempotency guard (accepted convention). It reuses the frozen window.dmoFocus helper only when
 * genuinely required and never declares a second focus helper.
 */
if (!window.dmoControlo) {
  window.dmoControlo = (function () {
    "use strict";

    var basePath = "/controlo/create";

    function api(path, method, body) {
      var options = { method: method, headers: {} };
      if (body !== undefined && body !== null) {
        options.headers["Content-Type"] = "application/json";
        options.body = JSON.stringify(body);
      }
      return fetch(path, options).then(function (response) {
        if (response.status === 204) {
          return { ok: true, status: response.status, body: null };
        }
        return response.json().catch(function () { return {}; }).then(function (payload) {
          return { ok: response.ok, status: response.status, body: payload };
        });
      });
    }

    function formState(root) {
      var temperature = value(root, "[data-dmo-field-temperature]");
      var marisa = value(root, "[data-dmo-field-marisa]");
      var puncao = value(root, "[data-dmo-field-puncao]");
      var rows = [];
      root.querySelectorAll("[data-dmo-row]").forEach(function (row) {
        var weight = row.querySelector("[data-dmo-field='weight']");
        rows.push({ waterWeightG: toDecimal(weight ? weight.value : "") });
      });
      return {
        waterTemperature: toDecimal(temperature),
        volumeMarisaBq: toNullableDecimal(marisa),
        volumePuncaoPu: toNullableDecimal(puncao),
        previousProductionEndReference: nullableText(value(root, "[data-dmo-field-sap-end]")),
        previousAverageWeightReference: nullableText(value(root, "[data-dmo-field-sap-weight]")),
        rows: rows
      };
    }

    function anchors(root) {
      var state = root.querySelector("[data-dmo-peso-id]");
      return {
        pesoId: state ? state.getAttribute("data-dmo-peso-id") || null : null,
        version: state ? parseInt(state.getAttribute("data-dmo-version") || "1", 10) : 1,
        cmId: state ? state.getAttribute("data-dmo-anchor-cm") || null : null,
        toolId: state ? state.getAttribute("data-dmo-anchor-tool") || null : null,
        submitted: state ? state.getAttribute("data-dmo-submitted") === "true" : false
      };
    }

    function setAnchor(root, pesoId, version) {
      var state = root.querySelector("[data-dmo-peso-id]");
      if (!state) { return; }
      if (pesoId) { state.setAttribute("data-dmo-peso-id", pesoId); }
      state.setAttribute("data-dmo-version", String(version));
    }

    function renderErrors(root, payload) {
      var region = root.querySelector("[data-dmo-controlo-state]");
      if (!region) { return; }
      var messages = [];
      if (payload && payload.errors && Array.isArray(payload.errors)) {
        messages = payload.errors;
      } else if (payload && payload.reason) {
        messages = [payload.reason + (payload.message ? " — " + payload.message : "")];
      }
      if (messages.length === 0) { messages = ["A operação não foi concluída."]; }
      region.hidden = false;
      region.setAttribute("role", "alert");
      // A later non-conflict failure must never leave a stale conflict marker behind.
      region.removeAttribute("data-dmo-conflict");
      region.textContent = "";
      var list = document.createElement("ul");
      messages.forEach(function (message) {
        var item = document.createElement("li");
        item.textContent = message;
        list.appendChild(item);
      });
      region.appendChild(list);
    }

    function renderResults(root, payload) {
      var table = root.querySelector("[data-dmo-results-table]");
      var hint = root.querySelector("[data-dmo-results-hint]");
      var missing = root.querySelector("[data-dmo-calculation-missing]");
      if (missing) { missing.hidden = true; }
      if (!table) { return; }
      var body = table.querySelector("tbody");
      body.textContent = "";
      payload.rows.forEach(function (row) {
        var tr = document.createElement("tr");
        tr.setAttribute("data-dmo-result-row", String(row.rowPosition));
        [String(row.rowPosition), format(row.waterWeightG), format(row.capacityCm3), "", "", format(row.glassWeightG)]
          .forEach(function (cell) {
            var td = document.createElement("td");
            td.textContent = cell;
            tr.appendChild(td);
          });
        body.appendChild(tr);
      });
      if (hint) { hint.hidden = true; }
      var summary = root.querySelector("[data-dmo-results-summary]");
      if (summary) {
        var place = function (selector, value) {
          var node = root.querySelector(selector);
          if (node) { node.textContent = value; }
        };
        var water = payload.rows.reduce(function (acc, row) { return acc + row.waterWeightG; }, 0) / Math.max(payload.rows.length, 1);
        var capacity = payload.rows.reduce(function (acc, row) { return acc + row.capacityCm3; }, 0) / Math.max(payload.rows.length, 1);
        var glass = payload.rows.reduce(function (acc, row) { return acc + row.glassWeightG; }, 0) / Math.max(payload.rows.length, 1);
        place("[data-dmo-summary-water]", format(water));
        place("[data-dmo-summary-capacity]", format(capacity));
        place("[data-dmo-summary-glass]", format(glass));
        place("[data-dmo-summary-density]", format(payload.glassDensityGCm3));
      }
    }

    function showSaved(root, message) {
      var region = root.querySelector("[data-dmo-controlo-state]");
      if (!region) { return; }
      region.hidden = false;
      region.removeAttribute("role");
      region.textContent = message;
    }

    /**
     * Binds an event listener ONLY when the target element exists (D1 correction). Absence of an
     * element is a VALID state — e.g. the submitted Peso view deliberately renders no editable
     * actions (only the disabled submit) — so the adapter must initialize normally and never throw
     * on a missing control; the bindings that DO exist still work.
     */
    function on(root, selector, event, handler) {
      var element = root.querySelector(selector);
      if (element) { element.addEventListener(event, handler); }
    }

    /**
     * The page-owned failure presentation of a mutation response (D2 correction): a typed
     * <c>stale-version</c> refusal enters the accepted conflict state with an explicit recovery
     * action (reload the authoritative current server state) — never an automatic retry, never a
     * silent merge, never an overwrite of the newer server version. Every other typed failure keeps
     * the existing <see cref="renderErrors"/> behavior exactly.
     */
    function renderMutationFailure(root, response) {
      var payload = response && response.body ? response.body : null;
      if (payload && payload.reason === "stale-version") {
        renderConflict(root, payload.message);
      } else {
        renderErrors(root, payload);
      }
    }

    /**
     * The conflict presentation (freeze §4 <c>conflict</c>): a clear message plus supplied recovery
     * choices — one explicit reload action that fetches the authoritative current server state.
     */
    function renderConflict(root, message) {
      var region = root.querySelector("[data-dmo-controlo-state]");
      if (!region) { return; }
      region.hidden = false;
      region.setAttribute("role", "alert");
      region.setAttribute("data-dmo-conflict", "true");
      region.textContent = "";
      var heading = document.createElement("strong");
      heading.textContent = "Conflito — os dados foram alterados por outra ação; nada foi guardado.";
      region.appendChild(heading);
      var detail = document.createElement("p");
      detail.textContent = message || "Recarregue para obter o estado atual do servidor.";
      region.appendChild(detail);
      var recover = document.createElement("button");
      recover.type = "button";
      recover.textContent = "Recarregar estado atual";
      recover.setAttribute("data-dmo-conflict-reload", "true");
      recover.addEventListener("click", function () { window.location.reload(); });
      region.appendChild(recover);
    }

    function withPending(root, key) {
      var buttons = root.querySelectorAll("[data-dmo-action='" + key + "']");
      var originals = [];
      buttons.forEach(function (button) {
        originals.push({ button: button, text: button.textContent, disabled: button.disabled });
        var pending = button.getAttribute("data-dmo-action-pending");
        if (pending) { button.textContent = pending; }
        button.disabled = true;
      });
      return function () {
        originals.forEach(function (entry) {
          entry.button.textContent = entry.text;
          entry.button.disabled = entry.disabled;
        });
      };
    }

    // ------------------------------------------------------------------ create surface
    function wireCreate(root) {
      var anchorsBox = anchors(root);

      on(root, "[data-dmo-action='calculate']", "click", function () {
        var release = withPending(root, "calculate");
        var state = formState(root);
        api(basePath + "/calculate", "POST", { cmId: anchorsBox.cmId, pendingToolId: anchorsBox.toolId, waterTemperature: state.waterTemperature, volumeMarisaBq: state.volumeMarisaBq, volumePuncaoPu: state.volumePuncaoPu, previousProductionEndReference: state.previousProductionEndReference, previousAverageWeightReference: state.previousAverageWeightReference, rows: state.rows }).then(function (response) {
          release();
          if (response.ok) { renderResults(root, response.body); }
          else { renderMutationFailure(root, response); }
        });
      });

      on(root, "[data-dmo-action='save']", "click", function () {
        var release = withPending(root, "save");
        var state = formState(root);
        var payload = { cmId: anchorsBox.cmId, pendingToolId: anchorsBox.toolId, waterTemperature: state.waterTemperature, volumeMarisaBq: state.volumeMarisaBq, volumePuncaoPu: state.volumePuncaoPu, previousProductionEndReference: state.previousProductionEndReference, previousAverageWeightReference: state.previousAverageWeightReference, rows: state.rows };
        var request = anchorsBox.pesoId
          ? api(basePath + "/pesos/" + anchorsBox.pesoId, "PUT", Object.assign({ expectedVersion: anchorsBox.version }, payload))
          : api(basePath + "/pesos", "POST", payload);
        request.then(function (response) {
          release();
          if (response.ok) {
            setAnchor(root, response.body.pesoId || anchorsBox.pesoId, response.body.version || anchorsBox.version);
            anchorsBox = anchors(root);
            showSaved(root, anchorsBox.pesoId ? "Controlo guardado (versão " + anchorsBox.version + ")." : "Controlo criado.");
          } else {
            renderMutationFailure(root, response);
          }
        });
      });

      on(root, "[data-dmo-action='submit']", "click", function () {
        if (!anchorsBox.pesoId) { renderErrors(root, { errors: ["Guarde o controlo antes de submeter para aprovação."] }); return; }
        var release = withPending(root, "submit");
        api(basePath + "/pesos/" + anchorsBox.pesoId + "/submit", "POST", { expectedVersion: anchorsBox.version }).then(function (response) {
          release();
          if (response.ok) {
            window.location.reload();
          } else {
            renderMutationFailure(root, response);
          }
        });
      });

      on(root, "[data-dmo-action='cancel']", "click", function () {
        window.location.href = "/controlo/create";
      });

      // The associate action exists only in the truthful pending case; the guard keeps the binding
      // optional (D1: absence is valid, initialization must never throw).
      on(root, "[data-dmo-associate]", "click", function () {
        if (!anchorsBox.pesoId) { renderErrors(root, { errors: ["Guarde o controlo antes de associar."] }); return; }
        var select = root.querySelector("[data-dmo-associate-candidate]");
        var option = select.options[select.selectedIndex];
        var cmId = option ? option.getAttribute("data-dmo-candidate-id") : null;
        if (!cmId) { return; }
        var release = withPending(root, "associate");
        api(basePath + "/pesos/" + anchorsBox.pesoId + "/associate", "POST", { cmId: cmId, expectedVersion: anchorsBox.version }).then(function (response) {
          release();
          if (response.ok) { window.location.reload(); }
          else { renderMutationFailure(root, response); }
        });
      });

      // MeasurementRows add/remove focus rules are managed by the shared dmo-measurement-rows.js;
      // this adapter only reads back the rendered rows.
    }

    // ---------------------------------------------------------------- definicoes surface
    function wireRepairers(root) {
      on(root, "[data-dmo-repairer-add]", "click", function () {
        var name = value(root, "[data-dmo-repairer-new-name]");
        api("/controlo/create/definicoes/repairers", "POST", { name: name }).then(function (response) {
          if (response.ok) { window.location.reload(); }
          else { renderMutationFailure(root, response); }
        });
      });

      root.querySelectorAll("[data-dmo-repairer-rename-save]").forEach(function (button) {
        button.addEventListener("click", function () {
          var id = button.getAttribute("data-dmo-repairer-rename-save");
          var input = root.querySelector("[data-dmo-repairer-rename='" + id + "']");
          api("/controlo/create/definicoes/repairers/" + id, "PUT", { expectedVersion: parseInt(input.getAttribute("data-dmo-repairer-version") || "1", 10), name: input.value }).then(function (response) {
            if (response.ok) { window.location.reload(); }
            else { renderMutationFailure(root, response); }
          });
        });
      });
    }

    function wireAssignments(root) {
      root.querySelectorAll("[data-dmo-assignment-set]").forEach(function (button) {
        button.addEventListener("click", function () {
          var machine = button.getAttribute("data-dmo-assignment-set");
          var select = root.querySelector("[data-dmo-assignment-repairer='" + machine + "']");
          var version = parseInt(select.getAttribute("data-dmo-assignment-version") || "1", 10);
          api("/controlo/create/definicoes/machine-assignments/" + machine, "PUT", { repairerId: select.value ? select.value : null, expectedVersion: version }).then(function (response) {
            if (response.ok) { window.location.reload(); }
            else { renderMutationFailure(root, response); }
          });
        });
      });

      root.querySelectorAll("[data-dmo-assignment-clear]").forEach(function (button) {
        button.addEventListener("click", function () {
          var machine = button.getAttribute("data-dmo-assignment-clear");
          var select = root.querySelector("[data-dmo-assignment-repairer='" + machine + "']");
          var version = parseInt(select.getAttribute("data-dmo-assignment-version") || "1", 10);
          api("/controlo/create/definicoes/machine-assignments/" + machine, "PUT", { repairerId: null, expectedVersion: version }).then(function (response) {
            if (response.ok) { window.location.reload(); }
            else { renderMutationFailure(root, response); }
          });
        });
      });
    }

    function wirePdfDirectory(root) {
      on(root, "[data-dmo-pdf-save]", "click", function () {
        var input = root.querySelector("[data-dmo-pdf-directory]");
        var configured = input.getAttribute("data-dmo-pdf-configured") === "true";
        var version = parseInt(input.getAttribute("data-dmo-pdf-version") || "0", 10);
        api("/controlo/create/definicoes/pdf-directory", "PUT", { baseDirectory: input.value, expectedVersion: configured ? version : null }).then(function (response) {
          if (response.ok) {
            input.setAttribute("data-dmo-pdf-configured", "true");
            input.setAttribute("data-dmo-pdf-version", String(response.body.version));
            showSaved(root, "Diretório base guardado (versão " + response.body.version + ").");
          } else { renderMutationFailure(root, response); }
        });
      });

      on(root, "[data-dmo-pdf-check]", "click", function () {
        api("/controlo/create/definicoes/pdf-directory/check", "POST").then(function (response) {
          var region = root.querySelector("[data-dmo-pdf-result]");
          region.hidden = false;
          region.textContent = pdfCheckLabel(response.body.state);
        });
      });
    }

    function pdfCheckLabel(state) {
      switch (state) {
        case "not-configured": return "Ainda não configurado.";
        case "ok": return "Diretório acessível (leitura e escrita pelo servidor).";
        case "directory-not-found": return "Diretório não encontrado no servidor.";
        case "not-a-directory": return "O caminho não é um diretório.";
        case "access-denied": return "Sem acesso de leitura/escrita a partir do servidor.";
        case "invalid-path": return "O caminho não é um caminho absoluto.";
        default: return "Falha na verificação do diretório.";
      }
    }

    function wireEmailLists(root) {
      on(root, "[data-dmo-list-create]", "click", function () {
        var name = value(root, "[data-dmo-list-new-name]");
        var recipients = recipientsFromTextarea(value(root, "[data-dmo-list-new-recipients]"));
        api("/controlo/create/definicoes/email-lists", "POST", { name: name, recipients: recipients }).then(function (response) {
          if (response.ok) { window.location.reload(); }
          else { renderMutationFailure(root, response); }
        });
      });

      root.querySelectorAll("[data-dmo-list-delete]").forEach(function (button) {
        button.addEventListener("click", function () {
          var id = button.getAttribute("data-dmo-list-delete");
          var name = button.getAttribute("data-dmo-list-delete-name");
          var version = button.getAttribute("data-dmo-list-delete-version");
          if (!window.confirm("Eliminar a lista de email \"" + name + "\"? (ação explícita)")) { return; }
          api("/controlo/create/definicoes/email-lists/" + id + "?expectedVersion=" + version + "&deleteConfirmed=true", "DELETE").then(function (response) {
            if (response.ok) { window.location.reload(); }
            else { renderMutationFailure(root, response); }
          });
        });
      });

      root.querySelectorAll("[data-dmo-list-name]").forEach(function (cell) {
        cell.addEventListener("click", function () {
          var id = cell.getAttribute("data-dmo-list-name");
          api("/controlo/create/definicoes/email-lists/" + id, "GET").then(function (response) {
            if (!response.ok) { renderErrors(root, response.body); return; }
            var list = response.body;
            root.querySelector("[data-dmo-list-new-name]").value = list.name;
            root.querySelector("[data-dmo-list-new-recipients]").value = list.recipients.join("\n");
            root.querySelector("[data-dmo-list-edit-id]").value = list.emailListId;
            root.querySelector("[data-dmo-list-edit-version]").value = list.version;
            root.querySelector("[data-dmo-list-edit-region]").hidden = false;
          });
        });
      });

      on(root, "[data-dmo-list-update]", "click", function () {
        var id = root.querySelector("[data-dmo-list-edit-id]").value;
        var version = parseInt(root.querySelector("[data-dmo-list-edit-version]").value || "1", 10);
        var name = value(root, "[data-dmo-list-new-name]");
        var recipients = recipientsFromTextarea(value(root, "[data-dmo-list-new-recipients]"));
        api("/controlo/create/definicoes/email-lists/" + id, "PUT", { expectedVersion: version, name: name, recipients: recipients }).then(function (response) {
          if (response.ok) { window.location.reload(); }
          else { renderMutationFailure(root, response); }
        });
      });

      on(root, "[data-dmo-list-edit-cancel]", "click", function () {
        window.location.reload();
      });
    }

    function wireEmailTemplates(root) {
      on(root, "[data-dmo-template-create]", "click", function () {
        var payload = templatePayload(root);
        api("/controlo/create/definicoes/email-templates", "POST", payload).then(function (response) {
          if (response.ok) { window.location.reload(); }
          else { renderMutationFailure(root, response); }
        });
      });

      root.querySelectorAll("[data-dmo-template-delete]").forEach(function (button) {
        button.addEventListener("click", function () {
          var id = button.getAttribute("data-dmo-template-delete");
          var name = button.getAttribute("data-dmo-template-delete-name");
          var version = button.getAttribute("data-dmo-template-delete-version");
          if (!window.confirm("Eliminar o template \"" + name + "\"? (ação explícita)")) { return; }
          api("/controlo/create/definicoes/email-templates/" + id + "?expectedVersion=" + version + "&deleteConfirmed=true", "DELETE").then(function (response) {
            if (response.ok) { window.location.reload(); }
            else { renderMutationFailure(root, response); }
          });
        });
      });

      root.querySelectorAll("[data-dmo-template-name]").forEach(function (cell) {
        cell.addEventListener("click", function () {
          var id = cell.getAttribute("data-dmo-template-name");
          api("/controlo/create/definicoes/email-templates/" + id, "GET").then(function (response) {
            if (!response.ok) { renderErrors(root, response.body); return; }
            var template = response.body;
            root.querySelector("[data-dmo-template-new-name]").value = template.name;
            root.querySelector("[data-dmo-template-new-subject]").value = template.subject;
            root.querySelector("[data-dmo-template-new-body]").value = template.body;
            root.querySelector("[data-dmo-template-new-type]").value = template.documentType || "";
            root.querySelector("[data-dmo-template-edit-id]").value = template.emailTemplateId;
            root.querySelector("[data-dmo-template-edit-version]").value = template.version;
            root.querySelector("[data-dmo-template-edit-region]").hidden = false;
          });
        });
      });

      on(root, "[data-dmo-template-update]", "click", function () {
        var id = root.querySelector("[data-dmo-template-edit-id]").value;
        var version = parseInt(root.querySelector("[data-dmo-template-edit-version]").value || "1", 10);
        var payload = templatePayload(root);
        payload.expectedVersion = version;
        api("/controlo/create/definicoes/email-templates/" + id, "PUT", payload).then(function (response) {
          if (response.ok) { window.location.reload(); }
          else { renderMutationFailure(root, response); }
        });
      });

      on(root, "[data-dmo-template-edit-cancel]", "click", function () {
        window.location.reload();
      });
    }

    function wireGlassDensities(root) {
      // One processo per save: ONLY the targeted row is submitted, with its observed version and
      // a strictly positive density (the backend is authoritative; a failure keeps the entry).
      root.querySelectorAll("[data-dmo-glass-density-save]").forEach(function (button) {
        button.addEventListener("click", function () {
          var processo = button.getAttribute("data-dmo-glass-density-save");
          var input = root.querySelector("[data-dmo-glass-density-input='" + processo + "']");
          var version = parseInt(input.getAttribute("data-dmo-glass-density-version") || "1", 10);
          api("/controlo/create/definicoes/glass-densities/" + processo, "PUT", { densityGcm3: toDecimal(input.value), expectedVersion: version }).then(function (response) {
            if (response.ok) { window.location.reload(); }
            else { renderMutationFailure(root, response); }
          });
        });
      });
    }

    function templatePayload(root) {
      return {
        name: value(root, "[data-dmo-template-new-name]"),
        subject: value(root, "[data-dmo-template-new-subject]"),
        body: value(root, "[data-dmo-template-new-body]"),
        documentType: nullableText(value(root, "[data-dmo-template-new-type]"))
      };
    }

    // -------------------------------------------------------------------------- helpers
    function value(root, selector) {
      var node = root.querySelector(selector);
      return node ? node.value : "";
    }

    function nullableText(text) {
      return text === null || text === undefined || text.trim() === "" ? null : text.trim();
    }

    function toDecimal(value) {
      var parsed = parseFloat(String(value).replace(",", "."));
      return isNaN(parsed) ? 0 : parsed;
    }

    function toNullableDecimal(value) {
      var text = String(value).trim();
      if (text === "") { return null; }
      return toDecimal(text);
    }

    function recipientsFromTextarea(text) {
      return String(text)
        .split(/\r?\n|\r|;/)
        .map(function (line) { return line.trim(); })
        .filter(function (line) { return line.length > 0; });
    }

    function format(value) {
      if (value === null || value === undefined || isNaN(value)) { return "—"; }
      return String(Math.round(value * 100) / 100).replace(".", ",");
    }

    /**
     * Wires the Peso PDF generation action (P2-T08 documents slice): an explicit human action on
     * decided, production-bound records that posts ONLY the canonical peso_id to the documents
     * route (Create owns the operational document work; Approve owns only the decision). The
     * backend is authoritative; the outcome region shows the deterministic output result (file
     * name + relative convention target) — never a filesystem path. A repeated invocation on an
     * already-generated target answers "já disponível" (the backend never overwrites). Refusals
     * enter the page-owned failure presentation (typed reason); pending state prevents duplicate
     * invocation while keeping the accessible name. Once a PDF exists (gerado / já disponível)
     * the manual-send region reveals next to it.
     */
    function wirePesoPdf(root) {
      var button = root.querySelector("[data-dmo-peso-pdf]");
      var outcome = root.querySelector("[data-dmo-peso-pdf-outcome]");
      var sendRegion = root.querySelector("[data-dmo-peso-pdf-send-region]");
      if (!button || !outcome) { return; }

      button.addEventListener("click", function () {
        var state = anchors(root);
        if (!state.pesoId || button.disabled) { return; }

        var original = button.textContent;
        var pending = button.getAttribute("data-dmo-peso-pdf-pending") || original;
        button.textContent = pending;
        button.disabled = true;

        api(basePath + "/pesos/" + state.pesoId + "/peso-pdf", "POST", null).then(function (response) {
          button.textContent = original;
          button.disabled = false;

          if (!response.ok) {
            renderMutationFailure(root, response);
            return;
          }

          var payload = response.body || {};
          outcome.hidden = false;
          outcome.removeAttribute("role");
          outcome.textContent = "";
          var paragraph = document.createElement("p");
          if (payload.status === "already-available") {
            paragraph.textContent = "PDF já disponível: " + payload.fileName + " — " + payload.relativePath + ".";
          } else {
            paragraph.textContent = "PDF gerado: " + payload.fileName + " — " + payload.relativePath + ".";
          }
          outcome.appendChild(paragraph);

          // The manual send reveals next to the existing PDF output (the user decides when to
          // send; the configured lists/template are the only recipient/template source).
          if (sendRegion) { sendRegion.hidden = false; }
        });
      });

      wirePesoPdfSend(root);
    }

    /**
     * Wires the manual email send of the EXISTING Peso PDF (P2-T08 documents slice): posts ONLY
     * the canonical peso_id + the applicable configured list id (when the configuration does not
     * determine a single list). The backend resolves the template/list exclusively from the
     * Definições data, attaches the existing PDF and NEVER regenerates it. The outcome shows the
     * evidence (file, template, recipient count); typed refusals enter the page-owned errors
     * presentation; pending state prevents duplicate sends; no reload is performed.
     */
    function wirePesoPdfSend(root) {
      var sendButton = root.querySelector("[data-dmo-peso-pdf-send]");
      var sendOutcome = root.querySelector("[data-dmo-peso-pdf-send-outcome]");
      var sendRegion = root.querySelector("[data-dmo-peso-pdf-send-region]");
      if (!sendButton || !sendOutcome) { return; }

      sendButton.addEventListener("click", function () {
        var state = anchors(root);
        if (!state.pesoId || sendButton.disabled) { return; }

        var emailListId = null;
        if (sendRegion && sendRegion.getAttribute("data-dmo-peso-pdf-list-value")) {
          emailListId = sendRegion.getAttribute("data-dmo-peso-pdf-list-value");
        } else {
          var select = root.querySelector("[data-dmo-peso-pdf-list]");
          if (select) {
            emailListId = select.value || null;
            if (!emailListId) {
              renderErrors(root, { errors: ["Selecione a lista de destinatários aplicável."] });
              return;
            }
          }
        }

        var original = sendButton.textContent;
        var pending = sendButton.getAttribute("data-dmo-peso-pdf-send-pending") || original;
        sendButton.textContent = pending;
        sendButton.disabled = true;

        var payload = emailListId ? { emailListId: emailListId } : {};
        api(basePath + "/pesos/" + state.pesoId + "/peso-pdf/send", "POST", payload).then(function (response) {
          sendButton.textContent = original;
          sendButton.disabled = false;

          if (!response.ok) {
            renderMutationFailure(root, response);
            return;
          }

          var payload = response.body || {};
          sendOutcome.hidden = false;
          sendOutcome.removeAttribute("role");
          sendOutcome.textContent = "";
          var paragraph = document.createElement("p");
          var recipientCount = payload.recipients ? payload.recipients.length : 0;
          paragraph.textContent = "Email enviado para " + recipientCount + " destinatário(s) — " +
            "ficheiro " + payload.fileName + " — template " + payload.templateName + ".";
          sendOutcome.appendChild(paragraph);
        });
      });
    }

    function initialize() {
      document.querySelectorAll("[data-dmo-controlo-root]").forEach(function (root) {
        var surface = root.getAttribute("data-dmo-controlo-surface");
        if (surface === "create") {
          wireCreate(root);
          wirePesoPdf(root);
        }
        else if (surface === "definicoes") {
          wireRepairers(root);
          wireAssignments(root);
          wirePdfDirectory(root);
          wireEmailLists(root);
          wireEmailTemplates(root);
          wireGlassDensities(root);
        }
      });
    }

    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", initialize);
    } else {
      initialize();
    }

    return { api: api };
  })();
}