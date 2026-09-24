using DMO.Application.ControloCreate;
using DMO.Application.JobOn;
using DMO.Application.Session;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.Web.Authorization;
using DMO.Web.Frontend.Shared.Contracts;
using DMO.Web.Frontend.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMO.Web.Pages.Controlo;

/// <summary>
/// Novo controlo surface: Peso create/measurement/submission (P2-T05 contract §8.3 regions R1–R8).
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §8 (UI contract), §4 (selection), §7 (create/edit/submit). The page
/// is server-gated <c>controlo-create</c>; every region is rendered from REAL backend reads
/// (production selection §4.2, CM context §4.3, the published Peso sheet for an open draft), never
/// reconstructed from form text. The page is designed at the binding 1366 × 768 fixed desktop
/// (§24) with the region-stable composition of §8.3.
/// <para>
/// The mutating actions (Calcular / Guardar / Submeter / Associar) are executed by the page-owned
/// adapter (<c>dmo-controlo.js</c>) against the accepted minimal-API routes; backend validation is
/// authoritative and entered values are retained on refusal (§8.5). No autosave exists (S2).</para>
/// </remarks>
[Authorize(Policy = ControloPolicyNames.ControloCreate)]
public sealed class CreateModel : PageModel
{
    private const string ShellTitle = "Novo controlo";
    private const string ShellContext = "Peso — criação/medição/submissão";

    private readonly IControloCreateService _controlo;
    private readonly IJobOnService _jobOns;
    private readonly IProductionResumoRead _productionResumo;
    private readonly ICurrentAccountContext _currentAccount;
    private readonly ShellPresentationService _shell;
    private readonly ILogger<CreateModel> _logger;

    /// <summary>Creates the page over the Controlo service, the composed Job On reads and the shell.</summary>
    public CreateModel(
        IControloCreateService controlo,
        IJobOnService jobOns,
        IProductionResumoRead productionResumo,
        ICurrentAccountContext currentAccount,
        ShellPresentationService shell,
        ILogger<CreateModel> logger)
    {
        ArgumentNullException.ThrowIfNull(controlo);
        ArgumentNullException.ThrowIfNull(jobOns);
        ArgumentNullException.ThrowIfNull(productionResumo);
        ArgumentNullException.ThrowIfNull(currentAccount);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(logger);
        _controlo = controlo;
        _jobOns = jobOns;
        _productionResumo = productionResumo;
        _currentAccount = currentAccount;
        _shell = shell;
        _logger = logger;
    }

    /// <summary>R2 — reference submitted for the productions lookup.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Reference { get; set; }

    /// <summary>R2/R3 — the explicitly selected production occurrence.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? JobOnId { get; set; }

    /// <summary>Open an existing draft Peso (route 1 <c>?pesoId=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? PesoId { get; set; }

    /// <summary>Whether the productions lookup did not complete (never aliased to empty).</summary>
    public bool LookupFailed { get; private set; }

    /// <summary>The exact contracted validation codes of a refused lookup.</summary>
    public IReadOnlyList<string> ValidationErrors { get; private set; } = [];

    /// <summary>R2 — the productions decision table, or <c>null</c> when no lookup was performed.</summary>
    public DenseTablePresentation? Productions { get; private set; }

    /// <summary>The page-owned opaque row-key → jobon route map of the productions table.</summary>
    public IReadOnlyList<ProductionRouteEntry> ProductionRoutes { get; private set; } = [];

    /// <summary>R1 — the read-only production context strip (real backend facts; never editable).</summary>
    public ProductionStripModel? Strip { get; private set; }

    /// <summary>R3 — the CM context presentation of the selected occurrence.</summary>
    public CmContextRegionModel? CmContext { get; private set; }

    /// <summary>The opened draft Peso sheet (real persisted facts; <c>null</c> in create mode).</summary>
    public PesoSheetReadModel? Draft { get; private set; }

    /// <summary>R4/R5 — the measurement rows region (P2-T03 mechanics, at-least-one).</summary>
    public MeasurementRowsPresentation Rows { get; private set; } = null!;

