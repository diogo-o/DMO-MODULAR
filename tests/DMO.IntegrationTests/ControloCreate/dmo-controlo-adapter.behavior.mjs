// P2-T05 focused correction (D1 + D2) — BEHAVIORAL regression harness for the page-owned
// adapter `src/DMO.Web/wwwroot/js/dmo-controlo.js` (the REAL shipped file, loaded from disk).
//
// It runs the adapter against a minimal document/window/fetch stub and drives the actual click
// handlers, proving:
//   D1 — the submitted Peso view (editable actions absent) initializes safely (no TypeError) and
//        the bindings that DO exist (submit, associate) still work;
//   D2 — a typed 409 `stale-version` on any guarded mutation enters the conflict state with the
//        explicit recovery action (reload the authoritative state), exactly ONE request is issued
//        (no automatic retry, no auto-merge, no overwrite of the newer version), the recovery
//        control reloads, and NON-stale typed failures keep the existing renderErrors behavior;
//   P2-T08 — the Peso PDF action (Create owns the operational document work): one POST with NO
//        body on the canonical peso_id, pending state while in flight, the deterministic output
//        result shown (generated / already-available; never rewritten), refusals into the
//        page-owned errors presentation, no reload.
//
// Usage: node dmo-controlo-adapter.behavior.mjs <absolute-path-to-dmo-controlo.js>
// Exit code 0 = all scenarios passed; 1 = a scenario failed (details on stdout).

import assert from "node:assert/strict";
import fs from "node:fs";

const adapterPath = process.argv[2];
if (!adapterPath) {
  console.error("Usage: node dmo-controlo-adapter.behavior.mjs <path-to-dmo-controlo.js>");
  process.exit(2);
}

const adapterSource = fs.readFileSync(adapterPath, "utf8");

// ---------------------------------------------------------------------------------------------
// Minimal DOM stub (only the APIs the adapter uses; selectors are attribute selectors only).
// ---------------------------------------------------------------------------------------------

function matches(element, selector) {
  const tag = selector.match(/^[a-zA-Z][a-zA-Z0-9-]*$/);
  if (tag) {
    return element.tagName === selector;
  }
  const match = selector.match(/^\[([a-zA-Z0-9_-]+)(?:='([^']*)'|="([^"]*)")?\]$/);
  if (!match) {
    throw new Error(`Unsupported selector "${selector}" in harness (attribute/tag selectors only).`);
  }
  const name = match[1];
  const expected = match[2] !== undefined ? match[2] : match[3];
  if (!Object.prototype.hasOwnProperty.call(element.attributes, name)) {
    return false;
  }
  return expected === undefined ? true : element.attributes[name] === expected;
}

function toNodeList(array) {
  array.forEach = function (callback) {
    for (let index = 0; index < this.length; index++) {
      callback(this[index], index, this);
    }
  };
  return array;
}

class Element {
  constructor(tag, attributes = {}) {
    this.tagName = tag;
    this.attributes = {};
    this.children = [];
    this.listeners = {};
    this._textContent = null;
    this.value = "";
    this.hidden = false;
    this.disabled = false;
    this.options = [];
    this.selectedIndex = 0;
    for (const [name, value] of Object.entries(attributes)) {
      this.attributes[name] = String(value);
    }
  }

  // Standard DOM semantics: the setter replaces the children with a text value; the getter
  // concatenates the descendants' text, falling back to the direct text value.
  get textContent() {
    if (this.children.length > 0) {
      return this.children.map((child) => child.textContent).join("");
    }
    return this._textContent ?? "";
  }

  set textContent(value) {
    this._textContent = String(value);
    this.children = [];
  }

  getAttribute(name) {
    return Object.prototype.hasOwnProperty.call(this.attributes, name)
      ? this.attributes[name]
      : null;
  }

  setAttribute(name, value) {
    this.attributes[name] = String(value);
  }

  removeAttribute(name) {
    delete this.attributes[name];
  }

  addEventListener(event, handler) {
    (this.listeners[event] ||= []).push(handler);
  }

  click() {
    for (const handler of this.listeners["click"] || []) {
      handler.call(this);
    }
  }

  // Simulates the platform behavior: a disabled control never fires its click listeners.
  fireClick() {
    if (this.disabled) {
      return false;
    }
    this.click();
    return true;
  }

  appendChild(child) {
    child.parent = this;
    this.children.push(child);
    return child;
  }

  // Standard DOM semantics: matches this element AND its descendants.
  querySelectorAll(selector) {
    const found = [];
    const walk = (node) => {
      if (matches(node, selector)) {
        found.push(node);
      }
      for (const child of node.children) {
        walk(child);
      }
    };
    walk(this);
    return toNodeList(found);
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] ?? null;
  }
}

