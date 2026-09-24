// P2-T06 K5 (AC-K4, D2 preservation) — BEHAVIORAL proof of the page-owned adapter
// (src/DMO.Web/wwwroot/js/dmo-controlo-approve.js, the REAL shipped file) executed with a real
// JavaScript engine (node) against a minimal document/window/fetch stub:
//
//   - a typed 409 stale-version on every guarded decision (approve / reject / reopen) enters the
//     accepted conflict state with the explicit recovery action ("Recarregar estado atual"),
//     issues EXACTLY ONE request (no automatic retry, no auto-merge, no silent overwrite of the
//     newer server version) and the recovery control reloads the authoritative state;
//   - NON-stale typed failures (validation-failed) keep the existing errors presentation;
//   - the adapter initializes safely when no review sheet/actions are rendered (D1 pattern);
//   - the reason input is opened by Rejeitar/Reabrir and no decision is sent without a
//     non-blank reason (backend authoritative: REJECT_REASON_REQUIRED/REOPEN_REASON_REQUIRED);
//   - approve is an explicit confirmed action (one request only after confirmation);
//   - open arbitration is resolved through the page-owned route map (no per-row action buttons);
//   - P2-T08 Peso PDF: one request on the canonical peso_id with NO body, pending state while in
//     flight (duplicate invocation prevented, accessible name preserved), the deterministic output
//     result shown (generated / already-available; never rewritten) and typed refusals into the
//     page-owned errors presentation — generation never reloads the page.
//
// Usage: node dmo-controlo-approve-adapter.behavior.mjs <path-to-dmo-controlo-approve.js>
'use strict';

import fs from 'node:fs';

const adapterPath = process.argv[2];
if (!adapterPath || !fs.existsSync(adapterPath)) {
  console.error('usage: node dmo-controlo-approve-adapter.behavior.mjs <adapter.js>');
  process.exit(2);
}

let failureCount = 0;

function fail(step, detail) {
  failureCount += 1;
  console.error(`FAIL [${step}]: ${detail}`);
}

function assert(step, condition, detail) {
  if (!condition) { fail(step, detail); }
}

