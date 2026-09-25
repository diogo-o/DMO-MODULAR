using DMO.Application.Access;
using DMO.Application.Accounts;
using DMO.Application.ControloCreate;
using DMO.Application.JobOn;
using DMO.Application.Session;
using DMO.Application.Tools;
using DMO.Web.Frontend.Shared.Contracts;
using DMO.Web.Frontend.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMO.Web.Pages.Controlo;

/// <summary>
/// Resumo — the Controlo landing page (design sources: <c>current/modulos/controlo/resumo.md</c>,
/// <c>current/FLUXO_FUNCIONAL.md</c> flow 3; layout adapted from
/// <c>current/dist/21_CONTROLO_01_VISUAL_AUTHORITY_controlo.html</c>).
/// The page always opens: with no query it offers the reference lookup; a reference lookup lists
/// that reference's Job On productions NEWEST FIRST for explicit selection (never auto-selected);
/// a selected occurrence renders the whole Resumo sheet anchored on its jobon_id, and switching
/// the production replaces the entire production context of the sheet.
/// </summary>
/// <remarks>
/// <para>
/// The Resumo is a read-only entry surface, not a persisted record: no table, identity, lifecycle
/// or status is created for it (the prototype's "Guardar Resumo" action, Job On revision counter
/// and MCaliper links are prototype-only and are NOT reproduced). Every fact comes from existing
/// authority — the reference-keyed Job On productions query
/// (<see cref="IJobOnService.FindProductionsAsync"/>) and <see cref="IProductionResumoRead"/>
/// (the jobon-anchored projection) — and nothing is recomputed, re-entered, duplicated or
/// invented here. The direct link <c>?ref=&lt;reference&gt;&amp;production=&lt;production&gt;</c>
/// opens a specific Resumo; a bare <c>?jobonId=</c> opens by the canonical anchor. Switching the
/// production replaces the whole production context of the sheet.</para>
/// <para>
/// The backend projection carries no per-production sheet state (no Estado), no Peso status, no
/// MCaliper data and no Pegamentos record, so none is rendered: areas without a real backend
/// surface (Comparação, Pegamentos, and Histórico for a caller without <c>controlo-approve</c>)
/// present a truthful unavailable state instead of fabricated content. Peso links to the existing
/// Create surface. The sheet renders the SAME <see cref="ProductionResumoReadModel"/> projection
/// a later Approve-side read-only view must consume — nothing here recomputes or copies it.</para>
/// <para>
/// Server-gated <c>controlo-create</c> exactly like every other P2-T05 route; no new policy, no
/// availability entry and no destination-route registration is added by this page (availability
/// and destination routes remain P2-T10's registration seam).
/// </para>
/// </remarks>
[Authorize(Policy = ControloPolicyNames.ControloCreate)]
public sealed class ResumoModel : PageModel
{
    private const string ShellTitle = "Resumo";
    private const string ShellContext = "Resumo da produção — entrada do Controlo";

    private readonly IJobOnService _jobOns;
    private readonly IProductionResumoRead _productionResumo;
    private readonly IModuleAccessService _access;
    private readonly ICurrentAccountContext _currentAccount;
    private readonly ShellPresentationService _shell;
    private readonly ILogger<ResumoModel> _logger;

    /// <summary>Creates the page over the Job On service, the Resumo read, access and the shell.</summary>
    public ResumoModel(
        IJobOnService jobOns,
        IProductionResumoRead productionResumo,
        IModuleAccessService access,
        ICurrentAccountContext currentAccount,
        ShellPresentationService shell,
        ILogger<ResumoModel> logger)
    {
        ArgumentNullException.ThrowIfNull(jobOns);
        ArgumentNullException.ThrowIfNull(productionResumo);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(currentAccount);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(logger);
        _jobOns = jobOns;
        _productionResumo = productionResumo;
        _access = access;
        _currentAccount = currentAccount;
        _shell = shell;
        _logger = logger;
    }

    /// <summary>The reference of the design direct link <c>?ref=…&amp;production=…</c>.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Ref { get; set; }

    /// <summary>Accepted alias of <see cref="Ref"/> matching the other surfaces' query convention.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Reference { get; set; }