let lastWindow = null;

function createDocument() {
  const documentStub = {
    readyState: "complete",
    addEventListener() {},
    createElement: (tag) => new Element(tag),
    roots: [],
    querySelectorAll(selector) {
      const found = [];
      for (const root of this.roots) {
        for (const node of root.querySelectorAll(selector)) {
          found.push(node);
        }
      }
      return toNodeList(found);
    },
  };
  return documentStub;
}

function createFetch(routes) {
  const calls = [];
  const fetchStub = (path, options) => {
    calls.push({ path, method: options.method, body: options.body ? JSON.parse(options.body) : null });
    const route = (fetchStub.routes ?? []).find(
      (entry) => entry.method === options.method && path.startsWith(entry.pathPrefix));
    const outcome = route ? route.response : { status: 500, body: { reason: "unexpected-route" } };
    return Promise.resolve({
      ok: outcome.status >= 200 && outcome.status < 300,
      status: outcome.status,
      json: () => Promise.resolve(outcome.body),
    });
  };
  fetchStub.calls = calls;
  fetchStub.routes = routes;
  return fetchStub;
}

function createWindow() {
  lastWindow = {
    dmoControlo: undefined,
    reloadCalls: 0,
    location: {
      reload() {
        lastWindow.reloadCalls += 1;
      },
    },
    confirm: () => true,
  };
  return lastWindow;
}

function loadAdapter(windowStub, documentStub, fetchStub) {
  // The adapter's only free variables are window/document/fetch; everything else is self-contained.
  const factory = new Function("window", "document", "fetch", adapterSource);
  factory(windowStub, documentStub, fetchStub);
}

async function flush() {
  await Promise.resolve();
  await Promise.resolve();
  await new Promise((resolve) => setImmediate(resolve));
}

// ---------------------------------------------------------------------------------------------
// Scenario builders
// ---------------------------------------------------------------------------------------------

function createSurface(overrides = {}) {
  const root = new Element("section", { "data-dmo-controlo-root": "true", "data-dmo-controlo-surface": "create" });

  const state = new Element("div", { "data-dmo-peso-id": overrides.pesoId ?? "", "data-dmo-version": String(overrides.version ?? 1), "data-dmo-submitted": overrides.submitted ? "true" : "false" });
  root.appendChild(state);

  const stateRegion = new Element("div", { "data-dmo-controlo-state": "true" });
  stateRegion.hidden = true;
  root.appendChild(stateRegion);

  const temperature = new Element("input", { "data-dmo-field-temperature": "true" });
  temperature.value = "20";
  const marisa = new Element("input", { "data-dmo-field-marisa": "true" });
  const puncao = new Element("input", { "data-dmo-field-puncao": "true" });
  const sapEnd = new Element("input", { "data-dmo-field-sap-end": "true" });
  const sapWeight = new Element("input", { "data-dmo-field-sap-weight": "true" });
  for (const input of [temperature, marisa, puncao, sapEnd, sapWeight]) {
    root.appendChild(input);
  }

  const actions = new Element("div", { "data-dmo-controlo-region": "actions" });
  for (const key of overrides.draftActions ?? ["calculate", "save", "submit", "cancel"]) {
    const button = new Element("button", { "data-dmo-action": key });
    button.textContent = key;
    actions.appendChild(button);
  }
  root.appendChild(actions);

  const results = new Element("div", { "data-dmo-controlo-region": "results" });
  const table = new Element("table", { "data-dmo-results-table": "true" });
  const tbody = new Element("tbody", { "data-dmo-tbody": "true" });
  table.appendChild(tbody);
  const hint = new Element("p", { "data-dmo-results-hint": "true" });
  hint.hidden = true;
  const missing = new Element("div", { "data-dmo-calculation-missing": "true" });
  missing.hidden = true;
  const summary = new Element("dl", { "data-dmo-results-summary": "true" });
  for (const name of ["water", "capacity", "glass", "density"]) {
    summary.appendChild(new Element("dd", { [`data-dmo-summary-${name}`]: "true" }));
  }
  for (const child of [table, hint, missing, summary]) {
    results.appendChild(child);
  }
  root.appendChild(results);

  if (overrides.pendingAssociate) {
    const select = new Element("select", { "data-dmo-associate-candidate": "true" });
    const option = new Element("option", { "data-dmo-candidate-id": "candidate-cm-1" });
    option.textContent = "REF — 1000 (B1)";
    select.options = [option];
    root.appendChild(select);
    const associateButton = new Element("button", { "data-dmo-associate": "true" });
    associateButton.textContent = "Associar";
    root.appendChild(associateButton);
  }

  // The P2-T08 Peso PDF action surface (rendered by the real Create page for decided,
  // production-bound drafts).
  if (overrides.pesoPdfAction) {
    const pdfButton = new Element("button", { "data-dmo-peso-pdf": "true", "data-dmo-peso-pdf-pending": "A gerar…" });
    pdfButton.textContent = "Gerar PDF do Peso";
    root.appendChild(pdfButton);
    const pdfOutcome = new Element("div", { "data-dmo-peso-pdf-outcome": "true" });
    pdfOutcome.hidden = true;
    root.appendChild(pdfOutcome);

    // The manual send region (revealed once a PDF output exists).
    const sendRegion = new Element("div", {
      "data-dmo-peso-pdf-send-region": "true",
      "data-dmo-peso-pdf-list-value": overrides.pesoPdfSingleListId ?? "",
    });
    sendRegion.hidden = true;
    root.appendChild(sendRegion);

    if (overrides.pesoPdfMultiList) {
      const select = new Element("select", { "data-dmo-peso-pdf-list": "true" });
      const placeholder = new Element("option", { value: "" });
      placeholder.textContent = "— selecionar lista —";
      const listA = new Element("option", { value: "11111111-1111-1111-1111-111111111111" });
      listA.textContent = "A";
      const listB = new Element("option", { value: "22222222-2222-2222-2222-222222222222" });
      listB.textContent = "B";
      select.options = [placeholder, listA, listB];
      root.appendChild(select);
    }

    const sendButton = new Element("button", { "data-dmo-peso-pdf-send": "true", "data-dmo-peso-pdf-send-pending": "A enviar…" });
    sendButton.textContent = "Enviar PDF por email";
    sendRegion.appendChild(sendButton);

    const sendOutcome = new Element("div", { "data-dmo-peso-pdf-send-outcome": "true" });
    sendOutcome.hidden = true;
    sendRegion.appendChild(sendOutcome);
  }

  return root;
}

