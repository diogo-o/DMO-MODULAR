using DMO.Application.ControloApprove;
using DMO.Application.ControloCreate;
using DMO.Application.Session;
using DMO.Domain.Controlo;
using DMO.Web.Frontend.Shared.Contracts;
using DMO.Web.Frontend.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMO.Web.Pages.Controlo.Approve;

/// <summary>
/// Aprovar surface (P2-T06 contract Â§21.1 regions R1â€“R5): the pending/review list, the exact-record
/// review sheet (shared read-only Peso presentation + decision trail) and the decision region.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract Â§15 (review list/read/detail), Â§16 (shared Peso read-model reuse â€”
/// the sheet renders the EXACT <c>PesoSheetReadModel</c> facts with the same labels/order/â‰¤ 2-dp
/// normalization as the Create read-only view â€” RD2), Â§19 (approve/reject/reopen), Â§23 (Enviar para
/// produção: an initiation affordance on approved Pesos, currently unavailable-with-reason until
/// P2-T08 owns execution; zero send persistence), Â§20/Â§21 (local Histórico boundaries; fixed
/// desktop at 1366 Ã— 768). The page is server-gated <c>controlo-approve</c>.
/// <para>
/// The mutating actions (Aprovar / Rejeitar / Reabrir) are executed by the page-owned adapter
/// (<c>dmo-controlo-approve.js</c>) against the accepted minimal-API routes; backend validation is
/// authoritative, every persisted-state refusal surfaces its typed reason, and a stale version
/// enters the accepted conflict presentation with the explicit reload recovery (D2).</para>
/// </remarks>
[Authorize(Policy = ControloApprovePolicyNames.ControloApprove)]
public sealed class ApproveIndexModel : PageModel
{
    private const string ShellTitle = "Aprovar";
    private const string ShellContext = "Controlo Approve â€” revisão e decisão de Pesos";

    private readonly IControloApproveService _service;
    private readonly ICurrentAccountContext _currentAccount;
    private readonly ShellPresentationService _shell;
    private readonly ILogger<ApproveIndexModel> _logger;

    /// <summary>Creates the page over the review service, the current account and the shell.</summary>
    public ApproveIndexModel(
        IControloApproveService service,
        ICurrentAccountContext currentAccount,
        ShellPresentationService shell,
        ILogger<ApproveIndexModel> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(currentAccount);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _currentAccount = currentAccount;
        _shell = shell;
        _logger = logger;
    }

    // ---- R1 â€” pending-list filters (backend-applied; Â§15.1) --------------------------------

    /// <summary>Reference filter (traversal through <c>cm_id â†’ job_ons</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Reference { get; set; }

    /// <summary>Production-number filter (traversal).</summary>
    [BindProperty(SupportsGet = true)]
    public string? ProductionNumber { get; set; }

    /// <summary>Machine filter (traversal).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Machine { get; set; }

    /// <summary>Processo filter (NNPB/PS, through the anchor).</summary>
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

    /// <summary>1-based page (DenseDataTable consumer-owned paging).</summary>
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    /// <summary>Page size (1â€“100, contract Â§15.1).</summary>
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 50;

    /// <summary>Open the exact record (double click / explicit open; Â§15.1).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? PesoId { get; set; }

    // ---- Presentation state -----------------------------------------------------------------

    /// <summary>The exact contracted validation codes of a refused query.</summary>
    public IReadOnlyList<string> ValidationErrors { get; private set; } = [];

    /// <summary>R2 â€” the pending/review table, or <c>null</c> on a refused query.</summary>
    public DenseTablePresentation? Pending { get; private set; }

    /// <summary>The page-owned opaque row-key â†’ review route map of the pending table.</summary>
    public IReadOnlyList<ReviewRouteEntry> OpenRoutes { get; private set; } = [];

    /// <summary>R3 â€” the exact-record review sheet (shared read model + trail + availability).</summary>
    public ReviewSheetView? Sheet { get; private set; }