// ------------------------------------------------------------------ minimal DOM stub
function makeRoot() {
  function el(name) {
    const node = {
      name,
      attrs: {},
      children: [],
      parent: null,
      listeners: {},
      hidden: false,
      textContent: '',
      _value: '',
      _disabled: false,
      appendChild(child) { child.parent = this; this.children.push(child); return child; },
      addEventListener(event, handler) {
        (this.listeners[event] = this.listeners[event] || []).push(handler);
      },
      dispatchEvent(event) {
        const handlers = this.listeners[event.type] || [];
        handlers.forEach((handler) => handler.call(this, event));
        return true;
      },
      querySelector(selector) { return query(this, selector); },
      querySelectorAll(selector) { return queryAll(this, selector); },
      setAttribute(name, value) { this.attrs[name] = String(value); },
      getAttribute(name) { return Object.prototype.hasOwnProperty.call(this.attrs, name) ? this.attrs[name] : null; },
      hasAttribute(name) { return Object.prototype.hasOwnProperty.call(this.attrs, name); },
      removeAttribute(name) { delete this.attrs[name]; },
      focus() {},
    };

    Object.defineProperty(node, 'value', {
      get() { return this._value; },
      set(value) { this._value = value; },
    });
    Object.defineProperty(node, 'disabled', {
      get() { return this._disabled; },
      set(value) { this._disabled = value; },
    });

    return node;
  }

  function matches(node, selector) {
    if (!node || node.name === '#root') { return false; }

    // Supports the two selector forms the adapter uses: [attr] and [attr='value'].
    const parsed = /^\[([a-z0-9-]+)(?:=['"]([^'"]*)['"])?\]$/i.exec(selector);
    if (!parsed) { return false; }

    const attribute = parsed[1];
    const expected = parsed[2];
    if (!Object.prototype.hasOwnProperty.call(node.attrs, attribute)) { return false; }

    return expected === undefined || node.attrs[attribute] === expected;
  }

  function queryAll(node, selector) {
    const found = [];
    const walk = (current) => {
      current.children.forEach((child) => {
        if (matches(child, selector)) { found.push(child); }
        walk(child);
      });
    };
    walk(node);
    return found;
  }

  function query(node, selector) {
    return queryAll(node, selector)[0] || null;
  }

  const root = el('#root');
  root._el = el;

  // The surface under test: index (Aprovar) with an opened review sheet + decision bar.
  const section = el('section');
  section.attrs['data-dmo-approve-root'] = 'true';
  root.appendChild(section);

  const state = el('div');
  state.attrs['data-dmo-approve-state'] = 'true';
  state.hidden = true;
  section.appendChild(state);

  const adapterState = el('div');
  adapterState.attrs['data-dmo-peso-id'] = '11111111-1111-1111-1111-111111111111';
  adapterState.attrs['data-dmo-version'] = '2';
  section.appendChild(adapterState);

  const reasonRegion = el('div');
  reasonRegion.attrs['data-dmo-reason-region'] = 'true';
  reasonRegion.hidden = true;
  section.appendChild(reasonRegion);

  const reasonInput = el('input');
  reasonInput.attrs['data-dmo-reason-input'] = 'true';
  reasonRegion.appendChild(reasonInput);

  [
    ['approve', 'primary'],
    ['reject', 'danger'],
    ['reopen', 'secondary'],
  ].forEach(([key, group]) => {
    const button = el('button');
    button.attrs['data-dmo-action'] = key;
    button.attrs['data-dmo-action-key'] = key;
    button.attrs['data-dmo-action-group'] = group;
    button.textContent = key;
    section.appendChild(button);
  });

  // The P2-T08 Peso PDF action surface (rendered on decided records by the real page).
  const pdfButton = el('button');
  pdfButton.attrs['data-dmo-peso-pdf'] = 'true';
  pdfButton.attrs['data-dmo-peso-pdf-pending'] = 'A gerar…';
  pdfButton.textContent = 'Gerar PDF do Peso';
  section.appendChild(pdfButton);

  const pdfOutcome = el('div');
  pdfOutcome.attrs['data-dmo-peso-pdf-outcome'] = 'true';
  pdfOutcome.hidden = true;
  section.appendChild(pdfOutcome);

  // A decision table + route map for the open-arbitration proof (inside the surface root, as
  // rendered by the real pages).
  const table = el('div');
  table.attrs['data-dmo-dense-table'] = 'true';
  table.attrs['data-dmo-open-enabled'] = 'true';
  section.appendChild(table);

  const routeMap = el('div');
  routeMap.attrs['data-dmo-open-route-map'] = 'true';
  section.appendChild(routeMap);
  const route = el('span');
  route.attrs['data-dmo-open-route'] = '22222222-2222-2222-2222-222222222222';
  route.attrs['data-dmo-open-href'] = '/controlo/approve?pesoId=22222222-2222-2222-2222-222222222222';
  routeMap.appendChild(route);

  return root;
}

function click(node) {
  node.dispatchEvent({ type: 'click' });
}

