using DMO.Application.Access;
using DMO.Application.Controlo.Approve;
using DMO.Application.JobOn;
using DMO.Application.Session;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints;

/// <summary>
/// Minimal API surface of Controlo Approve (P2-T06 contract §13.2 routes 3–9; routes 1–2 are the
/// Razor pages <c>Pages/Controlo/Approve/Index</c> + <c>Historico</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every route declares exactly ONE canonical Module policy:
/// <c>ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloApprove)</c> — no route carries
/// <c>controlo-create</c>, no route is anonymous, no policy is created, and the shared
/// <c>controlo</c> destination never merges the grants (§14).</para>
/// <para>
/// Approve/reject/reopen operate on the SAME <c>peso_id</c> with the closed command carriers
/// (identity/version [/reason]) — no Peso fact, no warning, no calculation output and no
/// actor/time are ever accepted (AC-D2/D4). The review reads compose the shared P2-T05
/// <c>PesoSheetReadModel</c> under the P2-T06 route. Every persisted-state refusal is a 409
/// distinguished by its reason token; no failure body ever contains a secret, a path, a
/// connection string or another user's data. There is no PDF/file/email/send/Definições/Folha/
/// per-CM route anywhere in this surface (§13.2 route-count statement).</para>
/// </remarks>
public static class ControloApproveEndpoints
{
    /// <summary>Base path of the Controlo Approve surface.</summary>
    public const string ControloApproveBasePath = "/controlo/approve";