    /// <summary>R5 — the per-row results table (individual results first-class; derived summaries).</summary>
    public PesoResultsPresentation? Results { get; private set; }

    /// <summary>R6 — the decision bar (Calcular / Guardar / Submeter / Cancelar).</summary>
    public DecisionBarPresentation Actions { get; private set; } = null!;

    /// <summary>R7 — the canonical Peso status presentation.</summary>
    public RecordStatusPresentation Status { get; private set; } = null!;

    /// <summary>Whether the draft is already submitted (Create-side mutations closed).</summary>
    public bool IsSubmitted { get; private set; }

    /// <summary>R3 — association candidates of the pending anchor (route 4 read), paired with their
    /// opaque keys.</summary>
    public IReadOnlyList<CandidateEntry> AssociationCandidates { get; private set; } = [];

    /// <summary>The observed version carried to the mutating routes (real Peso version, else 1).</summary>
    public int ObservedVersion { get; private set; } = 1;

    /// <inheritdoc />
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var current = await _currentAccount.GetCurrentAsync(cancellationToken);
        ViewData["DmoShell"] = await _shell.BuildAsync(current, ShellTitle, ShellContext, cancellationToken);

        Reference = Reference?.Trim();

        if (PesoId is { } pesoId)
        {
            await LoadDraftAsync(pesoId, cancellationToken);
            return;
        }

        if (JobOnId is { } jobOnId)
        {
            await LoadProductionAsync(jobOnId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(Reference))
        {
            await LoadProductionsAsync(Reference, cancellationToken);
        }

        BuildCreateRegions();
    }

    private async Task LoadProductionsAsync(string reference, CancellationToken cancellationToken)
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
                    break;

                case JobOnResult.ProductionsFound(var productions):
                    BuildProductions(productions);
                    break;

                case JobOnResult.Refused(var reason, var message, _, _):
                    _logger.LogWarning("Productions lookup refused ({Reason}): {Message}", reason, message);
                    LookupFailed = true;
                    break;