    /// <summary>The production number of the design direct link <c>?ref=…&amp;production=…</c>.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Production { get; set; }

    /// <summary>The explicitly selected production occurrence by its canonical jobon anchor.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? JobOnId { get; set; }

    /// <summary>Whether the productions lookup did not complete (never aliased to empty).</summary>
    public bool LookupFailed { get; private set; }

    /// <summary>Whether the selected occurrence is unknown (truthful not-found; the page still opens).</summary>
    public bool ResumoNotFound { get; private set; }

    /// <summary>Whether the direct link named a production number the reference does not have.</summary>
    public bool ProductionNotFound { get; private set; }

    /// <summary>The exact contracted validation codes of a refused lookup.</summary>
    public IReadOnlyList<string> ValidationErrors { get; private set; } = [];

    /// <summary>The productions switcher table (newest first), or null when no lookup was performed.</summary>
    public DenseTablePresentation? Productions { get; private set; }

    /// <summary>The page-owned opaque row-key → resumo route map of the productions switcher.</summary>
    public IReadOnlyList<ResumoRouteEntry> ProductionRoutes { get; private set; } = [];

    /// <summary>The consolidated Resumo sheet of the selected occurrence (null until one is selected).</summary>
    public ProductionResumoReadModel? Resumo { get; private set; }

    /// <summary>The read-only production context strip (real backend facts; never editable).</summary>
    public ResumoStripModel? Strip { get; private set; }

    /// <summary>The live CM Tool summary row of the selected occurrence (<c>null</c> when no CM context).</summary>
    public ToolSummaryRowPresentation? CmTool { get; private set; }

    /// <summary>Whether the selected occurrence has no CM context (missing-context recovery).</summary>
    public bool HasNoCmContext => Resumo is not null && Resumo.Cm is null;

    /// <summary>Whether the caller also holds <c>controlo-approve</c> (exposes the Histórico tab).</summary>
    public bool CanViewHistorico { get; private set; }

    /// <summary>The secondary Controlo tab strip (Resumo first; unavailable areas stated, never faked).</summary>
    public IReadOnlyList<ControloTabPresentation> Tabs { get; private set; } = [];

    /// <summary>The reference the lookup ran with (form round-trip).</summary>
    public string? LookupReference { get; private set; }

    /// <summary>The Peso tab/card target: the existing Peso surface, anchored when a production is selected.</summary>
    public string PesoHref => Resumo is { } resumo
        ? $"/controlo/create?jobonId={resumo.JobOnId}"
        : ControloTabs.PesoRoute;

    /// <summary>
    /// The Histórico tab target: the existing Histórico de Pesos surface, carrying the selected
    /// production identity (referência / produção / máquina) through the Histórico page's own GET
    /// filter contract so the operator lands on the matching history rows. No new state mechanism.
    /// </summary>
    public string HistoricoHref => Strip is { } strip
        ? $"{ControloTabs.HistoricoRoute}?reference={Uri.EscapeDataString(strip.Reference)}" +
          $"&productionNumber={Uri.EscapeDataString(strip.ProductionNumber)}" +
          $"&machine={Uri.EscapeDataString(strip.Machine)}"
        : ControloTabs.HistoricoRoute;

    /// <inheritdoc />
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var current = await _currentAccount.GetCurrentAsync(cancellationToken);
        ViewData["DmoShell"] = await _shell.BuildAsync(current, ShellTitle, ShellContext, cancellationToken);

        CanViewHistorico = current is CurrentAccount.User(var user)
            && await _access.HasModuleAsync(
                new AccountResolution.User(user), ModuleCatalog.ControloApprove, cancellationToken);

        Ref = Ref?.Trim();
        Reference = Reference?.Trim();
        Production = Production?.Trim();
        LookupReference = !string.IsNullOrWhiteSpace(Ref) ? Ref : Reference;

        if (JobOnId is { } jobOnId)
        {
            var byAnchor = await _productionResumo.GetResumoAsync(jobOnId, cancellationToken);
            if (byAnchor is null)
            {
                ResumoNotFound = true;
                BuildTabs();
                return;
            }

            SetSheet(byAnchor);
            await LoadSwitcherAsync(byAnchor, cancellationToken);
            BuildTabs();
            return;
        }