    /// <summary>The canonical policy of every Controlo Approve route (the pinned-value pattern of the
    /// accepted <c>JobOnPolicyNames</c>/<c>ControloPolicyNames</c>; the Razor-page constants live in
    /// <c>DMO.Web.Pages.Controlo.Approve.ControloApprovePolicyNames</c>).</summary>
    public static string Policy { get; } =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloApprove);

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the Controlo Approve endpoints onto the application.</summary>
    public static WebApplication MapControloApproveEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(ControloApproveBasePath)
            .RequireAuthorization(Policy);

        // Route 3 — the pending/review list (the reviewable predicate query, filters in SQL).
        group.MapGet("/pending", async (
            [AsParameters] PendingListQuery query,
            IControloApproveService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetPendingAsync(query, cancellationToken);

            return result is ReviewResult.PendingFound(var rows, var total)
                ? Results.Ok(new PendingListResponse(
                    rows
                        .Select(row => new PendingItemResponse(
                            row.PesoId,
                            row.Version,
                            row.Status,
                            row.CmId,
                            row.ToolId,
                            row.Reference,
                            row.ProductionNumber,
                            row.Machine,
                            row.ToolReference,
                            row.Processo,
                            row.SubmittedAt,
                            row.SubmittedByUserId,
                            row.RowCount,
                            row.HasDecisionTrail))
                        .ToArray(),
                    total))
                : MapResult(result, logger);
        });

        // Route 4 — the local Histórico list (filters applied by the backend).
        group.MapGet("/history", async (
            [AsParameters] HistoryListQuery query,
            IControloApproveService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetHistoryAsync(query, cancellationToken);

            return result is ReviewResult.HistoryFound(var rows, var total)
                ? Results.Ok(new HistoryListResponse(
                    rows
                        .Select(row => new HistoryItemResponse(
                            row.PesoId,
                            row.Version,
                            row.Status,
                            row.CmId,
                            row.ToolId,
                            row.Reference,
                            row.ProductionNumber,
                            row.Machine,
                            row.ToolReference,
                            row.Processo,
                            row.SubmittedAt,
                            row.SubmittedByUserId,
                            row.LastDecision is { } last
                                ? new DecisionSummaryResponse(last.Decision, last.DecidedByUserId, last.DecidedAt, last.Reason)
                                : null,
                            row.DecisionCount))
                        .ToArray(),
                    total))
                : MapResult(result, logger);
        });

        // Route 5 — the exact-record review sheet: the SHARED PesoSheetReadModel + the decision
        // trail + the review availability (read-only projection; never writes, never bumps).
        group.MapGet("/pesos/{pesoId:guid}", async (
            Guid pesoId,
            IControloApproveService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetReviewSheetAsync(pesoId, cancellationToken);

            return result is ReviewResult.ReviewSheet(var sheet)
                ? Results.Ok(ToReviewSheetResponse(sheet))
                : MapResult(result, logger);
        });

        // Route 6 — the decision trail of the exact Peso (decided_at ASC).
        group.MapGet("/pesos/{pesoId:guid}/decisions", async (
            Guid pesoId,
            IControloApproveService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetDecisionsAsync(pesoId, cancellationToken);

            return result is ReviewResult.DecisionsFound(var decisions)
                ? Results.Ok(new DecisionsResponse(
                    decisions.Select(ToDecisionResponse).ToArray()))
                : MapResult(result, logger);
        });

        // Route 7 — human approve on the SAME peso_id (identity + version only).
        group.MapPost("/pesos/{pesoId:guid}/approve", async (
            Guid pesoId,
            ApprovePesoRequest? body,
            IControloApproveService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloApproveValidationErrors.FilterInvalid);
            }

            var command = new ApprovePesoCommand(pesoId, body.ExpectedVersion);

            return await ExecuteAsync(
                token => service.ApproveAsync(command, token),
                logger,
                cancellationToken);
        });

        // Route 8 — human reject with reason on the SAME peso_id.
        group.MapPost("/pesos/{pesoId:guid}/reject", async (
            Guid pesoId,
            RejectPesoRequest? body,
            IControloApproveService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloApproveValidationErrors.RejectReasonRequired);
            }

            var command = new RejectPesoCommand(pesoId, body.ExpectedVersion, body.Reason);

            return await ExecuteAsync(
                token => service.RejectAsync(command, token),
                logger,
                cancellationToken);
        });

        // Route 9 — human reopen with reason on the SAME peso_id (draft-editable handoff restored).
        group.MapPost("/pesos/{pesoId:guid}/reopen", async (
            Guid pesoId,
            ReopenPesoRequest? body,
            IControloApproveService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloApproveValidationErrors.ReopenReasonRequired);
            }

            var command = new ReopenPesoCommand(pesoId, body.ExpectedVersion, body.Reason);

            return await ExecuteAsync(
                token => service.ReopenAsync(command, token),
                logger,
                cancellationToken);
        });

        return app;
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> ExecuteAsync(
        Func<CancellationToken, Task<ReviewResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var result = await operation(cancellationToken);

        // Status-level observability only: never payloads, never secrets.
        if (result is ReviewResult.Refused(var reason, var message))
        {
            logger.LogWarning("Controlo Approve operation refused ({Reason}): {Message}", reason, message);
        }

        return MapResult(result, logger);
    }

    private static IResult MapResult(ReviewResult result, ILogger logger) => result switch
    {
        ReviewResult.PendingFound(var rows, var total) => Results.Ok(new PendingListResponse(
            rows
                .Select(row => new PendingItemResponse(
                    row.PesoId,
                    row.Version,
                    row.Status,
                    row.CmId,
                    row.ToolId,
                    row.Reference,
                    row.ProductionNumber,
                    row.Machine,
                    row.ToolReference,
                    row.Processo,
                    row.SubmittedAt,
                    row.SubmittedByUserId,
                    row.RowCount,
                    row.HasDecisionTrail))
                .ToArray(),
            total)),

        ReviewResult.HistoryFound(var rows, var total) => Results.Ok(new HistoryListResponse(
            rows
                .Select(row => new HistoryItemResponse(
                    row.PesoId,
                    row.Version,
                    row.Status,
                    row.CmId,
                    row.ToolId,
                    row.Reference,
                    row.ProductionNumber,
                    row.Machine,
                    row.ToolReference,
                    row.Processo,
                    row.SubmittedAt,
                    row.SubmittedByUserId,
                    row.LastDecision is { } last
                        ? new DecisionSummaryResponse(last.Decision, last.DecidedByUserId, last.DecidedAt, last.Reason)
                        : null,
                    row.DecisionCount))
                .ToArray(),
            total)),

        ReviewResult.ReviewSheet(var sheet) => Results.Ok(ToReviewSheetResponse(sheet)),

        ReviewResult.DecisionsFound(var decisions) => Results.Ok(new DecisionsResponse(
            decisions.Select(ToDecisionResponse).ToArray())),

        ReviewResult.Approved(var pesoId, var version, var decidedAt) =>
            Results.Ok(new DecisionAppliedResponse("aprovado", pesoId, version, decidedAt, null)),

        ReviewResult.Rejected(var pesoId, var version, var decidedAt) =>
            Results.Ok(new DecisionAppliedResponse("nao_aprovado", pesoId, version, decidedAt, null)),

        ReviewResult.Reopened(var pesoId, var version, var decidedAt) =>
            Results.Ok(new DecisionAppliedResponse("reaberto", pesoId, version, decidedAt, null)),

        ReviewResult.ValidationFailed(var errors) => ValidationFailed(errors),

        ReviewResult.NotFound(var pesoId) => Results.NotFound(new { reason = "not-found", pesoId }),

        ReviewResult.Refused(var reason, var message) =>
            Results.Conflict(new ReviewRefusalResponse(RefusalToken(reason), message)),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult ValidationFailed(params string[] errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    private static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    /// <summary>The exact transport token of a review refusal (contract §12.2).</summary>
    public static string RefusalToken(ReviewRefusalReason reason) => reason switch
    {
        ReviewRefusalReason.StaleVersion => "stale-version",
        ReviewRefusalReason.NotReviewable => "not-reviewable",
        ReviewRefusalReason.AlreadyDecided => "already-decided",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown refusal reason."),
    };

    private static ReviewSheetResponse ToReviewSheetResponse(ReviewSheetReadModel sheet) => new(
        new PesoSheetResponse(
            sheet.Peso.PesoId,
            sheet.Peso.Version,
            sheet.Peso.Status,
            sheet.Peso.CmId,
            sheet.Peso.ToolId,
            sheet.Peso.Context is { } context
                ? new PesoContextResponse(
                    context.CmId,
                    context.ToolId,
                    context.FrozenToolType,
                    context.FrozenToolReference,
                    context.FrozenToolLot,
                    ToToolProjection(context.Tool))
                : null,
            sheet.Peso.Pending is { } pending
                ? new PesoPendingResponse(pending.ToolId, ToToolProjection(pending.Tool))
                : null,
            sheet.Peso.Production is { } production
                ? new PesoProductionResponse(
                    production.Reference,
                    production.ProductionNumber,
                    production.Machine,
                    production.ProductionDate)
                : null,
            sheet.Peso.CreatedByUserId,
            sheet.Peso.CreatedAt,
            sheet.Peso.SubmittedByUserId,
            sheet.Peso.SubmittedAt,
            sheet.Peso.WaterTemperature,
            sheet.Peso.VolumeMarisaBq,
            sheet.Peso.VolumePuncaoPu,
            sheet.Peso.GlassDensityGCm3,
            sheet.Peso.PreviousProductionEndReference,
            sheet.Peso.PreviousAverageWeightReference,
            sheet.Peso.Rows
                .Select(row => new PesoRowResponse(
                    row.PesoMeasurementRowId,
                    row.RowPosition,
                    row.WaterWeightG,
                    row.CapacityCm3,
                    row.GlassWeightG))
                .ToArray()),
        sheet.Decisions.Select(ToDecisionResponse).ToArray(),
        new AvailabilityResponse(
            sheet.Availability.CanApprove,
            sheet.Availability.ApproveDisabledReason,
            sheet.Availability.CanReject,
            sheet.Availability.RejectDisabledReason,
            sheet.Availability.CanReopen,
            sheet.Availability.ReopenDisabledReason));

    private static DecisionItemResponse ToDecisionResponse(DecisionItemReadModel decision) => new(
        decision.PesoReviewDecisionId,
        decision.Decision,
        decision.DecidedByUserId,
        decision.DecidedAt,
        decision.Reason,
        decision.PriorStatus,
        decision.PesoVersionAtDecision);

    private static JobOnEndpoints.JobOnToolProjectionResponse ToToolProjection(ToolSummaryProjection tool) =>
        new(
            tool.ToolId,
            ToolTokens.ToToken(tool.Type),
            tool.Reference,
            tool.Lot,
            ToolTokens.ToToken(tool.Processo),
            tool.Quantity,
            tool.CompatibleMachines.Select(machine => machine.Value).ToArray());

    // ---------------------------------------------------------------------------------------------
    // Transport shapes (route table §13.2)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Route 7 approve carrier: identity + observed version only.</summary>
    public sealed record ApprovePesoRequest(int ExpectedVersion);

    /// <summary>Route 8 reject carrier: identity + observed version + reason.</summary>
    public sealed record RejectPesoRequest(int ExpectedVersion, string Reason);

    /// <summary>Route 9 reopen carrier: identity + observed version + reason.</summary>
    public sealed record ReopenPesoRequest(int ExpectedVersion, string Reason);

    /// <summary>Route 3 response.</summary>
    public sealed record PendingListResponse(IReadOnlyList<PendingItemResponse> Rows, int Total);

    /// <summary>One route 3 row.</summary>
    public sealed record PendingItemResponse(
        Guid PesoId,
        int Version,
        string Status,
        Guid? CmId,
        Guid? ToolId,
        string? Reference,
        string? ProductionNumber,
        string? Machine,
        string? ToolReference,
        string? Processo,
        DateTimeOffset SubmittedAt,
        Guid SubmittedByUserId,
        int RowCount,
        bool HasDecisionTrail);

    /// <summary>Route 4 response.</summary>
    public sealed record HistoryListResponse(IReadOnlyList<HistoryItemResponse> Rows, int Total);

    /// <summary>One route 4 row.</summary>
    public sealed record HistoryItemResponse(
        Guid PesoId,
        int Version,
        string Status,
        Guid? CmId,
        Guid? ToolId,
        string? Reference,
        string? ProductionNumber,
        string? Machine,
        string? ToolReference,
        string? Processo,
        DateTimeOffset? SubmittedAt,
        Guid? SubmittedByUserId,
        DecisionSummaryResponse? LastDecision,
        int DecisionCount);

    /// <summary>The last decision summary of a history row.</summary>
    public sealed record DecisionSummaryResponse(
        string Decision,
        Guid DecidedByUserId,
        DateTimeOffset DecidedAt,
        string? Reason);

    /// <summary>Route 5 response: the review sheet (shared PesoSheetReadModel + trail + availability).</summary>
    public sealed record ReviewSheetResponse(
        PesoSheetResponse Peso,
        IReadOnlyList<DecisionItemResponse> Decisions,
        AvailabilityResponse Availability);

    /// <summary>The shared Peso sheet transport (the §26.3 shape, composed — never duplicated).</summary>
    public sealed record PesoSheetResponse(
        Guid PesoId,
        int Version,
        string Status,
        Guid? CmId,
        Guid? ToolId,
        PesoContextResponse? Context,
        PesoPendingResponse? Pending,
        PesoProductionResponse? Production,
        Guid? CreatedByUserId,
        DateTimeOffset CreatedAt,
        Guid? SubmittedByUserId,
        DateTimeOffset? SubmittedAt,
        decimal WaterTemperature,
        decimal? VolumeMarisaBq,
        decimal? VolumePuncaoPu,
        decimal? GlassDensityGCm3,
        string? PreviousProductionEndReference,
        string? PreviousAverageWeightReference,
        IReadOnlyList<PesoRowResponse> Rows);

    /// <summary>The production-case context of the sheet.</summary>
    public sealed record PesoContextResponse(
        Guid CmId,
        Guid ToolId,
        string FrozenToolType,
        string FrozenToolReference,
        string FrozenToolLot,
        JobOnEndpoints.JobOnToolProjectionResponse Tool);

    /// <summary>The pending-case context of the sheet.</summary>
    public sealed record PesoPendingResponse(
        Guid ToolId,
        JobOnEndpoints.JobOnToolProjectionResponse Tool);

    /// <summary>The traversal production facts of the sheet (incl. the Job On production date).</summary>
    public sealed record PesoProductionResponse(
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate);

    /// <summary>One row of the sheet.</summary>
    public sealed record PesoRowResponse(
        Guid PesoMeasurementRowId,
        int RowPosition,
        decimal WaterWeightG,
        decimal CapacityCm3,
        decimal GlassWeightG);

    /// <summary>One decision-trail item (route 6 / embedded in the route 5 response).</summary>
    public sealed record DecisionItemResponse(
        Guid PesoReviewDecisionId,
        string Decision,
        Guid DecidedByUserId,
        DateTimeOffset DecidedAt,
        string? Reason,
        string PriorStatus,
        int PesoVersionAtDecision);

    /// <summary>The review availability of the sheet.</summary>
    public sealed record AvailabilityResponse(
        bool CanApprove,
        string? ApproveDisabledReason,
        bool CanReject,
        string? RejectDisabledReason,
        bool CanReopen,
        string? ReopenDisabledReason);

    /// <summary>Route 6 response.</summary>
    public sealed record DecisionsResponse(IReadOnlyList<DecisionItemResponse> Decisions);

    /// <summary>Routes 7–9 response: the applied decision outcome facts.</summary>
    public sealed record DecisionAppliedResponse(
        string Decision,
        Guid PesoId,
        int Version,
        DateTimeOffset DecidedAt,
        string? Message);

    /// <summary>Typed, actionable refusal response.</summary>
    public sealed record ReviewRefusalResponse(string Reason, string Message);
}