using DMO.Application.Access;
using DMO.Application.Boquilhas;
using DMO.Application.JobOn;
using DMO.Application.Session;
using DMO.Domain.Tools;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints.Boquilhas;

/// <summary>
/// Minimal API surface of Boquilhas — the production movement register (P2-T07 OWNER
/// CLARIFICATION routes 1–12 + the §34 pré-JobOn association routes; the four pages are
/// <c>Pages/Boquilhas/Index</c> + <c>Novo</c> + <c>Historico</c> + <c>Definicoes</c>, whose
/// endpoints live in <c>BoquilhasDefinicoesEndpoints</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every route declares exactly ONE canonical Module policy —
/// <c>ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.Boquilhas)</c> = <c>dmo.module.boquilhas</c>.
/// No route carries a second policy; a grant to any sibling module never satisfies a P2-T07 route;
/// direct-route denial is server-side; ADMIN gains no operational access.</para>
/// <para>
/// <b>Superseded (Owner clarification):</b> the close/reopen routes, the opening-facts route and
/// the PERMANENT standalone-anchored create branch are REMOVED — there is no lifecycle state
/// machine and no permanent standalone flow; the register creation carries no quantity. The §34
/// delta adds the TRANSITIONAL pré-JobOn branch (the create carrier <c>pending_tool_id</c>), the
/// human-confirmed association surface (candidates read + associate write) and the
/// Job-On-incoming pending-registers light packet. The final route matrix is recalculated
/// (15 endpoints + 4 pages = 19 in this file's surface family), NOT preserved at the old count.</para>
/// </remarks>
public static class BoquilhasEndpoints
{
    /// <summary>Base path of the Boquilhas surface.</summary>
    public const string BoquilhasBasePath = "/boquilhas";