                default:
                    LookupFailed = true;
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Productions lookup failed.");
            LookupFailed = true;
        }
    }

    private void BuildProductions(IReadOnlyList<JobOnProductionListItem> productions)
    {
        var columns = new List<DenseTableColumnPresentation>
        {
            DenseTableColumnPresentation.Create("reference", "Referência"),
            DenseTableColumnPresentation.Create("production-number", "Número de produção", widthHint: DenseTableColumnWidthHint.Compact),
            DenseTableColumnPresentation.Create("machine", "Máquina", widthHint: DenseTableColumnWidthHint.Compact),
            DenseTableColumnPresentation.Create("production-date", "Data de produção", widthHint: DenseTableColumnWidthHint.Compact),
        };

        var rows = new List<DenseTableRowPresentation>(productions.Count);
        var routes = new List<ProductionRouteEntry>(productions.Count);

        for (var index = 0; index < productions.Count; index++)
        {
            var production = productions[index];
            var key = $"p-{index}";

            // The row key is opaque; the canonical jobon identity is resolved through this page's
            // own route map, never by parsing the key.
            routes.Add(new ProductionRouteEntry(key, $"/controlo/create?jobonId={production.JobOnId}"));

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

        // Every occurrence is listed; the operator selects ONE explicitly, never automatically,
        // even when exactly one row is returned (§4.2 rule 1).
        Productions = DenseTablePresentation.Create(
            rows.Count > 0 ? CommonState.Ready : CommonState.Empty,
            "Produções da referência",
            columns,
            rows,
            selectionEnabled: true,
            openEnabled: false,
            message: rows.Count > 0 ? null : "Não existem produções para esta referência.",
            resultSummary: $"{rows.Count} produção(ões) encontradas.",
            actionsColumnHeading: "Selecionar",
            regionLabel: "Produções");
    }

    /// <summary>
    /// R1/R3 — the production entry through the Resumo da produção (P2-T05 contract §31.1): the
    /// occurrence's OWN light projection — production facts + the CM context the Peso surface is
    /// populated from (reference, machine, lot, processo, CM identity via the real cm_id/tool_id).
    /// No full-ficha load, no second production model; a missing occurrence is a lookup failure,
    /// never an empty surface.
    /// </summary>
    private async Task LoadProductionAsync(Guid jobOnId, CancellationToken cancellationToken)
    {
        var resumo = await _productionResumo.GetResumoAsync(jobOnId, cancellationToken);

        if (resumo is null)
        {
            LookupFailed = true;
            return;
        }

        Strip = new ProductionStripModel(
            resumo.Reference,
            resumo.ProductionNumber,
            resumo.Machine,
            resumo.ProductionDate,
            resumo.Cm?.Processo is { } processo ? ToolTokens.ToToken(processo) : null);

        if (resumo.Cm is { } cm)
        {
            CmContext = new CmContextRegionModel(
                JobOnId: resumo.JobOnId,
                FichaVersion: resumo.Version,
                CmContextId: cm.CmId,
                ToolId: cm.ToolId,
                FrozenType: cm.FrozenToolType,
                cm.FrozenToolReference,
                cm.FrozenToolLot,
                BuildToolSummary(cm));
        }
        else
        {
            // No CM context on this occurrence: the region shows the missing-context recovery
            // (the truthful empty, never an invented context).
            CmContext = new CmContextRegionModel(
                JobOnId: resumo.JobOnId,
                FichaVersion: resumo.Version,
                CmContextId: null,
                ToolId: null,
                FrozenType: null,
                FrozenReference: null,
                FrozenLot: null,
                Tool: null);
        }

        AssociationCandidates = await LoadCandidatesAsync(resumo.Cm?.ToolId, cancellationToken);
    }

    private ToolSummaryRowPresentation BuildToolSummary(CmResumoProjection cm) =>
        ToolSummaryRowPresentation.Create(
            CommonState.Ready,
            cm.ToolId.ToString(),
            type: ToolSummaryFactPresentation.Create("Tipo", cm.FrozenToolType),
            reference: ToolSummaryFactPresentation.Create("Referência", cm.LiveToolReference),
            lot: ToolSummaryFactPresentation.Create("Lote", cm.LiveToolLot),
            process: ToolSummaryFactPresentation.Create("Processo", ToolTokens.ToToken(cm.Processo) ?? "—"),
            quantity: ToolSummaryFactPresentation.Create("Quantidade", cm.Quantity?.ToString() ?? "—"));

    private async Task<IReadOnlyList<CandidateEntry>> LoadCandidatesAsync(
        Guid? toolId,
        CancellationToken cancellationToken)
    {
        if (toolId is not { } id)
        {
            return [];
        }

        var result = await _controlo.ListAssociationCandidatesAsync(id, cancellationToken);

        if (result is not PesoResult.Candidates(var candidates))
        {
            return [];
        }

        return candidates
            .Select((candidate, index) => new CandidateEntry(
                $"c-{index}",
                candidate.CmContextId,
                candidate.JobOnId,
                candidate.Reference,
                candidate.ProductionNumber,
                candidate.Machine))
            .ToList();
    }

    private async Task LoadDraftAsync(Guid pesoId, CancellationToken cancellationToken)
    {
        var result = await _controlo.GetAsync(pesoId, cancellationToken);

        if (result is not PesoResult.Found(var sheet))
        {
            LookupFailed = true;
            BuildCreateRegions();
            return;
        }

        Draft = sheet;
        IsSubmitted = sheet.SubmittedAt is not null;
        ObservedVersion = sheet.Version;
        Strip = sheet.Production is { } production
            ? new ProductionStripModel(
                production.Reference,
                production.ProductionNumber,
                production.Machine,
                ProductionDate: null,
                Processo: sheet.Context?.Tool.Processo is { } processo ? ToolTokens.ToToken(processo) : null)
            : null;
        CmContext = sheet.Context is { } context
            ? new CmContextRegionModel(
                JobOnId: null,
                FichaVersion: sheet.Version,
                CmContextId: context.CmId,
                context.ToolId,
                context.FrozenToolType,
                context.FrozenToolReference,
                context.FrozenToolLot,
                ToolSummaryRowPresentation.Create(
                    CommonState.Ready,
                    context.Tool.ToolId.ToString(),
                    type: ToolSummaryFactPresentation.Create("Tipo", ToolTokens.ToToken(context.Tool.Type)),
                    reference: ToolSummaryFactPresentation.Create("Referência", context.Tool.Reference),
                    lot: ToolSummaryFactPresentation.Create("Lote", context.Tool.Lot),
                    process: ToolSummaryFactPresentation.Create(
                        "Processo", ToolTokens.ToToken(context.Tool.Processo) ?? "—")))
            : sheet.Pending is { } pending
                ? new CmContextRegionModel(
                    JobOnId: null,
                    FichaVersion: sheet.Version,
                    CmContextId: null,
                    pending.ToolId,
                    FrozenType: null,
                    FrozenReference: null,
                    FrozenLot: null,
                    ToolSummaryRowPresentation.Create(
                        CommonState.Ready,
                        pending.Tool.ToolId.ToString(),
                        type: ToolSummaryFactPresentation.Create("Tipo", ToolTokens.ToToken(pending.Tool.Type)),
                        reference: ToolSummaryFactPresentation.Create("Referência", pending.Tool.Reference),
                        lot: ToolSummaryFactPresentation.Create("Lote", pending.Tool.Lot)))
                : null;

        AssociationCandidates = await LoadCandidatesAsync(sheet.ToolId, cancellationToken);

        BuildDraftRegions(sheet);
    }

    private void BuildDraftRegions(PesoSheetReadModel sheet)
    {
        var rows = sheet.Rows
            .Select((row, index) => MeasurementRowPresentation.Create(
                $"r-{index}",
                $"Leitura {row.RowPosition}",
                [
                    MeasurementRowFieldPresentation.Create(
                        "weight",
                        "Peso de água (g)",
                        row.WaterWeightG.ToString("0.####"),
                        MeasurementRowFieldKind.Numeric),
                ]))
            .ToList();

        // The status token is the backend fact (the canonical §3.1 vocabulary): a submitted record
        // is either awaiting the decision (pendente) or already decided (aprovado / nao_aprovado).
        // Controlo_Create STATES the result of the decision on the same peso_id clearly in every
        // case — a decided record stays read-only here and the app informs (never blocks): the
        // correction of a rejected record returns through Approve's Reabrir, which restores this
        // editable draft handoff.
        var status = PesoStatusTokens.Parse(sheet.Status);
        var awaitingDecision = sheet.SubmittedAt is not null && (status is null or PesoStatus.Pendente);
        var decided = status is PesoStatus.Aprovado or PesoStatus.NaoAprovado;
        var readOnly = awaitingDecision || decided;

        Rows = MeasurementRowsPresentation.Create(
            readOnly ? CommonState.Conflict : CommonState.Ready,
            "Leituras de peso de água",
            minimumRowCount: 1,
            rows,
            minimumViolationReason: "É necessário pelo menos uma leitura válida.",
            structuralMutationAllowed: !readOnly,
            structuralMutationReason: readOnly ? RowsReason(status) : null,
            message: readOnly ? RowsMessage(status) : null);

        Results = new PesoResultsPresentation(sheet.Rows.ToList(), sheet.GlassDensityGCm3);

        Status = decided
            ? RecordStatusPresentation.Create(
                PesoStatusTokens.DisplayLabel(status!.Value),
                tone: status == PesoStatus.Aprovado ? StatusTone.Success : StatusTone.Danger,
                assistiveDescription: DecidedHint(status.Value))
            : RecordStatusPresentation.Create(
                awaitingDecision ? "Pendente — submetido para aprovação" : "Pendente",
                tone: StatusTone.Neutral);

        Actions = readOnly
            ? DecisionBarPresentation.Create(
                CommonState.Conflict,
                [
                    DecisionBarActionPresentation.Create(
                        SharedActionPresentation.CreateDisabled(
                            "submit",
                            decided ? PesoStatusTokens.DisplayLabel(status!.Value) : "Submetido",
                            decided ? SubmitReason(status!.Value) : "Este Peso já foi submetido para aprovação."),
                        DecisionBarActionGroup.Primary),
                ],
                message: decided
                    ? SubmitMessage(status!.Value)
                    : "Este Peso já foi submetido para aprovação; a correção pertence ao fluxo de aprovação.")
            : DecisionBarPresentation.Create(
                CommonState.Ready,
                [
                    DecisionBarActionPresentation.Create(
                        SharedActionPresentation.CreateEnabled("calculate", "Calcular", "A calcular…"),
                        DecisionBarActionGroup.Primary),
                    DecisionBarActionPresentation.Create(
                        SharedActionPresentation.CreateEnabled("save", "Guardar", "A guardar…"),
                        DecisionBarActionGroup.Primary),
                    DecisionBarActionPresentation.Create(
                        SharedActionPresentation.CreateEnabled("submit", "Submeter para aprovação", "A submeter…"),
                        DecisionBarActionGroup.Primary),
                    DecisionBarActionPresentation.Create(
                        SharedActionPresentation.CreateEnabled("cancel", "Cancelar", null),
                        DecisionBarActionGroup.Secondary),
                ]);
    }

    /// <summary>The rows-region read-only reason of a submitted/decided record (§8.6 presentation).</summary>
    private static string RowsReason(PesoStatus? status) => status switch
    {
        PesoStatus.Aprovado => "Este Peso foi aprovado; as leituras são apresentadas em modo de leitura.",
        PesoStatus.NaoAprovado => "Este Peso não foi aprovado; as leituras são apresentadas em modo de leitura.",
        _ => "Este Peso já foi submetido para aprovação; as leituras são apresentadas em modo de leitura.",
    };

    /// <summary>The rows-region message of a submitted/decided record (§8.6 presentation).</summary>
    private static string RowsMessage(PesoStatus? status) => status switch
    {
        PesoStatus.Aprovado => "Este Peso foi aprovado; nenhuma edição é possível em Controlo_Create.",
        PesoStatus.NaoAprovado => "Este Peso não foi aprovado; aguarde o Reabrir em Aprovar para corrigir.",
        _ => "Este Peso já foi submetido para aprovação.",
    };

    /// <summary>The assistive description of a decided status (result stated, never colour-only).</summary>
    private static string DecidedHint(PesoStatus status) => status == PesoStatus.Aprovado
        ? "Este Peso foi aprovado; a decisão está registada em Aprovar."
        : "Este Peso não foi aprovado; depois de Reabrir em Aprovar pode corrigir e submeter novamente.";

    /// <summary>The disabled-submit reason of a decided record (the result, plain and clear).</summary>
    private static string SubmitReason(PesoStatus status) => status == PesoStatus.Aprovado
        ? "Este Peso foi aprovado; não existem mais ações de edição em Controlo_Create."
        : "Este Peso não foi aprovado; a correção é feita pelo responsável através de Reabrir em Aprovar.";

    /// <summary>The decision-bar message of a decided record (inform; no artificial block).</summary>
    private static string SubmitMessage(PesoStatus status) => status == PesoStatus.Aprovado
        ? "Este Peso foi aprovado. O resultado da decisão é apresentado aqui; o histórico completo está em Aprovar."
        : "Este Peso não foi aprovado. Depois de Reabrir em Aprovar, pode corrigir e submeter novamente a partir daqui.";

    private void BuildCreateRegions()
    {
        Rows = MeasurementRowsPresentation.Create(
            CommonState.Ready,
            "Leituras de peso de água",
            minimumRowCount: 1,
            [
                MeasurementRowPresentation.Create(
                    "r-0",
                    "Leitura 1",
                    [
                        MeasurementRowFieldPresentation.Create(
                            "weight",
                            "Peso de água (g)",
                            string.Empty,
                            MeasurementRowFieldKind.Numeric),
                    ]),
            ],
            minimumViolationReason: "É necessário pelo menos uma leitura válida.");

        Results = null;

        Status = RecordStatusPresentation.Create("Pendente", tone: StatusTone.Neutral);

        Actions = DecisionBarPresentation.Create(
            CommonState.Ready,
            [
                DecisionBarActionPresentation.Create(
                    SharedActionPresentation.CreateEnabled("calculate", "Calcular", "A calcular…"),
                    DecisionBarActionGroup.Primary),
                DecisionBarActionPresentation.Create(
                    SharedActionPresentation.CreateEnabled("save", "Guardar", "A guardar…"),
                    DecisionBarActionGroup.Primary),
                DecisionBarActionPresentation.Create(
                    SharedActionPresentation.CreateEnabled("submit", "Submeter para aprovação", "A submeter…"),
                    DecisionBarActionGroup.Primary),
                DecisionBarActionPresentation.Create(
                    SharedActionPresentation.CreateEnabled("cancel", "Cancelar", null),
                    DecisionBarActionGroup.Secondary),
            ]);
    }
}