function definicoesAssignmentsSurface() {
  const root = new Element("section", { "data-dmo-controlo-root": "true", "data-dmo-controlo-surface": "definicoes" });
  const stateRegion = new Element("div", { "data-dmo-controlo-state": "true" });
  stateRegion.hidden = true;
  root.appendChild(stateRegion);
  const select = new Element("select", { "data-dmo-assignment-repairer": "B1", "data-dmo-assignment-version": "2" });
  root.appendChild(select);
  const setButton = new Element("button", { "data-dmo-assignment-set": "B1" });
  setButton.textContent = "Guardar";
  root.appendChild(setButton);
  return root;
}

function definicoesListsSurface() {
  const root = new Element("section", { "data-dmo-controlo-root": "true", "data-dmo-controlo-surface": "definicoes" });
  const stateRegion = new Element("div", { "data-dmo-controlo-state": "true" });
  stateRegion.hidden = true;
  root.appendChild(stateRegion);
  const editId = new Element("input", { "data-dmo-list-edit-id": "true" });
  editId.value = "list-1";
  const editVersion = new Element("input", { "data-dmo-list-edit-version": "true" });
  editVersion.value = "4";
  const name = new Element("input", { "data-dmo-list-new-name": "true" });
  const recipients = new Element("textarea", { "data-dmo-list-new-recipients": "true" });
  const updateButton = new Element("button", { "data-dmo-list-update": "true" });
  for (const child of [editId, editVersion, name, recipients, updateButton]) {
    root.appendChild(child);
  }
  return root;
}

function definicoesGlassDensitiesSurface() {
  const root = new Element("section", { "data-dmo-controlo-root": "true", "data-dmo-controlo-surface": "definicoes" });
  const stateRegion = new Element("div", { "data-dmo-controlo-state": "true" });
  stateRegion.hidden = true;
  root.appendChild(stateRegion);
  const input = new Element("input", { "data-dmo-glass-density-input": "NNPB", "data-dmo-glass-density-version": "2" });
  input.value = "2,41";
  root.appendChild(input);
  const saveButton = new Element("button", { "data-dmo-glass-density-save": "NNPB" });
  saveButton.textContent = "Guardar";
  root.appendChild(saveButton);
  return root;
}

const results = [];
function scenario(name, fn) {
  return Promise.resolve()
    .then(fn)
    .then(() => {
      results.push(`PASS ${name}`);
    })
    .catch((error) => {
      results.push(`FAIL ${name}: ${error && error.message ? error.message : String(error)}`);
    });
}

// ---------------------------------------------------------------------------------------------
// Scenarios
// ---------------------------------------------------------------------------------------------