        if (!string.IsNullOrWhiteSpace(LookupReference))
        {
            var productions = await LookupProductionsAsync(LookupReference, cancellationToken);
            if (productions is null)
            {
                BuildTabs();
                return;
            }

            if (!string.IsNullOrWhiteSpace(Production))
            {
                var chosen = productions.FirstOrDefault(production =>
                    string.Equals(production.ProductionNumber, Production, StringComparison.Ordinal));
                if (chosen is null)
                {
                    // Truthful not-found: the reference lookup succeeded but has no such
                    // production; the switcher still lists what DOES exist, nothing is fabricated.
                    ProductionNotFound = true;
                    BuildSwitcher(productions);
                    BuildTabs();
                    return;
                }

                var resumo = await _productionResumo.GetResumoAsync(chosen.JobOnId, cancellationToken);
                if (resumo is null)
                {
                    ResumoNotFound = true;
                    BuildSwitcher(productions);
                    BuildTabs();
                    return;
                }

                SetSheet(resumo);
                BuildSwitcher(productions, resumo.JobOnId);
                BuildTabs();
                return;
            }

            BuildSwitcher(productions);
            BuildTabs();
            return;
        }

        BuildTabs();
    }

    /// <summary>Runs the reference-keyed Job On productions query; <c>null</c> on any failure.</summary>
    private async Task<IReadOnlyList<JobOnProductionListItem>?> LookupProductionsAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _jobOns.FindProductionsAsync(
                new FindProductionsQuery(reference),
                cancellationToken);

            switch (result)
            {
                case JobOnResult.ValidationFailed(var errors):
                    ValidationErrors = errors;
                    return null;

                case JobOnResult.ProductionsFound(var productions):
                    return productions;

                case JobOnResult.Refused(var reason, var message, _, _):
                    _logger.LogWarning("Productions lookup refused ({Reason}): {Message}", reason, message);
                    LookupFailed = true;
                    return null;

                default:
                    LookupFailed = true;
                    return null;
            }
        }
        catch (Exception exception)
        {
            // A lookup failure is reported as a lookup failure and is never aliased to "no results".
            _logger.LogWarning(exception, "Productions lookup failed.");
            LookupFailed = true;
            return null;
        }
    }

    /// <summary>Loads the production switcher of an already-selected sheet's own reference.</summary>
    private async Task LoadSwitcherAsync(ProductionResumoReadModel resumo, CancellationToken cancellationToken)
    {
        var productions = await LookupProductionsAsync(resumo.Reference, cancellationToken);
        if (productions is not null)
        {
            BuildSwitcher(productions, resumo.JobOnId);
        }
    }

    /// <summary>
    /// Builds the productions switcher NEWEST FIRST (production date descending, undated last,
    /// production number descending as the tie-break): the repository's contracted order is a
    /// deterministic technical order with no industrial meaning, so the newest-first display
    /// order is this page's presentation responsibility. Selection stays explicit — the table
    /// never auto-selects and never auto-opens; the loaded sheet's own row is only marked.
    /// </summary>
    private void BuildSwitcher(IReadOnlyList<JobOnProductionListItem> productions, Guid? selectedJobOnId = null)
    {
        var ordered = productions
            .OrderByDescending(production => production.ProductionDate.HasValue)
            .ThenByDescending(production => production.ProductionDate)
            .ThenByDescending(production => production.ProductionNumber, StringComparer.Ordinal)
            .ToArray();

        var columns = new List<DenseTableColumnPresentation>
        {
            DenseTableColumnPresentation.Create("reference", "Referência"),
            DenseTableColumnPresentation.Create("production-number", "Produção", widthHint: DenseTableColumnWidthHint.Compact),
            DenseTableColumnPresentation.Create("machine", "Máquina", widthHint: DenseTableColumnWidthHint.Compact),
            DenseTableColumnPresentation.Create("production-date", "Data", widthHint: DenseTableColumnWidthHint.Compact),
        };

        var rows = new List<DenseTableRowPresentation>(ordered.Length);
        var routes = new List<ResumoRouteEntry>(ordered.Length);
        string? selectedKey = null;

        for (var index = 0; index < ordered.Length; index++)
        {
            var production = ordered[index];
            var key = $"p-{index}";

            // The row key is opaque; the canonical jobon anchor is resolved through this page's
            // own route map, never by parsing the key. The href is the design's direct link, so
            // switching the production replaces the whole production context of the sheet.
            routes.Add(new ResumoRouteEntry(
                key,
                $"/controlo/resumo?ref={Uri.EscapeDataString(production.Reference)}&production={Uri.EscapeDataString(production.ProductionNumber)}"));

            if (selectedJobOnId is { } selected && production.JobOnId == selected)
            {
                selectedKey = key;
            }

            rows.Add(DenseTableRowPresentation.Create(
                key,
                $"{production.Reference} — produção {production.ProductionNumber}",
                [
                    DenseTableCellPresentation.Create(production.Reference),
                    DenseTableCellPresentation.Create(production.ProductionNumber),
                    DenseTableCellPresentation.Create(production.Machine),
                    DenseTableCellPresentation.Create(production.ProductionDate?.ToString("yyyy-MM-dd") ?? "sem data"),
                ]));
        }

        ProductionRoutes = routes;

        // Every occurrence is listed; the operator opens ONE explicitly, never automatically,
        // even when exactly one row is returned.
        Productions = DenseTablePresentation.Create(
            rows.Count > 0 ? CommonState.Ready : CommonState.Empty,
            "Resumos desta referência",
            columns,
            rows,
            selectionEnabled: true,
            selectedKey: selectedKey,
            openEnabled: true,
            message: rows.Count > 0 ? null : "Não existem produções para esta referência.",
            resultSummary: $"{rows.Count} produção(ões) encontradas.",
            actionsColumnHeading: "Abrir",
            regionLabel: "Produções");
    }

    /// <summary>Mounts the selected occurrence's sheet: strip facts + the CM context presentation.</summary>
    private void SetSheet(ProductionResumoReadModel resumo)
    {
        Resumo = resumo;
        LookupReference = resumo.Reference;

        Strip = new ResumoStripModel(
            resumo.Reference,
            resumo.ProductionNumber,
            resumo.Machine,
            resumo.ProductionDate,
            resumo.Cm?.FrozenToolReference,
            resumo.Cm?.Processo is { } processo ? ToolTokens.ToToken(processo) : null,
            resumo.Cm?.FrozenToolLot);

        if (resumo.Cm is { } cm)
        {
            CmTool = ToolSummaryRowPresentation.Create(
                CommonState.Ready,
                cm.ToolId.ToString(),
                type: ToolSummaryFactPresentation.Create("Tipo", cm.FrozenToolType),
                reference: ToolSummaryFactPresentation.Create("Referência", cm.LiveToolReference),
                lot: ToolSummaryFactPresentation.Create("Lote", cm.LiveToolLot),
                process: ToolSummaryFactPresentation.Create("Processo", ToolTokens.ToToken(cm.Processo) ?? "—"),
                quantity: ToolSummaryFactPresentation.Create("Quantidade", cm.Quantity?.ToString() ?? "—"));
        }
    }

    private void BuildTabs()
    {
        Tabs = ControloTabs.Build(
            ControloTabs.ResumoKey,
            canCreate: true,
            canApprove: CanViewHistorico,
            resumoHref: ControloTabs.ResumoRoute,
            pesoHref: PesoHref,
            historicoHref: HistoricoHref);
    }
}

/// <summary>
/// One entry of the page-owned route map: an opaque DenseDataTable row key to that row's Resumo
/// direct link (<c>?ref=&lt;reference&gt;&amp;production=&lt;production&gt;</c>). The row key is
/// opaque by contract; the canonical jobon anchor is never parsed out of it, and the JS adapter
/// resolves the key through this map, never the other way around.
/// </summary>
public sealed record ResumoRouteEntry(string Key, string Href);

/// <summary>
/// The read-only production context strip facts (real backend reads only), in the contracted
/// display order Referência → Produção → Máquina → Data → CM → Processo → Lote. No Estado is
/// carried: the backend projection exposes no sheet state, so none is invented.
/// </summary>
public sealed record ResumoStripModel(
    string Reference,
    string ProductionNumber,
    string Machine,
    DateOnly? ProductionDate,
    string? CmReference,
    string? Processo,
    string? Lot);