/// <summary>One page-owned opaque row-key → route entry of the productions table.</summary>
public sealed record ProductionRouteEntry(string Key, string Href);

/// <summary>R1 — the read-only production context strip facts (real backend reads only).</summary>
public sealed record ProductionStripModel(
    string Reference,
    string ProductionNumber,
    string Machine,
    DateOnly? ProductionDate,
    string? Processo);

/// <summary>R3 — the CM context region: determined context, pending condition or missing-CM recovery.</summary>
public sealed record CmContextRegionModel(
    Guid? JobOnId,
    int FichaVersion,
    Guid? CmContextId,
    Guid? ToolId,
    string? FrozenType,
    string? FrozenReference,
    string? FrozenLot,
    ToolSummaryRowPresentation? Tool)
{
    /// <summary>Whether the region shows the truthful pending condition (<c>Job On por associar</c>).</summary>
    public bool IsPending => Tool is not null && CmContextId is null;

    /// <summary>Whether the selected occurrence has NO CM context (missing-context recovery).</summary>
    public bool HasNoContext => Tool is null;
}

/// <summary>R3 — one pending-association candidate (opaque key + real cm_id facts).</summary>
public sealed record CandidateEntry(
    string Key,
    Guid CmContextId,
    Guid JobOnId,
    string Reference,
    string ProductionNumber,
    string Machine);