    /// <summary>R4 â€” the decision region (DecisionBar).</summary>
    public DecisionBarPresentation? Actions { get; private set; }

    /// <summary>R4 â€” the Enviar-para-produção affordance state (Q-SEND, Â§23.2).</summary>
    public SendAffordancePresentation? Send { get; private set; }

    /// <summary>
    /// Whether the Peso PDF generation action is offered (P2-T08 documents slice): true only for a
    /// DECIDED, production-bound Peso â€” documents become available after the decision and their
    /// target is derived from the Job On traversal facts (a pending <c>Job On por associar</c>
    /// Peso has no target). Generation is executed by the page-owned adapter against the
    /// <c>peso-pdf</c> route and never shows a filesystem path, only the relative convention target.
    /// </summary>
    public bool CanGeneratePesoPdf { get; private set; }

    /// <summary>R5 â€” the decision trail of the opened record.</summary>
    public AuditTrailPresentation? Trail { get; private set; }

    /// <summary>The observed version carried to the decision routes (real Peso version).</summary>
    public int ObservedVersion { get; private set; } = 1;

    /// <inheritdoc />
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var current = await _currentAccount.GetCurrentAsync(cancellationToken);
        ViewData["DmoShell"] = await _shell.BuildAsync(current, ShellTitle, ShellContext, cancellationToken);

        if (PesoId is { } pesoId)
        {
            await LoadReviewSheetAsync(pesoId, cancellationToken);
        }