/** Flushes the promise microtasks the adapter resolves through (fetch .then chains). */
async function flush() {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

function textOf(node) {
  const collect = (current) => current.textContent + current.children.map(collect).join('');
  return collect(node);
}

// ------------------------------------------------------------------ scenario driver
function loadAdapter(root, fetchImpl, confirmImpl) {
  const documentStub = {
    readyState: 'complete',
    querySelectorAll(selector) { return root.querySelectorAll(selector); },
    createElement(name) { return root._el(name); },
    addEventListener() {},
  };

  const locationValue = { href: '', reloads: 0 };
  locationValue.reload = () => { locationValue.reloads += 1; };

  const windowStub = {
    location: locationValue,
    document: documentStub,
    fetch: fetchImpl,
    confirm: confirmImpl || (() => true),
    CustomEvent,
    addEventListener() {},
  };

  const source = fs.readFileSync(adapterPath, 'utf8');
  // eslint-disable-next-line no-new-func
  new Function('window', 'document', 'fetch', 'location', 'confirm', 'CustomEvent', source)
    .call(windowStub, windowStub, documentStub, fetchImpl, locationValue, windowStub.confirm, CustomEvent);

  return windowStub;
}

// ------------------------------------------------------------------ main
(async () => {
  // ------------------------------------------------------------------ D1: absence is a valid state
  {
    const root = makeRoot();
    loadAdapter(
      root,
      async () => { fail('D1', 'the adapter must not fetch on an empty surface'); return { ok: false, status: 500, json: async () => ({}) }; },
      null,
    );
    assert('D1', true, 'adapter initialized with no review sheet/actions present');
  }

  // ------------------------------------------------------------------ approve: one request, version carried, success reloads
  {
    const root = makeRoot();
    const requests = [];
    const win = loadAdapter(
      root,
      async (url, options) => {
        requests.push({ url: String(url), body: options && options.body });
        return { ok: true, status: 200, json: async () => ({ decision: 'aprovado' }) };
      },
      () => true,
    );

    click(root.querySelectorAll('[data-dmo-action="approve"]')[0]);
    await flush();

    assert('approve-request', requests.length === 1 && requests[0].url.endsWith('/approve'),
      `exactly one approve request, got ${requests.length}`);
    assert('approve-version', requests.length === 1 && JSON.parse(requests[0].body).expectedVersion === 2,
      'approve carries the observed version');
    assert('approve-reload', win.location.reloads === 1, 'success reloads the authoritative state');
  }

  // ------------------------------------------------------------------ conflict presentation (K5)
  {
    const root = makeRoot();
    const requests = [];
    let response = { ok: false, status: 409, json: async () => ({ reason: 'stale-version', message: 'versao antiga' }) };
    const win = loadAdapter(
      root,
      async (url, options) => {
        requests.push({ url: String(url), body: options && options.body });
        return response;
      },
      () => true,
    );

    const reject = root.querySelectorAll('[data-dmo-action="reject"]')[0];
    const reasonInput = root.querySelectorAll('[data-dmo-reason-input]')[0];
    const stateNode = root.querySelectorAll('[data-dmo-approve-state]')[0];

    reasonInput.value = 'Leitura fora do esperado.';
    requests.length = 0;
    click(reject);
    await flush();

    assert('conflict-requests', requests.length === 1, `exactly ONE request on stale-version, got ${requests.length}`);
    assert('conflict-marker', stateNode.getAttribute('data-dmo-conflict') === 'true', 'the conflict marker is rendered');
    const stateText = textOf(stateNode);
    assert('conflict-heading', stateText.indexOf('Conflito') !== -1, 'the conflict heading is present');
    assert('conflict-reload', stateText.indexOf('Recarregar estado atual') !== -1, 'the explicit reload recovery is present');

    const reloadButton = root.querySelectorAll('[data-dmo-conflict-reload]')[0];
    const before = win.location.reloads;
    click(reloadButton);
    await flush();
    assert('reload', win.location.reloads === before + 1, 'the recovery control triggers exactly one reload');
    assert('no-retry', requests.length === 1, 'no automatic retry follows the conflict');

    // A NON-stale typed failure keeps the errors presentation (no conflict marker).
    response = { ok: false, status: 400, json: async () => ({ reason: 'validation-failed', errors: ['REJECT_REASON_REQUIRED'] }) };
    const freshRoot = makeRoot();
    const freshRequests = [];
    const freshState = freshRoot.querySelectorAll('[data-dmo-approve-state]')[0];
    const freshInput = freshRoot.querySelectorAll('[data-dmo-reason-input]')[0];
    const freshReject = freshRoot.querySelectorAll('[data-dmo-action="reject"]')[0];
    loadAdapter(freshRoot, async (url) => {
      freshRequests.push({ url: String(url) });
      return response;
    }, () => true);
    freshInput.value = 'motivo';
    click(freshReject);
    await flush();
    assert('errors-not-conflict', freshState.getAttribute('data-dmo-conflict') === null, 'non-stale failures keep the errors presentation');
    assert('errors-shown', textOf(freshState).indexOf('REJECT_REASON_REQUIRED') !== -1, 'the error codes are rendered');
  }

  // ------------------------------------------------------------------ reject: reason required, no request without a non-blank reason
  {
    const root = makeRoot();
    const requests = [];
    const win = loadAdapter(
      root,
      async (url, options) => {
        requests.push({ url: String(url), body: options && options.body });
        return { ok: true, status: 200, json: async () => ({ decision: 'nao_aprovado' }) };
      },
      () => true,
    );

    const reject = root.querySelectorAll('[data-dmo-action="reject"]')[0];
    const reasonInput = root.querySelectorAll('[data-dmo-reason-input]')[0];
    const reasonRegion = root.querySelectorAll('[data-dmo-reason-region]')[0];

    click(reject); // opens the reason input, sends nothing
    await flush();
    assert('reject-first-click', requests.length === 0 && reasonRegion.hidden === false,
      'the first reject click only opens the reason input');

    click(reject); // still empty → local refusal, no request
    await flush();
    assert('reject-empty', requests.length === 0, 'no request is sent with an empty reason');

    reasonInput.value = '  Leitura fora do esperado.  ';
    click(reject);
    await flush();
    assert('reject-sent', requests.length === 1 && requests[0].url.endsWith('/reject'),
      'one reject request with the reason');
    assert('reject-body', requests.length === 1 && JSON.parse(requests[0].body).reason === 'Leitura fora do esperado.',
      'the trimmed reason travels in the body');
    assert('reject-reload', win.location.reloads === 1, 'success reloads the authoritative state');
  }

  // ------------------------------------------------------------------ reopen: same guarded flow with reason
  {
    const root = makeRoot();
    const requests = [];
    const win = loadAdapter(
      root,
      async (url, options) => {
        requests.push({ url: String(url), body: options && options.body });
        return { ok: true, status: 200, json: async () => ({ decision: 'reaberto' }) };
      },
      () => true,
    );

    const reopen = root.querySelectorAll('[data-dmo-action="reopen"]')[0];
    const reasonInput = root.querySelectorAll('[data-dmo-reason-input]')[0];

    reasonInput.value = 'Correcao da temperatura.';
    click(reopen);
    await flush();
    assert('reopen-sent', requests.length === 1 && requests[0].url.endsWith('/reopen'),
      'one reopen request with the reason');
    assert('reopen-body', JSON.parse(requests[0].body).reason === 'Correcao da temperatura.', 'the reason travels');
    assert('reopen-reload', win.location.reloads === 1, 'success reloads the authoritative state');
  }

  // ------------------------------------------------------------------ open arbitration through the route map (PL4/H3 mechanics)
  {
    const root = makeRoot();
    const win = loadAdapter(root, async () => ({ ok: true, status: 200, json: async () => ({}) }), null);
    const table = root.querySelectorAll('[data-dmo-dense-table]')[0];

    table.dispatchEvent({ type: 'dmo:open-requested', detail: { rowKey: '22222222-2222-2222-2222-222222222222' } });
    assert('open-route',
      win.location.href === '/controlo/approve?pesoId=22222222-2222-2222-2222-222222222222',
      `open resolves the EXACT record route, got '${win.location.href}'`);
  }

  // ------------------------------------------------------------------ Peso PDF (P2-T08): one request on the peso_id, NO body, pending
  // state while in flight, the deterministic output result shown (generated /
  // already-available), refusals into the page-owned errors presentation,
  // duplicate invocation prevented while pending.
  {
    const root = makeRoot();
    const requests = [];
    const win = loadAdapter(
      root,
      async (url, options) => {
        requests.push({ url: String(url), body: options && options.body });
        return {
          ok: true,
          status: 200,
          json: async () => ({
            status: 'generated',
            pesoId: '11111111-1111-1111-1111-111111111111',
            version: 2,
            fileName: 'Peso_ref-X_B1.pdf',
            relativePath: 'ref-X/prod-1/Peso_ref-X_B1.pdf',
            bytes: 42,
          }),
        };
      },
      null,
    );

    const button = root.querySelectorAll('[data-dmo-peso-pdf]')[0];
    const outcome = root.querySelectorAll('[data-dmo-peso-pdf-outcome]')[0];

    click(button);
    // Pending state while the request is in flight: duplicate invocation prevented, accessible
    // name preserved.
    assert('peso-pdf-pending', button.disabled === true && button.textContent === 'A gerar…',
      `the PDF button is pending while the request is in flight (disabled='${button.disabled}', text='${button.textContent}')`);
    await flush();

    assert('peso-pdf-request', requests.length === 1 && requests[0].url.endsWith('/peso-pdf'),
      `exactly ONE peso-pdf request to the owning-workflow route, got ${requests.length}`);
    assert('peso-pdf-no-body', requests.length === 1 && requests[0].body === undefined,
      'the peso-pdf request carries NO body (only the canonical peso_id path identity)');
    assert('peso-pdf-restored', button.disabled === false && button.textContent === 'Gerar PDF do Peso',
      'the button is restored after the response');
    assert('peso-pdf-outcome', outcome.hidden === false
      && textOf(outcome).indexOf('PDF gerado: Peso_ref-X_B1.pdf') !== -1,
      'the generated output result (file name + relative target) is shown');
    assert('peso-pdf-no-reload', win.location.reloads === 0, 'generation does NOT reload the page');
  }

  // already-available: the same deterministic target is reported, never rewritten.
  {
    const root = makeRoot();
    const win = loadAdapter(
      root,
      async (url) => ({
        ok: true,
        status: 200,
        json: async () => ({
          status: 'already-available',
          pesoId: '11111111-1111-1111-1111-111111111111',
          version: 2,
          fileName: 'Peso_ref-X_B1.pdf',
          relativePath: 'ref-X/prod-1/Peso_ref-X_B1.pdf',
          bytes: 42,
        }),
      }),
      null,
    );

    const button = root.querySelectorAll('[data-dmo-peso-pdf]')[0];
    const outcome = root.querySelectorAll('[data-dmo-peso-pdf-outcome]')[0];

    click(button);
    await flush();
    assert('peso-pdf-available', textOf(outcome).indexOf('PDF já disponível: Peso_ref-X_B1.pdf') !== -1,
      'an existing deterministic target is reported as already-available');
    assert('peso-pdf-available-no-reload', win.location.reloads === 0, 'no reload on already-available');
  }

  // A typed refusal (not-decided) enters the errors presentation and restores the button.
  {
    const root = makeRoot();
    const response = {
      ok: false,
      status: 409,
      json: async () => ({ reason: 'not-decided', message: 'Este Peso ainda não foi decidido.' }),
    };
    const win = loadAdapter(root, async () => response, null);

    const button = root.querySelectorAll('[data-dmo-peso-pdf]')[0];
    const stateNode = root.querySelectorAll('[data-dmo-approve-state]')[0];

    click(button);
    await flush();
    assert('peso-pdf-refused', textOf(stateNode).indexOf('not-decided') !== -1,
      'a typed refusal enters the page-owned errors presentation');
    assert('peso-pdf-refused-restored', button.disabled === false, 'the button is restored after the refusal');
    assert('peso-pdf-refused-no-reload', win.location.reloads === 0, 'no reload on refusal');
  }

  // ------------------------------------------------------------------ summary
  if (failureCount > 0) {
    console.error(`\n${failureCount} scenario(s) FAILED.`);
    process.exit(1);
  }

  console.log('dmo-controlo-approve adapter behavioral scenarios PASSED.');
  process.exit(0);
})();