// S0 — control: the normal draft surface still wires and works after the refactor (calculate +
//      create-save success paths, non-stale failures keep the existing behavior).
await scenario("S0 normal draft surface still works", async () => {
  const documentStub = createDocument();
  const root = createSurface({});
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/calculate",
      response: {
        status: 200,
        body: {
          cmId: null,
          pendingToolId: "tool-1",
          waterTemperature: 20,
          waterDensityGCm3: 0.9982,
          glassDensityGCm3: 2.5,
          rows: [{ rowPosition: 1, waterWeightG: 500, capacityCm3: 500.9016, glassWeightG: 1252.254 }],
        },
      },
    },
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos",
      response: { status: 201, body: { pesoId: "p-1", version: 1 } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub); // must not throw

  root.querySelector("[data-dmo-action='calculate']").click();
  await flush();

  const tbody = root.querySelector("[data-dmo-results-table]").querySelector("[data-dmo-tbody]");
  assert.equal(tbody.children.length, 1, "calculate preview must render the result row");
  assert.equal(root.querySelector("[data-dmo-results-hint]").hidden, true, "hint must hide after results");

  root.querySelector("[data-dmo-action='save']").click();
  await flush();

  const createCall = fetchStub.calls.find(
    (call) => call.method === "POST" && call.path === "/controlo/create/pesos");
  assert.ok(createCall, "create-save must POST /controlo/create/pesos");
  assert.equal(createCall.body.expectedVersion, undefined, "create carries no expectedVersion");
  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.hidden, false, "saved state must be shown");
  assert.match(region.textContent, /Controlo guardado \(versão 1\)/, "create-save success message with fresh version");
  assert.equal(region.getAttribute("data-dmo-conflict"), null, "no conflict marker on success");
});

// S1 — D1 regression: the submitted Peso view (editable actions ABSENT) initializes safely and the
//      existing bindings (disabled submit, pending associate) still work.
await scenario("S1 submitted view initializes safely and its bindings still work", async () => {
  const documentStub = createDocument();
  const root = createSurface({
    pesoId: "p-1",
    version: 2,
    submitted: true,
    draftActions: ["submit"], // the ONLY action of the submitted state
    pendingAssociate: true,
  });
  root.querySelector("[data-dmo-action='submit']").disabled = true; // server-rendered disabled
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/p-1/submit",
      response: { status: 409, body: { reason: "already-submitted", message: "already submitted." } },
    },
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/p-1/associate",
      response: { status: 409, body: { reason: "already-submitted", message: "already submitted." } },
    },
  ]);

  // D1 core: before the correction the unguarded calculate/save/cancel bindings threw a TypeError
  // here, killing every subsequent binding on this view.
  loadAdapter(windowStub, documentStub, fetchStub);

  // The submitted-state bindings that DO exist work: the disabled submit never fires (platform
  // behavior), and the pending associate control still posts and surfaces the typed refusal.
  const submitFired = root.querySelector("[data-dmo-action='submit']").fireClick();
  assert.equal(submitFired, false, "disabled submit must not fire in the submitted state");
  assert.equal(fetchStub.calls.length, 0, "disabled submit must issue no request");

  root.querySelector("[data-dmo-associate]").click();
  await flush();

  assert.equal(
    fetchStub.calls[0].path,
    "/controlo/create/pesos/p-1/associate",
    "the associate binding must still be wired on the submitted+pending view");
  assert.equal(fetchStub.calls[0].body.expectedVersion, 2, "associate sends the observed version");
  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.hidden, false, "typed refusal must be surfaced");
  assert.match(region.textContent, /already-submitted/, "non-stale typed refusal keeps the existing presentation");
  assert.equal(region.getAttribute("data-dmo-conflict"), null, "no conflict marker for a non-stale refusal");
});

// S2 — D2 regression: draft save → 409 stale-version → conflict state + recovery action; exactly
//      one request (no auto-retry), nothing overwritten, recovery reloads the authoritative state.
await scenario("S2 save stale-version enters conflict with recovery and no auto-retry", async () => {
  const documentStub = createDocument();
  const root = createSurface({ pesoId: "p-1", version: 3 });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "PUT",
      pathPrefix: "/controlo/create/pesos/p-1",
      response: { status: 409, body: { reason: "stale-version", message: "The Peso changed after it was observed." } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-action='save']").click();
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE request — no automatic retry");
  assert.equal(fetchStub.calls[0].body.expectedVersion, 3, "the observed version is sent");

  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.hidden, false, "conflict region must be visible");
  assert.equal(region.getAttribute("data-dmo-conflict"), "true", "conflict marker set");
  assert.match(region.textContent, /Conflito/, "clear conflict message");
  assert.match(region.textContent, /The Peso changed after it was observed/, "typed message surfaced");

  const recover = region.querySelector("[data-dmo-conflict-reload]");
  assert.ok(recover, "recovery action must exist");
  assert.match(recover.textContent, /Recarregar estado atual/, "recovery action labelled");

  const versionNode = root.querySelector("[data-dmo-peso-id]");
  assert.equal(
    versionNode.getAttribute("data-dmo-version"),
    "3",
    "the newer server version is never overwritten locally (no setAnchor on refusal)");

  recover.click();
  assert.equal(windowStub.reloadCalls, 1, "recovery reloads the authoritative current state");
});

