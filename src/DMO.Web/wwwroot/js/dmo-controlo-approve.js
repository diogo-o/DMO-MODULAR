/* P2-T06 Controlo Approve page-owned adapter (contract §13.2, §19, §21).
 *
 * Two surfaces:
 *   index     — Aprovar: pending list + exact-record review + decision region;
 *   historico — Histórico de Pesos: history list + detail + decision region.
 *
 * The adapter performs NO domain decision: backend validation is authoritative and every
 * persisted-state refusal surfaces its typed reason. Open/select arbitration is delegated to the
 * shared DenseDataTable adapter (single click selects, double click opens); the page adapter only
 * resolves the consumer route map. A typed `stale-version` refusal enters the accepted conflict
 * state with an explicit reload recovery (the D2 pattern) — no automatic retry, no auto-merge,
 * no silent overwrite, and the observed version is refreshed only on success.
 *
 * No width listener exists anywhere (AC-L1): the page renders its fixed-desktop composition and
 * scrolling is handled by the layout, not by script. It reuses the frozen window.dmoFocus helper
 * only when genuinely required and never declares a second focus helper.
 */
if (!window.dmoControloApprove) {
  window.dmoControloApprove = (function () {
    "use strict";

    var basePath = "/controlo/approve";

    function api(path, method, body) {
      var options = { method: method, headers: {} };
      if (body !== undefined && body !== null) {
        options.headers["Content-Type"] = "application/json";
        options.body = JSON.stringify(body);
      }
      return fetch(path, options).then(function (response) {
        return response.json().catch(function () { return {}; }).then(function (payload) {
          return { ok: response.ok, status: response.status, body: payload };
        });
      });
    }

    /**
     * Binds an event listener ONLY when the target element exists: absence of an element is a
     * VALID state (e.g. no review sheet opened, no decision actions rendered), so the adapter
     * must initialize normally and never throw on a missing control (D1 pattern).
     */
    function on(root, selector, event, handler) {
      var element = root.querySelector(selector);
      if (element) { element.addEventListener(event, handler); }
    }

    /**
     * The page-owned failure presentation of a decision response (D2 pattern): a typed
     * `stale-version` refusal enters the accepted conflict state with an explicit recovery
     * action (reload the authoritative current server state) — never an automatic retry, never a
     * silent merge, never an overwrite of the newer server version. Every other typed failure
     * keeps the errors presentation exactly.
     */
    function renderMutationFailure(root, response) {
      var payload = response && response.body ? response.body : null;
      if (payload && payload.reason === "stale-version") {
        renderConflict(root, payload.message);
      } else {
        renderErrors(root, payload);
      }
    }

    function renderErrors(root, payload) {
      var region = root.querySelector("[data-dmo-approve-state]");
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

    /**
     * The conflict presentation (freeze §4 `conflict`): a clear message plus supplied recovery
     * choices — one explicit reload action that fetches the authoritative current server state.
     */
    function renderConflict(root, message) {
      var region = root.querySelector("[data-dmo-approve-state]");
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

    function observedState(root) {
      var state = root.querySelector("[data-dmo-peso-id]");
      return {
        pesoId: state ? state.getAttribute("data-dmo-peso-id") || null : null,
        version: state ? parseInt(state.getAttribute("data-dmo-version") || "1", 10) : 1
      };
    }

    function reasonOf(root) {
      var input = root.querySelector("[data-dmo-reason-input]");
      return input ? input.value.trim() : "";
    }

    /**
     * Wires the table open arbitration: the shared DenseDataTable adapter raises `dmo:open-requested`
     * (single click selects, double click opens); this adapter resolves the page-owned route map
     * and navigates to the EXACT record review/detail. No per-row action grid exists (AC-H3).
     */
    function wireOpenRoutes(root) {
      root.querySelectorAll("[data-dmo-dense-table]").forEach(function (table) {
        table.addEventListener("dmo:open-requested", function (event) {
          var key = event.detail && event.detail.rowKey;
          if (!key) { return; }
          var route = root.querySelector("[data-dmo-open-route='" + key + "']");
          var href = route && route.getAttribute("data-dmo-open-href");
          if (href) { window.location.href = href; }
        });
      });
    }

    /**
     * Wires the decision actions (Aprovar / Rejeitar / Reabrir). All decisions are HUMAN actions:
     * the adapter sends only identity + version (+ the entered reason for reject/reopen) and
     * never derives a decision from results or warnings. Reject/reopen open the mandatory reason
     * input; a stale version enters the conflict presentation; success reloads the authoritative
     * state (the observed version is refreshed by the reload only).
     */
    function wireDecisions(root) {
      var reasonOpen = false;

      function requireReason(root) {
        var region = root.querySelector("[data-dmo-reason-region]");
        if (!region) { return true; }
        region.hidden = false;
        if (reasonOpen) { return true; }
        reasonOpen = true;
        var input = root.querySelector("[data-dmo-reason-input]");
        if (input) { input.focus(); }
        return false;
      }

      function decide(root, action, path, payload) {
        var state = observedState(root);
        if (!state.pesoId) { return; }
        var release = withPending(root, action);
        api(basePath + "/pesos/" + state.pesoId + path, "POST", payload).then(function (response) {
          release();
          if (response.ok) { window.location.reload(); }
          else { renderMutationFailure(root, response); }
        });
      }

      on(root, "[data-dmo-action='approve']", "click", function () {
        var state = observedState(root);
        if (!state.pesoId) { return; }
        // Explicit human confirmation; duplicate invocation prevented by the pending guard.
        if (!window.confirm("Aprovar este Peso? A decisão é humana e registada com o seu utilizador.")) { return; }
        decide(root, "approve", "/approve", { expectedVersion: state.version });
      });

      on(root, "[data-dmo-action='reject']", "click", function () {
        var state = observedState(root);
        if (!state.pesoId) { return; }
        // The first activation opens the mandatory reason input; the decision only proceeds with
        // a non-blank reason (the backend is authoritative: REJECT_REASON_REQUIRED).
        if (reasonOf(root) === "") {
          if (!requireReason(root)) { return; }
          renderErrors(root, { errors: ["Indique o motivo da rejeição (obrigatório)."] });
          return;
        }
        if (!window.confirm("Rejeitar este Peso? A decisão é humana e registada com o seu utilizador.")) { return; }
        decide(root, "reject", "/reject", { expectedVersion: state.version, reason: reasonOf(root) });
      });

      on(root, "[data-dmo-action='reopen']", "click", function () {
        var state = observedState(root);
        if (!state.pesoId) { return; }
        if (reasonOf(root) === "") {
          if (!requireReason(root)) { return; }
          renderErrors(root, { errors: ["Indique o motivo da reabertura (obrigatório)."] });
          return;
        }
        if (!window.confirm("Reabrir este Peso? Volta a pendente para edição em Controlo_Create; o histórico de decisões é preservado.")) { return; }
        decide(root, "reopen", "/reopen", { expectedVersion: state.version, reason: reasonOf(root) });
      });
    }

    function initialize() {
      document.querySelectorAll("[data-dmo-approve-root]").forEach(function (root) {
        wireOpenRoutes(root);
        wireDecisions(root);
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