    /// <summary>
    /// The canonical policy of every Boquilhas route (the pinned-value pattern of the accepted
    /// <c>JobOnPolicyNames</c>/<c>ControloPolicyNames</c>; the Razor-page constants live in
    /// <c>DMO.Web.Pages.Boquilhas.BoquilhasPolicyNames</c>).
    /// </summary>
    public static string Policy { get; } =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.Boquilhas);

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the Boquilhas endpoints onto the application.</summary>
    public static WebApplication MapBoquilhasEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(BoquilhasBasePath)
            .RequireAuthorization(Policy);

        // Route 1 — the register list (production context + derived outstanding; filters; paging).
        group.MapGet("/registers", async (
            string? reference,
            string? lot,
            int? page,
            int? pageSize,
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var query = new BoquilhasListQuery(reference, lot, page ?? 1, pageSize ?? 50);

            var result = await service.GetListAsync(query, cancellationToken);

            return result is BoquilhasResult.ListFound(var rows, var total)
                ? Results.Ok(new RegisterListResponse(
                    rows.Select(row => new RegisterItemResponse(
                        row.BoquilhasId,
                        row.BqId,
                        row.ToolId,
                        row.IsPending,
                        row.Reference,
                        row.Lot,
                        row.ProductionNumber,
                        row.ProductionMachine,
                        row.ProductionDate,
                        row.Outstanding,
                        row.MovementCount,
                        row.LastMovementAt))
                        .ToArray(),
                    total))
                : MapResult(result);
        });

        // Route 2 — the register ficha: production context + movement ledger + derived outstanding.
        group.MapGet("/registers/{boquilhasId:guid}", async (
            Guid boquilhasId,
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(boquilhasId, cancellationToken);

            return result is BoquilhasResult.Ficha(var ficha)
                ? Results.Ok(new { ficha = ToFicha(ficha) })
                : MapResult(result);
        });

        // Route 3 — the per-movement edit/audit trail (edited_at ASC).
        group.MapGet("/registers/{boquilhasId:guid}/movements/{movementId:guid}/audit", async (
            Guid boquilhasId,
            Guid movementId,
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetMovementAuditAsync(boquilhasId, movementId, cancellationToken);

            return result is BoquilhasResult.MovementAuditFound(var movement, var entries)
                ? Results.Ok(new MovementAuditResponse(
                    movement,
                    entries.Select(ToAuditItem).ToArray()))
                : MapResult(result);
        });

        // Route 4 — create the register IDENTITY (NO quantity event): EXACTLY ONE anchor — the
        // REAL production BQ context (bqId) XOR the transitional pré-JobOn canonical BQ Tool
        // (pendingToolId, §34.1 rule 1).
        group.MapPost("/registers", async (
            CreateRegisterRequest? body,
            IBoquilhasService service,
            ICurrentAccountContext currentAccount,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(BoquilhasValidationErrors.AnchorConflict);
            }

            var account = await currentAccount.GetCurrentAsync(cancellationToken);

            if (account is not CurrentAccount.User(var user))
            {
                return MapResult(new BoquilhasResult.ValidationFailed(
                    [BoquilhasValidationErrors.FilterInvalid]));
            }

            var result = await service.CreateAsync(
                new CreateBoquilhaRegisterCommand(body.BqId, body.PendingToolId, user.AccountId),
                cancellationToken);

            return await ExecuteAsync(
                result,
                logger,
                success: r => r is BoquilhasResult.RegisterCreated(var id)
                    ? Results.Created(
                        $"{BoquilhasBasePath}/registers/{id}",
                        new RegisterCreatedResponse(id))
                    : null);
        });

        // §34.1 rule 2 — the association candidates of a PENDING register: every REAL
        // bq_contexts row whose tool_id equals the register's provisional anchor, with the REAL
        // Job On production facts (register-side context; never auto-selected).
        group.MapGet("/registers/{boquilhasId:guid}/association-candidates", async (
            Guid boquilhasId,
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAssociationCandidatesAsync(boquilhasId, cancellationToken);

            return result is BoquilhasResult.AssociationCandidatesFound(var candidates)
                ? Results.Ok(new AssociationCandidatesResponse(
                    candidates.Select(candidate => new AssociationCandidateResponse(
                        candidate.BqId,
                        candidate.JobOnId,
                        candidate.Reference,
                        candidate.ProductionNumber,
                        candidate.Machine,
                        candidate.ProductionDate))
                        .ToArray()))
                : MapResult(result);
        });

        // §34.1 rules 2–3 — the HUMAN-CONFIRMED association: the same boquilhas_id binds to the
        // explicit candidate bq_id (whose bq_contexts.tool_id must equal the pending anchor — the
        // same-UUID proof). Typed refusals: already-associated / association-mismatch /
        // stale-version; history untouched.
        group.MapPost("/registers/{boquilhasId:guid}/associate", async (
            Guid boquilhasId,
            AssociateRegisterRequest? body,
            IBoquilhasService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(BoquilhasValidationErrors.BqContextNotFound);
            }

            var result = await service.AssociateAsync(
                new AssociateBoquilhasCommand(boquilhasId, body.BqId, body.ExpectedVersion),
                cancellationToken);

            return await ExecuteAsync(
                result,
                logger,
                success: r => r is BoquilhasResult.Associated(var associatedId, var version, var bqId)
                    ? Results.Ok(new AssociatedResponse(associatedId, version, bqId))
                    : null);
        });

        // §34.1 rule 2 — the Job-On-incoming direction: the pending registers of the production's
        // canonical Tool (bq_id → tool_id; ONE context-specific query, never a global scan).
        group.MapGet("/pending-registers", async (
            Guid? bqId,
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            if (bqId is not { } resolved)
            {
                return ValidationFailed(BoquilhasValidationErrors.BqContextNotFound);
            }

            var result = await service.GetPendingRegistersAsync(resolved, cancellationToken);

            return result is BoquilhasResult.PendingRegistersFound(var registers)
                ? Results.Ok(new PendingRegistersResponse(
                    registers.Select(register => new PendingRegisterResponse(
                        register.BoquilhasId,
                        register.ToolId,
                        register.ToolReference,
                        register.ToolLot,
                        register.Version,
                        register.CreatedAt,
                        register.MovementCount,
                        register.Outstanding))
                        .ToArray()))
                : MapResult(result);
        });

        // Route 5 — append one movement (Saída / Entrada / Entrada sem reparação).
        group.MapPost("/registers/{boquilhasId:guid}/movements", async (
            Guid boquilhasId,
            AppendMovementRequest? body,
            IBoquilhasService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(BoquilhasValidationErrors.MovementTypeInvalid);
            }

            var command = new AppendMovementCommand(
                boquilhasId,
                body.MovementType,
                body.Quantity,
                body.BusinessDate,
                body.Machine,
                body.RepairerId,
                body.Observations);

            var result = await service.AppendMovementAsync(command, cancellationToken);

            return await ExecuteAsync(
                result,
                logger,
                success: r => r is BoquilhasResult.MovementAppended(var movementId, var version)
                    ? Results.Created(
                        $"{BoquilhasBasePath}/registers/{boquilhasId}/movements/{movementId}",
                        new MovementAppliedResponse(movementId, version))
                    : null);
        });

        // Route 6 — edit the existing movement + audit (same row; no second quantity event).
        group.MapPut("/registers/{boquilhasId:guid}/movements/{movementId:guid}", async (
            Guid boquilhasId,
            Guid movementId,
            EditMovementRequest? body,
            IBoquilhasService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(BoquilhasValidationErrors.QuantityNotPositive);
            }

            var command = new EditMovementCommand(
                boquilhasId,
                movementId,
                body.ExpectedMovementVersion,
                body.Quantity,
                body.BusinessDate,
                body.Machine,
                body.RepairerId,
                body.Observations);

            var result = await service.EditMovementAsync(command, cancellationToken);

            return await ExecuteAsync(
                result,
                logger,
                success: r => r is BoquilhasResult.MovementEdited(var editedMovementId, var version)
                    ? Results.Ok(new MovementEditedResponse(editedMovementId, version))
                    : null);
        });

        // Route 7 — reference → productions for the association flow (explicit selection).
        group.MapGet("/productions", async (
            string? reference,
            IBoquilhasService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return ValidationFailed(BoquilhasValidationErrors.ReferenceRequired);
            }

            var result = await service.FindProductionsAsync(
                new FindProductionsQuery(reference.Trim()),
                cancellationToken);

            return result is BoquilhasResult.ProductionsFound(var productions)
                ? Results.Ok(new ProductionsResponse(
                    productions.Select(production => new ProductionItemResponse(
                        production.JobOnId,
                        production.Reference,
                        production.ProductionNumber,
                        production.Machine,
                        production.ProductionDate))
                        .ToArray()))
                : MapResult(result, logger);
        });

        // Route 8 — Job On ficha read (production facts + BQ context presence).
        group.MapGet("/jobons/{jobonId:guid}", async (
            Guid jobonId,
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetJobOnAsync(jobonId, cancellationToken);

            return result is BoquilhasResult.JobOnFichaFound(var ficha)
                ? Results.Ok(new { ficha = ToJobOnFicha(ficha) })
                : MapResult(result);
        });

        // Route 9 — create the MISSING BQ context through the Job On application contract
        // (Keep every fact + Set the BQ slot): the bq_contexts row is created by Job On's own
        // application/repository code, never by a Boquilhas repository.
        group.MapPost("/jobons/{jobonId:guid}/bq-association", async (
            Guid jobonId,
            BqAssociationRequest? body,
            IBoquilhasService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(BoquilhasValidationErrors.BqContextNotFound);
            }

            var result = await service.AssociateBqAsync(
                new AssociateBqCommand(jobonId, body.ToolId, body.ExpectedJobOnVersion),
                cancellationToken);

            return await ExecuteAsync(
                result,
                logger,
                success: r => r is BoquilhasResult.BqAssociated(var associatedJobOnId, var bqId, var version)
                    ? Results.Created(
                        $"{BoquilhasBasePath}/jobons/{associatedJobOnId}",
                        new BqAssociatedResponse(associatedJobOnId, bqId, version))
                    : null);
        });

        // Route 10 — consumed read: the six current machine → repairer assignments.
        group.MapGet("/machine-assignments", async (
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetMachineAssignmentsAsync(cancellationToken);

            return result is BoquilhasResult.AssignmentsFound(var assignments)
                ? Results.Ok(new AssignmentsResponse(
                    assignments.Select(assignment => new AssignmentResponse(
                        assignment.Machine,
                        assignment.RepairerId,
                        assignment.RepairerName,
                        assignment.AssignmentUnavailable))
                        .ToArray()))
                : MapResult(result);
        });

        // Route 11 — consumed read: the repairer register.
        group.MapGet("/repairers", async (
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetRepairersAsync(cancellationToken);

            return result is BoquilhasResult.RepairersFound(var repairers)
                ? Results.Ok(new RepairersResponse(
                    repairers.Select(repairer => new RepairerResponse(
                        repairer.RepairerId,
                        repairer.Name))
                        .ToArray()))
                : MapResult(result);
        });

        // Route 12 — the local Histórico query: movement-level chronological history with the
        // production context (filters backend-applied).
        group.MapGet("/history", async (
            string? reference,
            string? lot,
            string? machine,
            DateOnly? businessDateFrom,
            DateOnly? businessDateTo,
            string? movementType,
            Guid? repairerId,
            int? page,
            int? pageSize,
            IBoquilhasService service,
            CancellationToken cancellationToken) =>
        {
            var query = new BoquilhasHistoryQuery(
                reference,
                lot,
                machine,
                businessDateFrom,
                businessDateTo,
                movementType,
                repairerId,
                page ?? 1,
                pageSize ?? 50);

            var result = await service.GetHistoryAsync(query, cancellationToken);

            return result is BoquilhasResult.HistoryFound(var rows, var total)
                ? Results.Ok(new HistoryListResponse(
                    rows.Select(row => new HistoryItemResponse(
                        row.MovementId,
                        row.BoquilhasId,
                        row.MovementType,
                        row.Quantity,
                        row.BusinessDate,
                        row.RecordedAt,
                        row.RecordedByUserId,
                        row.Machine,
                        row.RepairerId,
                        row.Observations,
                        row.Reference,
                        row.Lot,
                        row.ProductionNumber,
                        row.ProductionMachine))
                        .ToArray(),
                    total))
                : MapResult(result);
        });

        return app;
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> ExecuteAsync(
        BoquilhasResult result,
        ILogger logger,
        Func<BoquilhasResult, IResult?>? success)
    {
        // Status-level observability only: never payloads, never secrets.
        if (result is BoquilhasResult.Refused(var reason, var message))
        {
            logger.LogWarning("Boquilhas operation refused ({Reason}): {Message}", reason, message);
        }

        return success?.Invoke(result) ?? MapResult(result);
    }

    private static IResult MapResult(BoquilhasResult result, ILogger? logger = null) => result switch
    {
        BoquilhasResult.ListFound(var rows, var total) => Results.Ok(new RegisterListResponse(
            rows.Select(row => new RegisterItemResponse(
                row.BoquilhasId,
                row.BqId,
                row.ToolId,
                row.IsPending,
                row.Reference,
                row.Lot,
                row.ProductionNumber,
                row.ProductionMachine,
                row.ProductionDate,
                row.Outstanding,
                row.MovementCount,
                row.LastMovementAt))
                .ToArray(),
            total)),

        BoquilhasResult.HistoryFound(var rows, var total) => Results.Ok(new HistoryListResponse(
            rows.Select(row => new HistoryItemResponse(
                row.MovementId,
                row.BoquilhasId,
                row.MovementType,
                row.Quantity,
                row.BusinessDate,
                row.RecordedAt,
                row.RecordedByUserId,
                row.Machine,
                row.RepairerId,
                row.Observations,
                row.Reference,
                row.Lot,
                row.ProductionNumber,
                row.ProductionMachine))
                .ToArray(),
            total)),

        BoquilhasResult.Ficha(var ficha) => Results.Ok(new { ficha = ToFicha(ficha) }),

        BoquilhasResult.MovementAuditFound(var movementId, var entries) =>
            Results.Ok(new MovementAuditResponse(movementId, entries.Select(ToAuditItem).ToArray())),

        BoquilhasResult.RegisterCreated(var id) =>
            Results.Created($"{BoquilhasBasePath}/registers/{id}", new RegisterCreatedResponse(id)),

        BoquilhasResult.MovementAppended(var movementId, var version) =>
            Results.Created(
                $"{BoquilhasBasePath}/registers/{movementId}",
                new MovementAppliedResponse(movementId, version)),

        BoquilhasResult.MovementEdited(var movementId, var version) =>
            Results.Ok(new MovementEditedResponse(movementId, version)),

        BoquilhasResult.ProductionsFound(var productions) => Results.Ok(new ProductionsResponse(
            productions.Select(production => new ProductionItemResponse(
                production.JobOnId,
                production.Reference,
                production.ProductionNumber,
                production.Machine,
                production.ProductionDate))
                .ToArray())),

        BoquilhasResult.JobOnFichaFound(var ficha) => Results.Ok(new { ficha = ToJobOnFicha(ficha) }),

        BoquilhasResult.BqAssociated(var jobOnId, var bqId, var version) =>
            Results.Created(
                $"{BoquilhasBasePath}/jobons/{jobOnId}",
                new BqAssociatedResponse(jobOnId, bqId, version)),

        BoquilhasResult.AssignmentsFound(var assignments) => Results.Ok(new AssignmentsResponse(
            assignments.Select(assignment => new AssignmentResponse(
                assignment.Machine,
                assignment.RepairerId,
                assignment.RepairerName,
                assignment.AssignmentUnavailable))
                .ToArray())),

        BoquilhasResult.RepairersFound(var repairers) => Results.Ok(new RepairersResponse(
            repairers.Select(repairer => new RepairerResponse(
                repairer.RepairerId,
                repairer.Name))
                .ToArray())),

        BoquilhasResult.AssociationCandidatesFound(var candidates) =>
            Results.Ok(new AssociationCandidatesResponse(
                candidates.Select(candidate => new AssociationCandidateResponse(
                    candidate.BqId,
                    candidate.JobOnId,
                    candidate.Reference,
                    candidate.ProductionNumber,
                    candidate.Machine,
                    candidate.ProductionDate))
                    .ToArray())),

        BoquilhasResult.PendingRegistersFound(var registers) =>
            Results.Ok(new PendingRegistersResponse(
                registers.Select(register => new PendingRegisterResponse(
                    register.BoquilhasId,
                    register.ToolId,
                    register.ToolReference,
                    register.ToolLot,
                    register.Version,
                    register.CreatedAt,
                    register.MovementCount,
                    register.Outstanding))
                    .ToArray())),

        BoquilhasResult.Associated(var associatedId, var version, var bqId) =>
            Results.Ok(new AssociatedResponse(associatedId, version, bqId)),

        BoquilhasResult.ValidationFailed(var errors) => ValidationFailed(errors),

        BoquilhasResult.NotFound(var id) => Results.NotFound(new { reason = "not-found", id }),

        BoquilhasResult.Refused(var reason, var message) =>
            Results.Conflict(new BoquilhasRefusalResponse(RefusalToken(reason), message)),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult ValidationFailed(params string[] errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    private static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    /// <summary>The exact transport token of a Boquilhas refusal.</summary>
    public static string RefusalToken(BoquilhasRefusalReason reason) => reason switch
    {
        BoquilhasRefusalReason.StaleVersion => "stale-version",
        BoquilhasRefusalReason.RegisterExists => "register-exists",
        BoquilhasRefusalReason.AlreadyAssociated => "already-associated",
        BoquilhasRefusalReason.AssociationMismatch => "association-mismatch",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown refusal reason."),
    };

    private static FichaResponse ToFicha(RegisterFichaReadModel ficha) => new(
        ficha.BoquilhasId,
        ficha.BqId,
        ficha.ToolId,
        ficha.IsPending,
        ficha.Reference,
        ficha.Lot,
        ficha.Outstanding,
        ficha.CreatedByUserId,
        ficha.CreatedAt,
        ficha.Version,
        ficha.Anchor is { } anchor
            ? new AnchorResponse(
                anchor.ToolId,
                anchor.FrozenToolType,
                anchor.FrozenToolReference,
                anchor.FrozenToolLot)
            : null,
        ficha.PendingTool is { } pendingTool
            ? new PendingToolResponse(
                pendingTool.ToolId,
                pendingTool.ToolType,
                pendingTool.ToolReference,
                pendingTool.ToolLot)
            : null,
        ficha.Production is { } production
            ? new ProductionContextResponse(
                production.Reference,
                production.ProductionNumber,
                production.Machine,
                production.ProductionDate)
            : null,
        ficha.Movements.Select(movement => new MovementResponse(
            movement.MovementId,
            movement.MovementType,
            movement.Quantity,
            movement.BusinessDate,
            movement.RecordedAt,
            movement.RecordedByUserId,
            movement.Machine,
            movement.RepairerId,
            movement.Observations,
            movement.Version,
            movement.Saldo))
            .ToArray());

    private static MovementAuditItemResponse ToAuditItem(MovementAuditItemReadModel entry) => new(
        entry.MovementAuditId,
        entry.MovementId,
        entry.EditedByUserId,
        entry.EditedAt,
        entry.BeforeQuantity,
        entry.AfterQuantity,
        entry.BeforeBusinessDate,
        entry.AfterBusinessDate,
        entry.BeforeMachine,
        entry.AfterMachine,
        entry.BeforeRepairerId,
        entry.AfterRepairerId,
        entry.BeforeObservations,
        entry.AfterObservations);

    private static JobOnFichaResponse ToJobOnFicha(JobOnFicha ficha) => new(
        ficha.JobOnId,
        ficha.Reference,
        ficha.ProductionNumber,
        ficha.Machine,
        ficha.ProductionDate,
        ficha.Version,
        ficha.Contexts.Select(context => new JobOnBqContextResponse(
            context.ContextId,
            context.ToolId,
            DMO.Application.Tools.ToolTokens.ToToken(context.ToolType),
            context.ToolReference,
            context.ToolLot))
            .ToArray());

    // ---------------------------------------------------------------------------------------------
    // Transport shapes (route matrix 1–12)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Route 4 create carrier: EXACTLY ONE anchor — <c>BqId</c> (the REAL production BQ context)
    /// XOR <c>PendingToolId</c> (the transitional pré-JobOn canonical BQ Tool, §34.1). The JSON
    /// members are <c>bqId</c>/<c>pendingToolId</c>; the backend resolves the actor.
    /// </summary>
    public sealed record CreateRegisterRequest(Guid? BqId, Guid? PendingToolId);

    /// <summary>§34 associação carrier: the explicit candidate <c>bq_id</c> + the observed register version.</summary>
    public sealed record AssociateRegisterRequest(Guid BqId, int ExpectedVersion);

    /// <summary>Route 5 append carrier: the movement facts.</summary>
    public sealed record AppendMovementRequest(
        string MovementType,
        int Quantity,
        DateOnly BusinessDate,
        string? Machine,
        Guid? RepairerId,
        string? Observations);

    /// <summary>Route 6 edit carrier: the observed movement version + the new editable values only.</summary>
    public sealed record EditMovementRequest(
        int ExpectedMovementVersion,
        int Quantity,
        DateOnly BusinessDate,
        string? Machine,
        Guid? RepairerId,
        string? Observations);

    /// <summary>Route 9 BQ-association carrier: the canonical Tool + the observed Job On version.</summary>
    public sealed record BqAssociationRequest(Guid ToolId, int ExpectedJobOnVersion);

    /// <summary>Route 1 response.</summary>
    public sealed record RegisterListResponse(IReadOnlyList<RegisterItemResponse> Rows, int Total);

    /// <summary>One route 1 row (a pending row carries the transitional Tool anchor).</summary>
    public sealed record RegisterItemResponse(
        Guid BoquilhasId,
        Guid? BqId,
        Guid? ToolId,
        bool IsPending,
        string? Reference,
        string? Lot,
        string? ProductionNumber,
        string? ProductionMachine,
        DateOnly? ProductionDate,
        int Outstanding,
        int MovementCount,
        DateTimeOffset? LastMovementAt);

    /// <summary>Route 2 response.</summary>
    public sealed record FichaResponse(
        Guid BoquilhasId,
        Guid? BqId,
        Guid? ToolId,
        bool IsPending,
        string? Reference,
        string? Lot,
        int Outstanding,
        Guid CreatedByUserId,
        DateTimeOffset CreatedAt,
        int Version,
        AnchorResponse? Anchor,
        PendingToolResponse? PendingTool,
        ProductionContextResponse? Production,
        IReadOnlyList<MovementResponse> Movements);

    /// <summary>The anchor context of a route 2 ficha (the frozen BQ triple).</summary>
    public sealed record AnchorResponse(
        Guid ToolId,
        string FrozenToolType,
        string FrozenToolReference,
        string FrozenToolLot);

    /// <summary>The transitional pré-JobOn Tool facts of a pending route 2 ficha (§34.1).</summary>
    public sealed record PendingToolResponse(
        Guid ToolId,
        string ToolType,
        string ToolReference,
        string ToolLot);

    /// <summary>The real production context of a route 2 ficha.</summary>
    public sealed record ProductionContextResponse(
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate);

    /// <summary>One movement of the route 2 ledger (with the display-only cumulative saldo).</summary>
    public sealed record MovementResponse(
        Guid MovementId,
        string MovementType,
        int Quantity,
        DateOnly BusinessDate,
        DateTimeOffset RecordedAt,
        Guid RecordedByUserId,
        string? Machine,
        Guid? RepairerId,
        string? Observations,
        int Version,
        int Saldo);

    /// <summary>Route 3 response: the per-movement edit/audit trail.</summary>
    public sealed record MovementAuditResponse(Guid MovementId, IReadOnlyList<MovementAuditItemResponse> Entries);

    /// <summary>One route 3 audit item (before/after of every editable field + backend actor/time).</summary>
    public sealed record MovementAuditItemResponse(
        Guid MovementAuditId,
        Guid MovementId,
        Guid EditedByUserId,
        DateTimeOffset EditedAt,
        int BeforeQuantity,
        int AfterQuantity,
        DateOnly BeforeBusinessDate,
        DateOnly AfterBusinessDate,
        string? BeforeMachine,
        string? AfterMachine,
        Guid? BeforeRepairerId,
        Guid? AfterRepairerId,
        string? BeforeObservations,
        string? AfterObservations);

    /// <summary>Route 4 response.</summary>
    public sealed record RegisterCreatedResponse(Guid BoquilhasId);

    /// <summary>Route 5 response.</summary>
    public sealed record MovementAppliedResponse(Guid MovementId, int Version);

    /// <summary>Route 6 response.</summary>
    public sealed record MovementEditedResponse(Guid MovementId, int Version);

    /// <summary>Route 7 response.</summary>
    public sealed record ProductionsResponse(IReadOnlyList<ProductionItemResponse> Productions);

    /// <summary>One route 7 production item (the accepted P2-T04 carrier shape).</summary>
    public sealed record ProductionItemResponse(
        Guid JobonId,
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate);

    /// <summary>Route 8 response: the Job On ficha facts + context presence.</summary>
    public sealed record JobOnFichaResponse(
        Guid JobOnId,
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate,
        int Version,
        IReadOnlyList<JobOnBqContextResponse> Contexts);

    /// <summary>One BQ context of a route 8 ficha.</summary>
    public sealed record JobOnBqContextResponse(
        Guid ContextId,
        Guid ToolId,
        string ToolType,
        string ToolReference,
        string ToolLot);

    /// <summary>Route 9 response: the REAL bq_id created/updated by Job On's own code.</summary>
    /// <remarks>The transport token is <c>jobonId</c> (the accepted P2-T04 convention).</remarks>
    public sealed record BqAssociatedResponse(Guid JobonId, Guid BqId, int Version);

    /// <summary>§34.candidates response: the REAL BQ contexts resolving to the pending anchor Tool.</summary>
    public sealed record AssociationCandidatesResponse(IReadOnlyList<AssociationCandidateResponse> Candidates);

    /// <summary>One §34.candidates row (a REAL bq_contexts row + the REAL Job On facts).</summary>
    public sealed record AssociationCandidateResponse(
        Guid BqId,
        Guid JobonId,
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate);

    /// <summary>§34.pending response: the pré-JobOn registers of the production's canonical Tool.</summary>
    public sealed record PendingRegistersResponse(IReadOnlyList<PendingRegisterResponse> Registers);

    /// <summary>One §34.pending row (light packet: register facts + the anchor Tool facts; history only when opened).</summary>
    public sealed record PendingRegisterResponse(
        Guid BoquilhasId,
        Guid ToolId,
        string ToolReference,
        string ToolLot,
        int Version,
        DateTimeOffset CreatedAt,
        int MovementCount,
        int Outstanding);

    /// <summary>§34.associate response: the SAME boquilhas_id, now <c>bq_id → jobon_id</c>.</summary>
    public sealed record AssociatedResponse(Guid BoquilhasId, int Version, Guid BqId);

    /// <summary>Route 10 response.</summary>
    public sealed record AssignmentsResponse(IReadOnlyList<AssignmentResponse> Assignments);

    /// <summary>One route 10 assignment (absent row = explicit <c>assignmentUnavailable</c>).</summary>
    public sealed record AssignmentResponse(
        string Machine,
        Guid? RepairerId,
        string? RepairerName,
        bool AssignmentUnavailable);

    /// <summary>Route 11 response.</summary>
    public sealed record RepairersResponse(IReadOnlyList<RepairerResponse> Repairers);

    /// <summary>One route 11 repairer.</summary>
    public sealed record RepairerResponse(Guid RepairerId, string Name);

    /// <summary>Route 12 response.</summary>
    public sealed record HistoryListResponse(IReadOnlyList<HistoryItemResponse> Rows, int Total);

    /// <summary>One route 12 row: one movement with its register's production context.</summary>
    public sealed record HistoryItemResponse(
        Guid MovementId,
        Guid BoquilhasId,
        string MovementType,
        int Quantity,
        DateOnly BusinessDate,
        DateTimeOffset RecordedAt,
        Guid RecordedByUserId,
        string? Machine,
        Guid? RepairerId,
        string? Observations,
        string? Reference,
        string? Lot,
        string? ProductionNumber,
        string? ProductionMachine);

    /// <summary>Typed, actionable refusal response.</summary>
    public sealed record BoquilhasRefusalResponse(string Reason, string Message);
}