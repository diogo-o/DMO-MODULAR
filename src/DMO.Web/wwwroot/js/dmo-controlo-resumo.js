/*
    Resumo (Controlo landing) page-owned adapter.

    One responsibility: the productions switcher is a shared DenseDataTable whose open
    arbitration is the accepted `dmo:open-requested` presentation event (double click /
    Enter / the explicit "Abrir" control). This adapter resolves the opaque row key through
    the page-owned route map — never by parsing the key — and navigates to the row's own
    Resumo direct link (?ref=<reference>&production=<production>), so switching the
    production replaces the whole production context of the sheet.

    It is presentation-only: it fetches nothing, persists nothing, constructs no route of
    its own and holds no domain vocabulary. It is idempotent: loading it twice installs no
    second listener.
*/
(function () {
    'use strict';

    if (window.dmoControloResumo) {
        return;
    }

    function closest(node, selector) {
        if (!node || typeof node.closest !== 'function') {
            return null;
        }

        return node.closest(selector);
    }

    document.addEventListener('dmo:open-requested', function (event) {
        var detail = event.detail || {};
        var surface = closest(event.target, '[data-dmo-resumo-surface]');
        var entry = surface && detail.rowKey
            ? surface.querySelector('[data-dmo-resumo-route="' + detail.rowKey + '"]')
            : null;

        if (entry) {
            var href = entry.getAttribute('data-dmo-resumo-href');
            if (href) {
                window.location.assign(href);
            }
        }
    }, false);

    window.dmoControloResumo = {};
})();