// S3 — D2 regression: submit → 409 stale-version → same conflict + recovery.
await scenario("S3 submit stale-version enters conflict with recovery and no auto-retry", async () => {
  const documentStub = createDocument();
  const root = createSurface({ pesoId: "p-1", version: 3 });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/p-1/submit",
      response: { status: 409, body: { reason: "stale-version", message: "stale during submit." } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-action='submit']").click();
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE request — no automatic retry");
  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.getAttribute("data-dmo-conflict"), "true", "conflict marker set");
  assert.match(region.textContent, /stale during submit/, "typed message surfaced");
  root.querySelector("[data-dmo-conflict-reload]").click();
  assert.equal(windowStub.reloadCalls, 1, "recovery reloads the authoritative current state");
});

// S4 — D2 regression: Definições guarded mutation (machine-assignment set) → same conflict +
//      recovery (the page-owned handling is shared by every guarded mutation).
await scenario("S4 settings guarded mutation stale-version enters conflict with recovery", async () => {
  const documentStub = createDocument();
  const root = definicoesAssignmentsSurface();
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "PUT",
      pathPrefix: "/controlo/create/definicoes/machine-assignments/B1",
      response: { status: 409, body: { reason: "stale-version", message: "assignment changed." } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-assignment-set]").click();
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE request — no automatic retry");
  assert.equal(fetchStub.calls[0].body.expectedVersion, 2, "the observed version is sent");
  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.getAttribute("data-dmo-conflict"), "true", "conflict marker set");
  assert.match(region.textContent, /assignment changed/, "typed message surfaced");
  root.querySelector("[data-dmo-conflict-reload]").click();
  assert.equal(windowStub.reloadCalls, 1, "recovery reloads the authoritative current state");
});

// S5 — D2 regression: email-list update (second settings guarded mutation) → same conflict.
await scenario("S5 email-list update stale-version enters conflict with recovery", async () => {
  const documentStub = createDocument();
  const root = definicoesListsSurface();
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "PUT",
      pathPrefix: "/controlo/create/definicoes/email-lists/list-1",
      response: { status: 409, body: { reason: "stale-version", message: "list changed." } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-list-update]").click();
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE request — no automatic retry");
  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.getAttribute("data-dmo-conflict"), "true", "conflict marker set");
  assert.match(region.textContent, /list changed/, "typed message surfaced");
  root.querySelector("[data-dmo-conflict-reload]").click();
  assert.equal(windowStub.reloadCalls, 1, "recovery reloads the authoritative current state");
});

// S6 — D2 regression: NON-stale typed failures keep the existing behavior (validation-failed 400
//      and already-submitted 409 → plain typed errors, no conflict state, no recovery control).
await scenario("S6 non-stale typed failures keep the existing presentation", async () => {
  const documentStub = createDocument();
  const root = createSurface({ pesoId: "p-1", version: 3 });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "PUT",
      pathPrefix: "/controlo/create/pesos/p-1",
      response: {
        status: 400,
        body: { reason: "validation-failed", errors: ["ROW_WEIGHT_INVALID"] },
      },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-action='save']").click();
  await flush();

  let region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.hidden, false, "typed validation failure must be surfaced");
  assert.match(region.textContent, /ROW_WEIGHT_INVALID/, "validation token listed");
  assert.equal(region.getAttribute("data-dmo-conflict"), null, "no conflict marker for validation-failed");
  assert.equal(region.querySelector("[data-dmo-conflict-reload]"), null, "no recovery control for validation-failed");

  fetchStub.calls.length = 0;
  // Re-point the stub: replace routes with the already-submitted refusal.
  fetchStub.routes = [
    {
      method: "PUT",
      pathPrefix: "/controlo/create/pesos/p-1",
      response: { status: 409, body: { reason: "already-submitted", message: "closed." } },
    },
  ];

  root.querySelector("[data-dmo-action='save']").click();
  await flush();

  region = root.querySelector("[data-dmo-controlo-state]");
  assert.match(region.textContent, /already-submitted/, "non-stale 409 keeps the existing presentation");
  assert.equal(region.getAttribute("data-dmo-conflict"), null, "no conflict marker for already-submitted");
  assert.equal(region.querySelector("[data-dmo-conflict-reload]"), null, "no recovery control for already-submitted");
  assert.equal(fetchStub.calls.length, 1, "exactly ONE request for the second mutation");
});