        await LoadPendingAsync(cancellationToken);
    }

    private async Task LoadPendingAsync(CancellationToken cancellationToken)
    {
        var query = new PendingListQuery(
            Normalize(Reference),
            Normalize(ProductionNumber),
            Normalize(Machine),
            Normalize(Processo),
            Normalize(ToolReference),
            SubmittedFrom,
            SubmittedTo,
            Math.Max(PageNumber, 1),
            PageSize);

        var result = await _service.GetPendingAsync(query, cancellationToken);

        switch (result)
        {
            case ReviewResult.PendingFound(var rows, var total):
                BuildPending(rows, total);
                break;

            case ReviewResult.ValidationFailed(var errors):
                ValidationErrors = errors;
                break;

            case ReviewResult.Refused(var reason, var message):
                _logger.LogWarning("Pending list refused ({Reason}): {Message}", reason, message);
                break;

            default:
                break;
        }
    }

    private void BuildPending(IReadOnlyList<PendingItemReadModel> rows, int total)
    {
        var columns = new List<DenseTableColumnPresentation>
        {
            Column("submittedAt", "Submetido em"),
            Column("reference", "Referência"),
            Column("productionNumber", "Produção"),
            Column("machine", "Máquina"),
            Column("toolReference", "Ferramenta"),
            Column("processo", "Processo"),
            Column("rowCount", "Leituras"),
            Column("trail", "Decisões"),
        };

        var tableRows = rows
            .Select(row => DenseTableRowPresentation.Create(
                row.PesoId.ToString(),
                $"Peso {row.PesoId} â€” {row.Reference ?? row.ToolReference ?? "(sem contexto)"}",
                [
                    DenseTableCellPresentation.Create(row.SubmittedAt.ToString("yyyy-MM-dd HH:mm") + " UTC"),
                    DenseTableCellPresentation.Create(row.Reference ?? "â€”"),
                    DenseTableCellPresentation.Create(row.ProductionNumber ?? "â€”"),
                    DenseTableCellPresentation.Create(row.Machine ?? "â€”"),
                    DenseTableCellPresentation.Create(row.ToolReference ?? "â€”"),
                    DenseTableCellPresentation.Create(row.Processo ?? "â€”"),
                    DenseTableCellPresentation.Create(row.RowCount.ToString()),
                    DenseTableCellPresentation.Create(row.HasDecisionTrail ? "Sim" : "â€”"),
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

        // Single click selects the row; double click (or the explicit open control) opens the
        // exact record â€” the accepted DenseDataTable arbitration (Â§15.1/Â§21.1 R2, AC-AP2/H3).
        Pending = DenseTablePresentation.Ready(
            caption: "Pesos para aprovação",
            columns,
            tableRows,
            selectionEnabled: true,
            openEnabled: true,
            resultSummary: rows.Count == 0 ? null : $"{total} {"linha" + (total == 1 ? "" : "s")}",
            filters:
            [
                Filter("reference", "Referência", Reference),
            ]);

        OpenRoutes = rows
            .Select(row => new ReviewRouteEntry(
                row.PesoId.ToString(),
                $"/controlo/approve?pesoId={row.PesoId}"))
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

        // The Peso PDF action is offered only for decided, production-bound records (documents
        // become available after the decision; the target needs the Job On traversal facts).
        var decided = sheet.Peso.Status is not null
            && (PesoStatusTokens.Parse(sheet.Peso.Status) is PesoStatus.Aprovado
                or PesoStatus.NaoAprovado);
        CanGeneratePesoPdf = decided && sheet.Peso.Production is not null;

        Actions = BuildDecisionBar(sheet);
        Send = BuildSendAffordance(sheet);
        Trail = BuildTrail(sheet.Decisions);
    }

    private static DecisionBarPresentation BuildDecisionBar(ReviewSheetReadModel sheet)
    {
        var availability = sheet.Availability;
        var actions = new List<DecisionBarActionPresentation>();

        // Aprovar: the primary decision (human-only; never implied by warnings/results).
        actions.Add(DecisionBarActionPresentation.Create(
            availability.CanApprove
                ? SharedActionPresentation.CreateEnabled("approve", "Aprovar", "A aprovarâ€¦")
                : SharedActionPresentation.CreateDisabled("approve", "Aprovar", availability.ApproveDisabledReason!),
            DecisionBarActionGroup.Primary));

        // Rejeitar: danger decision with a mandatory note (opens the reason input).
        actions.Add(DecisionBarActionPresentation.Create(
            availability.CanReject
                ? SharedActionPresentation.CreateEnabled("reject", "Rejeitar", "A rejeitarâ€¦")
                : SharedActionPresentation.CreateDisabled("reject", "Rejeitar", availability.RejectDisabledReason!),
            DecisionBarActionGroup.Danger));

        // Reabrir: returns the record to the draft handoff (reason required).
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

    /// <summary>
    /// The Enviar-para-produção initiation affordance (Q-SEND, Â§23.2): visible ONLY on approved
    /// Pesos, explicit confirmed, NEVER automatic â€” and currently <b>unavailable-with-reason</b>
    /// because the sending workflow is P2-T08's contract (not yet authorized). Zero send
    /// mechanics and zero send persistence exist in P2-T06 (Â§23.4, BND-B2).
    /// </summary>
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

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DenseTableColumnPresentation Column(string key, string heading) =>
        DenseTableColumnPresentation.Create(key, heading, alignment: DenseTableColumnAlignment.Start);

    private static DenseTableFilterPresentation Filter(string key, string label, string? value) =>
        DenseTableFilterPresentation.Create(key, label, value ?? string.Empty);

    /// <summary>A row of the page-owned open-route map (opaque key â†’ exact review route).</summary>
    public sealed record ReviewRouteEntry(string Key, string Href);

    /// <summary>The review-sheet view state of the page (shared read model + trail + availability).</summary>
    public sealed record ReviewSheetView(
        PesoSheetReadModel Peso,
        IReadOnlyList<DecisionItemReadModel> Decisions,
        ReviewAvailability Availability);

    /// <summary>The Enviar-para-produção affordance state (Â§23.2).</summary>
    public sealed record SendAffordancePresentation(bool Visible, bool Enabled, string Reason);
}