/// <summary>R5 — the per-row results presentation (individual results first-class; derived
/// averages/deviations never hide an individual result, §5.3/AC-M8).</summary>
public sealed record PesoResultsPresentation(
    IReadOnlyList<PesoRowReadModel> Rows,
    decimal? GlassDensityGCm3)
{
    /// <summary>The derived average of the entered water weights (never stored).</summary>
    public decimal? AverageWaterWeightG =>
        Rows.Count == 0 ? null : Rows.Average(row => row.WaterWeightG);

    /// <summary>The derived average capacity (never stored).</summary>
    public decimal? AverageCapacityCm3 =>
        Rows.Count == 0 ? null : Rows.Average(row => row.CapacityCm3);

    /// <summary>The derived average glass weight (never stored).</summary>
    public decimal? AverageGlassWeightG =>
        Rows.Count == 0 ? null : Rows.Average(row => row.GlassWeightG);

    /// <summary>The derived per-row deviation from the average capacity in cm³ (never stored).</summary>
    public decimal DeviationCm3(PesoRowReadModel row) =>
        AverageCapacityCm3 is { } average ? row.CapacityCm3 - average : 0;

    /// <summary>The derived per-row deviation from the average capacity in % (never stored).</summary>
    public decimal? DeviationPercent(PesoRowReadModel row) =>
        AverageCapacityCm3 is { } average && average != 0
            ? (row.CapacityCm3 - average) / average * 100
            : null;
}