// S7 — D2 regression for the glass-density Definições PUT (routes 18/19, post-closure
// correction): a typed 409 stale-version enters the SAME conflict state with recovery, exactly
// ONE request is issued (no automatic retry), and the entered value is retained in the input.
await scenario("S7 glass-density save stale-version enters conflict with recovery", async () => {
  const documentStub = createDocument();
  const root = definicoesGlassDensitiesSurface();
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "PUT",
      pathPrefix: "/controlo/create/definicoes/glass-densities/NNPB",
      response: { status: 409, body: { reason: "stale-version", message: "density changed." } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-glass-density-save]").click();
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE request — no automatic retry");
  assert.equal(fetchStub.calls[0].method, "PUT", "glass-density save is a PUT");
  assert.equal(fetchStub.calls[0].body.densityGcm3, 2.41, "the comma decimal is parsed and sent");
  assert.equal(fetchStub.calls[0].body.expectedVersion, 2, "the observed version is sent");
  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.getAttribute("data-dmo-conflict"), "true", "conflict marker set");
  assert.match(region.textContent, /density changed/, "typed message surfaced");
  root.querySelector("[data-dmo-conflict-reload]").click();
  assert.equal(windowStub.reloadCalls, 1, "recovery reloads the authoritative current state");
});

// S8 — D2 regression for the glass-density PUT: NON-stale typed failures (validation-failed 400
// with DENSITY_NOT_POSITIVE/PROCESSO_UNKNOWN) keep the existing plain error presentation — no
// conflict marker, no recovery control.
await scenario("S8 glass-density validation failure keeps the plain error presentation", async () => {
  const documentStub = createDocument();
  const root = definicoesGlassDensitiesSurface();
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "PUT",
      pathPrefix: "/controlo/create/definicoes/glass-densities/NNPB",
      response: { status: 400, body: { reason: "validation-failed", errors: ["DENSITY_NOT_POSITIVE"] } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-glass-density-save]").click();
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE request — no automatic retry");
  const region = root.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.hidden, false, "typed validation failure must be surfaced");
  assert.match(region.textContent, /DENSITY_NOT_POSITIVE/, "validation token listed");
  assert.equal(region.getAttribute("data-dmo-conflict"), null, "no conflict marker for validation-failed");
  assert.equal(region.querySelector("[data-dmo-conflict-reload]"), null, "no recovery control for validation-failed");
});

// S9 — P2-T08 documents slice: the Peso PDF action on the Create surface posts ONE request with
// NO body (only the canonical peso_id in the path), prevents duplicate invocation while pending
// (accessible name preserved), shows the deterministic output result (generated / already-
// available; never rewritten), enters reduced failures into the page-owned errors presentation,
// and never reloads the page.
await scenario("S9 Peso PDF generation posts once, shows the output and never reloads", async () => {
  const documentStub = createDocument();
  const root = createSurface({ pesoId: "11111111-1111-1111-1111-111111111111", version: 2, submitted: true, pesoPdfAction: true, draftActions: ["submit"] });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/11111111-1111-1111-1111-111111111111/peso-pdf",
      response: {
        status: 200,
        body: {
          status: "generated",
          pesoId: "11111111-1111-1111-1111-111111111111",
          version: 2,
          fileName: "Peso_ref-X_B1.pdf",
          relativePath: "ref-X/prod-1/Peso_ref-X_B1.pdf",
          bytes: 42,
        },
      },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  const button = root.querySelector("[data-dmo-peso-pdf]");
  const outcome = root.querySelector("[data-dmo-peso-pdf-outcome]");

  button.click();
  assert.equal(button.disabled, true, "pending state blocks duplicate invocation while in flight");
  assert.equal(button.textContent, "A gerar…", "accessible name preserved while pending");
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE peso-pdf request");
  assert.equal(fetchStub.calls[0].method, "POST", "the peso-pdf request is a POST");
  assert.equal(fetchStub.calls[0].path, "/controlo/create/pesos/11111111-1111-1111-1111-111111111111/peso-pdf",
    "the request targets the canonical peso_id documents route");
  assert.equal(fetchStub.calls[0].body, null, "the peso-pdf request carries NO body");
  assert.equal(button.disabled, false, "button restored after the response");
  assert.equal(button.textContent, "Gerar PDF do Peso", "button text restored after the response");
  assert.equal(outcome.hidden, false, "the outcome region is revealed");
  assert.match(outcome.textContent, /PDF gerado: Peso_ref-X_B1.pdf/, "the generated output result is shown");
  assert.equal(windowStub.reloadCalls, 0, "generation does NOT reload the page");
});

