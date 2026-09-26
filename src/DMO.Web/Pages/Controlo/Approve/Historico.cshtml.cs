using DMO.Application.Access;
using DMO.Application.Accounts;
using DMO.Application.Controlo.Approve;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Session;
using DMO.Domain.Controlo;
using DMO.Web.Frontend.Shared.Contracts;
using DMO.Web.Frontend.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMO.Web.Pages.Controlo.Approve;

/// <summary>
/// Histórico de Pesos surface (P2-T06 contract Â§21.2 regions H1â€“H3, Â§20): HISTÃ“RICO (local)
/// inside Controlo Approve â€” never HISTÃ“RICO GLOBAL (<c>historia</c>, deferred by design).
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract Â§20 (filters, rows, decision/open semantics, trail), Â§15.2 (the
/// history query contract), Â§21.2 (H1â€“H3 regions). The list filters are ALL backend-applied over
/// backend-reported facts; rows carry the exact <c>peso_id</c> and the current status/decision/
/// actor/time facts; single click selects, double click opens the exact record (detail = the
/// review sheet + full trail â€” H3 reuses the R3/R5 content); actions live outside the table â€” no
/// per-row action button grid exists (AC-H3). The page is server-gated <c>controlo-approve</c>.
/// <para>The decision actions on the opened detail (Reabrir, Reabrir-then-approve flows) run
/// through the same page-owned adapter and routes as the Aprovar surface (never row grids).</para>
/// </remarks>
[Authorize(Policy = ControloApprovePolicyNames.ControloApprove)]
public sealed class ApproveHistoricoModel : PageModel
{
    private const string ShellTitle = "Histórico de Pesos";
    private const string ShellContext = "Controlo Approve â€” histórico local de Pesos";

    private readonly IControloApproveService _service;
    private readonly IModuleAccessService _access;
    private readonly ICurrentAccountContext _currentAccount;
    private readonly ShellPresentationService _shell;
    private readonly ILogger<ApproveHistoricoModel> _logger;

    /// <summary>Creates the page over the review service, access, the current account and the shell.</summary>
    public ApproveHistoricoModel(
        IControloApproveService service,
        IModuleAccessService access,
        ICurrentAccountContext currentAccount,
        ShellPresentationService shell,
        ILogger<ApproveHistoricoModel> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(currentAccount);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _access = access;
        _currentAccount = currentAccount;
        _shell = shell;
        _logger = logger;
    }

    // ---- H1 â€” filters (the Â§15.2 set; backend-applied) --------------------------------------

    /// <summary>Review-state filter (closed vocabulary <c>submetido|pendente|aprovado|nao_aprovado</c>; a filter, not a status).</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReviewState { get; set; }

    /// <summary>Reference filter (traversal).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Reference { get; set; }

    /// <summary>Production-number filter (traversal).</summary>
    [BindProperty(SupportsGet = true)]
    public string? ProductionNumber { get; set; }

    /// <summary>Machine filter (traversal).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Machine { get; set; }

    /// <summary>Processo filter (through the anchor).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Processo { get; set; }

    /// <summary>Tool-reference filter (frozen CM triple / pending Tool).</summary>
    [BindProperty(SupportsGet = true)]
    public string? ToolReference { get; set; }

    /// <summary>Submitted-from filter.</summary>
    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? SubmittedFrom { get; set; }

    /// <summary>Submitted-to filter.</summary>
    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? SubmittedTo { get; set; }

    /// <summary>Decided-from filter (over the decision trail).</summary>
    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? DecidedFrom { get; set; }

    /// <summary>Decided-to filter (over the decision trail).</summary>
    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? DecidedTo { get; set; }

    /// <summary>Decision actor filter (raw backend user id; validated before use).</summary>
    [BindProperty(SupportsGet = true)]
    public string? DecidedByUserId { get; set; }

    /// <summary>Decision filter (closed vocabulary <c>aprovado|nao_aprovado|reaberto</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Decision { get; set; }

    /// <summary>1-based page.</summary>
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    /// <summary>Page size (1â€“100).</summary>
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 50;

    /// <summary>Open the exact record (double click / explicit open).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? PesoId { get; set; }

    // ---- Presentation state -----------------------------------------------------------------

    /// <summary>The exact contracted validation codes of a refused query.</summary>
    public IReadOnlyList<string> ValidationErrors { get; private set; } = [];

    /// <summary>H2 â€” the history table, or <c>null</c> on a refused query.</summary>
    public DenseTablePresentation? History { get; private set; }

    /// <summary>The page-owned opaque row-key â†’ detail route map.</summary>
    public IReadOnlyList<ReviewRouteEntry> OpenRoutes { get; private set; } = [];

    /// <summary>H3 â€” the opened record's review sheet (detail; R3 content + full trail).</summary>
    public ReviewSheetView? Sheet { get; private set; }

    /// <summary>The detail decision region (DecisionBar).</summary>
    public DecisionBarPresentation? Actions { get; private set; }

    /// <summary>The Enviar-para-produção affordance state (Q-SEND, Â§23.2).</summary>
    public SendAffordancePresentation? Send { get; private set; }

    /// <summary>The detail decision trail.</summary>
    public AuditTrailPresentation? Trail { get; private set; }

    /// <summary>The observed version of the opened record.</summary>
    public int ObservedVersion { get; private set; } = 1;

    /// <inheritdoc />
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var current = await _currentAccount.GetCurrentAsync(cancellationToken);
        ViewData["DmoShell"] = await _shell.BuildAsync(current, ShellTitle, ShellContext, cancellationToken);

        CanCreate = current is CurrentAccount.User(var user)
            && await _access.HasModuleAsync(
                new AccountResolution.User(user), ModuleCatalog.ControloCreate, cancellationToken);

        BuildTabs(CanCreate);

        if (PesoId is { } pesoId)
        {
            await LoadReviewSheetAsync(pesoId, cancellationToken);
        }

        await LoadHistoryAsync(cancellationToken);
    }

    /// <summary>Whether the caller also holds <c>controlo-create</c> (gates the create-side tabs).</summary>
    public bool CanCreate { get; private set; }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var decidedBy = ParseOptionalGuid(DecidedByUserId);

        // A supplied but unparseable actor filter is a refused filter (FILTER_INVALID), never a
        // silent full list (AC-H2/AP3).
        if (DecidedByUserId is not null && decidedBy is null)
        {
            ValidationErrors = [ControloApproveValidationErrors.FilterInvalid];
            return;
        }

        var query = new HistoryListQuery(
            Normalize(ReviewState),
            Normalize(Reference),
            Normalize(ProductionNumber),
            Normalize(Machine),
            Normalize(Processo),
            Normalize(ToolReference),
            SubmittedFrom,
            SubmittedTo,
            DecidedFrom,
            DecidedTo,
            decidedBy,
            Normalize(Decision),
            Math.Max(PageNumber, 1),
            PageSize);

        var result = await _service.GetHistoryAsync(query, cancellationToken);

        switch (result)
        {
            case ReviewResult.HistoryFound(var rows, var total):
                BuildHistory(rows, total);
                break;

            case ReviewResult.ValidationFailed(var errors):
                ValidationErrors = errors;
                break;

            case ReviewResult.Refused(var reason, var message):
                _logger.LogWarning("History list refused ({Reason}): {Message}", reason, message);
                break;

            default:
                break;
        }
    }

    private void BuildHistory(IReadOnlyList<HistoryItemReadModel> rows, int total)
    {
        var columns = new List<DenseTableColumnPresentation>
        {
            Column("submittedAt", "Submetido em"),
            Column("reference", "Referência"),
            Column("productionNumber", "Produção"),
            Column("machine", "Máquina"),
            Column("toolReference", "Ferramenta"),
            Column("processo", "Processo"),
            Column("lastDecision", "Ãšltima decisão"),
            Column("decisionCount", "Decisões"),
        };

        var tableRows = rows
            .Select(row => DenseTableRowPresentation.Create(
                row.PesoId.ToString(),
                $"Peso {row.PesoId} â€” {row.Reference ?? row.ToolReference ?? "(sem contexto)"}",
                [
                    DenseTableCellPresentation.Create(row.SubmittedAt?.ToString("yyyy-MM-dd HH:mm") + " UTC" ?? "â€”"),
                    DenseTableCellPresentation.Create(row.Reference ?? "â€”"),
                    DenseTableCellPresentation.Create(row.ProductionNumber ?? "â€”"),
                    DenseTableCellPresentation.Create(row.Machine ?? "â€”"),
                    DenseTableCellPresentation.Create(row.ToolReference ?? "â€”"),
                    DenseTableCellPresentation.Create(row.Processo ?? "â€”"),
                    DenseTableCellPresentation.Create(
                        row.LastDecision is { } last
                            ? $"{DecisionLabel(last.Decision)} â€” {last.DecidedAt:yyyy-MM-dd HH:mm} UTC"
                            : "â€”"),
                    DenseTableCellPresentation.Create(row.DecisionCount.ToString()),
                ],
                status: RecordStatusPresentation.Create(
                    row.Status switch
                    {
                        "pendente" => "Pendente",
                        "aprovado" => "Aprovado",
                        _ => "Não aprovado",
                    },
                    StatusTone.Neutral)))
            .ToList();

        // Single click selects; double click (or the explicit open control) opens the exact record
        // with its decision/reopen trail (decision/open arbitration, AC-H3/H5).
        History = DenseTablePresentation.Ready(
            caption: "Histórico de Pesos",
            columns,
            tableRows,
            selectionEnabled: true,
            openEnabled: true,
            resultSummary: rows.Count == 0 ? null : $"{total} {"linha" + (total == 1 ? "" : "s")}");

        OpenRoutes = rows
            .Select(row => new ReviewRouteEntry(
                row.PesoId.ToString(),
                $"/controlo/approve/historico?pesoId={row.PesoId}"))
            .ToList();
    }

    private async Task LoadReviewSheetAsync(Guid pesoId, CancellationToken cancellationToken)
    {
        var result = await _service.GetReviewSheetAsync(pesoId, cancellationToken);

        if (result is not ReviewResult.ReviewSheet(var sheet))
        {
            _logger.LogWarning("Review sheet for '{PesoId}' could not be loaded.", pesoId);
            return;
        }

        ObservedVersion = sheet.Peso.Version;
        Sheet = new ReviewSheetView(sheet.Peso, sheet.Decisions, sheet.Availability);
        Actions = BuildDecisionBar(sheet);
        Send = BuildSendAffordance(sheet);
        Trail = BuildTrail(sheet.Decisions);
    }

    private static DecisionBarPresentation BuildDecisionBar(ReviewSheetReadModel sheet)
    {
        var availability = sheet.Availability;
        var actions = new List<DecisionBarActionPresentation>();

        actions.Add(DecisionBarActionPresentation.Create(
            availability.CanApprove
                ? SharedActionPresentation.CreateEnabled("approve", "Aprovar", "A aprovarâ€¦")
                : SharedActionPresentation.CreateDisabled("approve", "Aprovar", availability.ApproveDisabledReason!),
            DecisionBarActionGroup.Primary));

        actions.Add(DecisionBarActionPresentation.Create(
            availability.CanReject
                ? SharedActionPresentation.CreateEnabled("reject", "Rejeitar", "A rejeitarâ€¦")
                : SharedActionPresentation.CreateDisabled("reject", "Rejeitar", availability.RejectDisabledReason!),
            DecisionBarActionGroup.Danger));

        actions.Add(DecisionBarActionPresentation.Create(
            availability.CanReopen
                ? SharedActionPresentation.CreateEnabled("reopen", "Reabrir", "A reabrirâ€¦")
                : SharedActionPresentation.CreateDisabled("reopen", "Reabrir", availability.ReopenDisabledReason!),
            DecisionBarActionGroup.Secondary));

        return DecisionBarPresentation.Create(
            CommonState.Ready,
            actions,
            regionLabel: "Decisão",
            statusText: "As decisões são humanas: indÃ­cios e avisos nunca decidem por si.");
    }

    /// <summary>The Enviar-para-produção initiation affordance (Q-SEND, Â§23.2): visible ONLY on
    /// approved Pesos, unavailable-with-reason until P2-T08 owns execution; zero send mechanics
    /// and zero send persistence (Â§23.4, BND-B2).</summary>
    private static SendAffordancePresentation? BuildSendAffordance(ReviewSheetReadModel sheet)
    {
        if (!string.Equals(sheet.Peso.Status, PesoStatusTokens.ToToken(PesoStatus.Aprovado), StringComparison.Ordinal))
        {
            return null;
        }

        return new SendAffordancePresentation(
            Visible: true,
            Enabled: false,
            Reason: "O envio para produção é definido pelo contrato de documentos (P2-T08), ainda não disponÃ­vel.");
    }

    private static AuditTrailPresentation BuildTrail(IReadOnlyList<DecisionItemReadModel> decisions)
    {
        if (decisions.Count == 0)
        {
            return AuditTrailPresentation.Empty("Decisões", "Sem decisões â€” este Peso ainda não foi decidido.");
        }

        var entries = decisions
            .Select(decision => AuditEntryPresentation.Create(
                decision.PesoReviewDecisionId.ToString(),
                $"{DecisionLabel(decision.Decision)} â€” {decision.PriorStatus} â†’ {CurrentLabel(decision.Decision)}",
                actor: decision.DecidedByUserId.ToString(),
                actorUnavailableText: null,
                timestampText: decision.DecidedAt.ToString("yyyy-MM-dd HH:mm") + " UTC",
                timestampValue: decision.DecidedAt,
                detail: decision.Reason is null
                    ? null
                    : $"Motivo: {decision.Reason}",
                beforeDetail: null,
                afterDetail: $"versão {decision.PesoVersionAtDecision}",
                detailAction: null))
            .ToList();

        return AuditTrailPresentation.Ready("Decisões", entries);
    }

    private static string DecisionLabel(string token) => token switch
    {
        "aprovado" => "Aprovado",
        "nao_aprovado" => "Não aprovado",
        _ => "Reaberto",
    };

    private static string CurrentLabel(string token) => token switch
    {
        "aprovado" => "aprovado",
        "nao_aprovado" => "não aprovado",
        _ => "pendente (reaberto para edição)",
    };

    private static Guid? ParseOptionalGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Back-link to the Controlo landing surface. When the active filters name the production
    /// identity (referência + produção), the link re-opens the Resumo of that exact production
    /// through the Resumo route's own direct-link contract (<c>?ref=…&amp;production=…</c>) —
    /// no new state mechanism, the existing query contracts of both routes do the round-trip.
    /// </summary>
    public string ResumoHref
    {
        get
        {
            var reference = Normalize(Reference);
            if (reference is null)
            {
                return ControloTabs.ResumoRoute;
            }

            var production = Normalize(ProductionNumber);
            return production is null
                ? $"{ControloTabs.ResumoRoute}?ref={Uri.EscapeDataString(reference)}"
                : $"{ControloTabs.ResumoRoute}?ref={Uri.EscapeDataString(reference)}&production={Uri.EscapeDataString(production)}";
        }
    }

    /// <summary>The secondary Controlo tab strip, with Histórico as the current tab.</summary>
    public IReadOnlyList<ControloTabPresentation> Tabs { get; private set; } = [];

    private void BuildTabs(bool canCreate)
    {
        Tabs = ControloTabs.Build(
            ControloTabs.HistoricoKey,
            canCreate: canCreate,
            canApprove: true,
            resumoHref: ResumoHref,
            pesoHref: ControloTabs.PesoRoute,
            historicoHref: ControloTabs.HistoricoRoute);
    }

    private static DenseTableColumnPresentation Column(string key, string heading) =>
        DenseTableColumnPresentation.Create(key, heading, alignment: DenseTableColumnAlignment.Start);

    /// <summary>A row of the page-owned open-route map (opaque key â†’ exact detail route).</summary>
    public sealed record ReviewRouteEntry(string Key, string Href);

    /// <summary>The review-sheet view state of the page (shared read model + trail + availability).</summary>
    public sealed record ReviewSheetView(
        PesoSheetReadModel Peso,
        IReadOnlyList<DecisionItemReadModel> Decisions,
        ReviewAvailability Availability);

    /// <summary>The Enviar-para-produção affordance state (Â§23.2).</summary>
    public sealed record SendAffordancePresentation(bool Visible, bool Enabled, string Reason);
}