// S10 — P2-T08: an existing deterministic target is reported as already-available (the backend
// never overwrites) and a typed refusal enters the pages-owned error presentation.
await scenario("S10 Peso PDF already-available and typed refusal handling", async () => {
  const documentStub = createDocument();
  const root = createSurface({ pesoId: "22222222-2222-2222-2222-222222222222", version: 2, submitted: true, pesoPdfAction: true, draftActions: ["submit"] });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/22222222-2222-2222-2222-222222222222/peso-pdf",
      response: {
        status: 200,
        body: { status: "already-available", pesoId: "22222222-2222-2222-2222-222222222222", version: 2, fileName: "Peso_ref-X_B1.pdf", relativePath: "ref-X/prod-1/Peso_ref-X_B1.pdf", bytes: 42 },
      },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  root.querySelector("[data-dmo-peso-pdf]").click();
  await flush();

  const outcome = root.querySelector("[data-dmo-peso-pdf-outcome]");
  assert.match(outcome.textContent, /PDF já disponível: Peso_ref-X_B1.pdf/, "an existing target is reported as already-available");
  assert.equal(windowStub.reloadCalls, 0, "no reload on already-available");

  // A second surface with a typed refusal into the errors presentation.
  const documentStub2 = createDocument();
  const root2 = createSurface({ pesoId: "33333333-3333-3333-3333-333333333333", version: 2, submitted: true, pesoPdfAction: true, draftActions: ["submit"] });
  documentStub2.roots.push(root2);
  const windowStub2 = createWindow();
  const fetchStub2 = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/33333333-3333-3333-3333-333333333333/peso-pdf",
      response: { status: 409, body: { reason: "not-decided", message: "Este Peso ainda não foi decidido." } },
    },
  ]);

  loadAdapter(windowStub2, documentStub2, fetchStub2);

  root2.querySelector("[data-dmo-peso-pdf]").click();
  await flush();

  const region = root2.querySelector("[data-dmo-controlo-state]");
  assert.equal(region.hidden, false, "a typed refusal is surfaced in the errors presentation");
  assert.match(region.textContent, /not-decided/, "the typed refusal reason is shown");
  assert.equal(region.getAttribute("data-dmo-conflict"), null, "a not-decided refusal is not a conflict");
  assert.equal(root2.querySelector("[data-dmo-peso-pdf]").disabled, false, "the button is restored after the refusal");
  assert.equal(windowStub2.reloadCalls, 0, "no reload on refusal");
});

// S11 — P2-T08 manual email send (single configured list applies automatically): the send
// reveals next to the PDF output, posts the canonical peso_id + the applicable configured list
// id, shows the evidence (recipient count, file, template), restores the button and never
// reloads.
await scenario("S11 Peso PDF manual send posts once, shows the evidence and never reloads", async () => {
  const documentStub = createDocument();
  const root = createSurface({
    pesoId: "44444444-4444-4444-4444-444444444444",
    version: 2,
    submitted: true,
    pesoPdfAction: true,
    pesoPdfSingleListId: "33333333-3333-3333-3333-333333333333",
    draftActions: ["submit"],
  });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/44444444-4444-4444-4444-444444444444/peso-pdf/send",
      response: {
        status: 200,
        body: {
          status: "sent",
          pesoId: "44444444-4444-4444-4444-444444444444",
          version: 2,
          fileName: "Peso_ref-X_B1.pdf",
          templateName: "Peso operação",
          recipients: ["a@example.com", "b@example.com"],
          sentAt: "2026-09-24T10:00:00Z",
        },
      },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  const sendRegion = root.querySelector("[data-dmo-peso-pdf-send-region]");
  const sendButton = root.querySelector("[data-dmo-peso-pdf-send]");
  const sendOutcome = root.querySelector("[data-dmo-peso-pdf-send-outcome]");

  sendButton.click();
  assert.equal(sendButton.disabled, true, "pending state blocks a duplicate send while in flight");
  assert.equal(sendButton.textContent, "A enviar…", "accessible name preserved while pending");
  await flush();

  assert.equal(fetchStub.calls.length, 1, "exactly ONE send request");
  assert.equal(fetchStub.calls[0].method, "POST", "the send request is a POST");
  assert.equal(fetchStub.calls[0].path,
    "/controlo/create/pesos/44444444-4444-4444-4444-444444444444/peso-pdf/send",
    "the request targets the manual-send route of the canonical peso_id");
  assert.equal(fetchStub.calls[0].body.emailListId, "33333333-3333-3333-3333-333333333333",
    "the applicable configured list id travels (no recipient address ever leaves the page)");
  assert.equal(sendButton.disabled, false, "button restored after the response");
  assert.equal(sendOutcome.hidden, false, "the send outcome region is revealed");
  assert.match(sendOutcome.textContent,
    /Email enviado para 2 destinatário\(s\) — ficheiro Peso_ref-X_B1\.pdf — template Peso operação\./,
    "the evidence (recipient count, file, template) is shown");
  assert.equal(windowStub.reloadCalls, 0, "a successful send does NOT reload the page");
});

// S12 — several configured lists: the operator selects the applicable one; without a selection
// no request is sent (the adapter never guesses); with the selection the send proceeds.
await scenario("S12 Peso PDF send requires the operator list selection when several lists exist", async () => {
  const documentStub = createDocument();
  const root = createSurface({
    pesoId: "55555555-5555-5555-5555-555555555555",
    version: 2,
    submitted: true,
    pesoPdfAction: true,
    pesoPdfMultiList: true,
    draftActions: ["submit"],
  });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/55555555-5555-5555-5555-555555555555/peso-pdf/send",
      response: {
        status: 200,
        body: {
          status: "sent",
          pesoId: "55555555-5555-5555-5555-555555555555",
          version: 2,
          fileName: "Peso_ref-X_B1.pdf",
          templateName: "Peso operação",
          recipients: ["a@example.com"],
          sentAt: "2026-09-24T10:05:00Z",
        },
      },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  const sendButton = root.querySelector("[data-dmo-peso-pdf-send]");
  const select = root.querySelector("[data-dmo-peso-pdf-list]");
  const stateRegion = root.querySelector("[data-dmo-controlo-state]");

  // No selection yet: the adapter blocks locally with the state information — no request.
  sendButton.click();
  await flush();
  assert.equal(fetchStub.calls.length, 0, "no send without a list selection");
  assert.equal(stateRegion.hidden, false, "the local state informs the missing selection");
  assert.match(stateRegion.textContent, /Selecione a lista/, "the local notice asks for the selection");

  // With the explicit selection of the applicable configured list: exactly ONE request.
  select.value = "22222222-2222-2222-2222-222222222222";
  sendButton.click();
  await flush();
  assert.equal(fetchStub.calls.length, 1, "exactly ONE send after the selection");
  assert.equal(fetchStub.calls[0].body.emailListId, "22222222-2222-2222-2222-222222222222",
    "the selected configured list id travels");
  assert.equal(windowStub.reloadCalls, 0, "no reload after the send");
});

// S13 — a typed backend refusal (email-template-not-configured) enters the page-owned errors
// presentation and restores the button.
await scenario("S13 Peso PDF send typed refusal enters the errors presentation", async () => {
  const documentStub = createDocument();
  const root = createSurface({
    pesoId: "66666666-6666-6666-6666-666666666666",
    version: 2,
    submitted: true,
    pesoPdfAction: true,
    pesoPdfSingleListId: "33333333-3333-3333-3333-333333333333",
    draftActions: ["submit"],
  });
  documentStub.roots.push(root);
  const windowStub = createWindow();
  const fetchStub = createFetch([
    {
      method: "POST",
      pathPrefix: "/controlo/create/pesos/66666666-6666-6666-6666-666666666666/peso-pdf/send",
      response: { status: 409, body: { reason: "email-template-not-configured", message: "Sem template aplicável." } },
    },
  ]);

  loadAdapter(windowStub, documentStub, fetchStub);

  const sendButton = root.querySelector("[data-dmo-peso-pdf-send]");
  const stateRegion = root.querySelector("[data-dmo-controlo-state]");

  sendButton.click();
  await flush();
  assert.equal(fetchStub.calls.length, 1, "exactly ONE send request on the refusal");
  assert.equal(stateRegion.hidden, false, "the typed refusal is surfaced");
  assert.match(stateRegion.textContent, /email-template-not-configured/, "the typed reason is shown");
  assert.equal(stateRegion.getAttribute("data-dmo-conflict"), null, "a config refusal is not a concurrency conflict");
  assert.equal(sendButton.disabled, false, "the button is restored after the refusal");
  assert.equal(windowStub.reloadCalls, 0, "no reload on refusal");
});

// ---------------------------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------------------------

const failures = results.filter((line) => line.startsWith("FAIL"));
for (const line of results) {
  console.log(line);
}
if (failures.length > 0) {
  console.error(`${failures.length} scenario(s) failed.`);
  process.exit(1);
}
console.log(`ALL ${results.length} BEHAVIORAL SCENARIOS PASSED (${adapterPath})`);
process.exit(0);