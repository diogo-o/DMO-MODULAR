using DMO.Application.Access;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Session;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints;

/// <summary>
/// Minimal API surface of Controlo Create (P2-T05 contract §21.3 routes 2–11; route 1 is the Razor
/// page <c>Pages/Controlo/Create</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every route declares exactly ONE canonical Module policy:
/// <c>ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloCreate)</c> — no route carries
/// <c>controlo-approve</c>, no route is anonymous, no policy is created. The P2-T05 routes that
/// compose P2-T04 application reads never bypass a policy: the Tool search/create calls stay on
/// the <c>ferramentas</c> routes (P2-T04 routes 12/13).</para>
/// <para>
/// Route 8 (calculate) is request-carrier-identified (C3): no path identity, no record resolution,
/// never 404, no write and no version bump. The endpoints perform no domain decision: they bind
/// transport shapes, resolve the backend actor from <c>ICurrentAccountContext</c>, call the
/// application services and switch on the closed result sets. Every persisted-state refusal is a
/// 409 distinguished by its reason token; no failure body ever contains a secret, a path, a
/// connection string or another user's data.</para>
/// </remarks>
public static class ControloCreateEndpoints
{
    /// <summary>Base path of the Controlo Create surface.</summary>
    public const string ControloCreateBasePath = "/controlo/create";

    /// <summary>The canonical policy of every Controlo Create route (the pinned-value pattern of the
/// accepted <c>JobOnPolicyNames</c>; the Razor-page constants live in
/// <c>DMO.Web.Pages.Controlo.ControloPolicyNames</c>).</summary>
    public static string Policy { get; } =
        ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.ControloCreate);

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the Controlo Create endpoints onto the application.</summary>
    public static WebApplication MapControloCreateEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(ControloCreateBasePath)
            .RequireAuthorization(Policy);

        // Route 2 — reference → productions (explicit selection source). Every matching occurrence
        // is returned; a single result is never auto-selected (AC-R1). The four outcomes stay
        // distinct: validation (400), denied (403), lookup failure, explicit empty (200).
        group.MapGet("/productions", async (
            string? reference,
            IJobOnService jobOns,
            CancellationToken cancellationToken) =>
        {
            var result = await jobOns.FindProductionsAsync(
                new FindProductionsQuery(reference ?? string.Empty),
                cancellationToken);

            return result is JobOnResult.ProductionsFound(var productions)
                ? Results.Ok(new JobOnEndpoints.JobOnProductionListResponse(
                    productions
                        .Select(production => new JobOnEndpoints.JobOnProductionItemResponse(
                            production.JobOnId,
                            production.Reference,
                            production.ProductionNumber,
                            production.Machine,
                            production.ProductionDate))
                        .ToArray()))
                : MapJobOnResult(result);
        });

        // Route 3 — Job On ficha + CM context (frozen triple + live projection) for the strip and
        // context regions. The SAME ficha shape as the accepted Job On consult read (one query
        // shape: AC-R2).
        group.MapGet("/jobons/{jobonId:guid}", async (
            Guid jobonId,
            IJobOnService jobOns,
            CancellationToken cancellationToken) =>
        {
            var result = await jobOns.GetAsync(jobonId, cancellationToken);

            return result is JobOnResult.Ficha(var ficha)
                ? Results.Ok(ToFichaResponse(ficha))
                : MapJobOnResult(result);
        });

        // Resumo da produção — the Controlo entry read of ONE occurrence (P2-T05 contract §31.1,
        // OWNER CLARIFICATION: jobon_id → Resumo da produção → cm_id → Peso). Light packet: the
        // production facts + the CM context the Peso surface is populated from (reference, machine,
        // lot, processo, CM identity) — one context-specific statement, no global scan, no Peso/
        // output/history payload. Read-only; never writes and never bumps.
        group.MapGet("/resumo/{jobonId:guid}", async (
            Guid jobonId,
            IProductionResumoRead resumo,
            CancellationToken cancellationToken) =>
        {
            var value = await resumo.GetResumoAsync(jobonId, cancellationToken);

            return value is null
                ? Results.NotFound(new { reason = "not-found", jobonId })
                : Results.Ok(ToResumoResponse(value));
        });

        // Route 4 — pending-association candidates resolving to the anchor Tool (the additive Job On
        // read, Q-CAND).
        group.MapGet("/tools/{toolId:guid}/association-candidates", async (
            Guid toolId,
            IControloCreateService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAssociationCandidatesAsync(toolId, cancellationToken);

            return result is PesoResult.Candidates(var value)
                ? Results.Ok(new PesoAssociationCandidateResponse(
                    value
                        .Select(candidate => new PesoAssociationCandidateItemResponse(
                            candidate.CmContextId,
                            candidate.JobOnId,
                            candidate.Reference,
                            candidate.ProductionNumber,
                            candidate.Machine))
                        .ToArray()))
                : MapResult(result);
        });

        // Route 5 — create the Peso draft transactionally (record + full row set; backend-allocated
        // ids; the actor is the backend-resolved current USER).
        group.MapPost("/pesos", async (
            CreatePesoRequest? body,
            IControloCreateService service,
            ICurrentAccountContext currentAccount,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var actor = await ResolveCurrentUserIdAsync(currentAccount, cancellationToken);
            if (actor is null)
            {
                return Results.Forbid();
            }

            if (body is null)
            {
                return ValidationFailed(ControloCreateValidationErrors.PesoAnchorRequired);
            }

            var command = new CreatePesoCommand(
                body.CmId,
                body.PendingToolId,
                body.WaterTemperature,
                body.VolumeMarisaBq,
                body.VolumePuncaoPu,
                body.PreviousProductionEndReference,
                body.PreviousAverageWeightReference,
                body.Rows?.Select(row => row.WaterWeightG).ToArray() ?? [],
                actor.Value);

            return await ExecuteAsync(
                token => service.CreateAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is PesoResult.Created(var pesoId, var version)
                    ? Results.Created(
                        $"{ControloCreateBasePath}/pesos/{pesoId}",
                        new PesoCreatedResponse(pesoId, version))
                    : null);
        });

        // Route 6 — the published Peso read (ficha: anchor projection, inputs, rows+results,
        // attribution). Read-only projection; never writes and never bumps.
        group.MapGet("/pesos/{pesoId:guid}", async (
            Guid pesoId,
            IControloCreateService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(pesoId, cancellationToken);

            return result is PesoResult.Found(var sheet)
                ? Results.Ok(ToSheetResponse(sheet))
                : MapResult(result);
        });

        // Route 7 — draft edit: same peso_id, whole row set replaced, derived results recomputed.
        group.MapPut("/pesos/{pesoId:guid}", async (
            Guid pesoId,
            UpdatePesoRequest? body,
            IControloCreateService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloCreateValidationErrors.WaterTemperatureRequired);
            }

            var command = new UpdatePesoCommand(
                pesoId,
                body.ExpectedVersion,
                body.WaterTemperature,
                body.VolumeMarisaBq,
                body.VolumePuncaoPu,
                body.PreviousProductionEndReference,
                body.PreviousAverageWeightReference,
                body.Rows?.Select(row => row.WaterWeightG).ToArray() ?? []);

            return await ExecuteAsync(
                token => service.UpdateAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is PesoResult.Updated(var updatedId, var version)
                    ? Results.Ok(new PesoUpdatedResponse(updatedId, version))
                    : null);
        });

        // Route 8 — stateless calculation: request-carrier identity only (C3). No path identity, no
        // record resolution, never 404, no write, no version bump, no id allocation.
        group.MapPost("/calculate", async (
            CalculatePesoRequest? body,
            IControloCreateService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloCreateValidationErrors.PesoAnchorRequired);
            }

            var command = new CalculatePesoCommand(
                body.CmId,
                body.PendingToolId,
                body.WaterTemperature,
                body.VolumeMarisaBq,
                body.VolumePuncaoPu,
                body.PreviousProductionEndReference,
                body.PreviousAverageWeightReference,
                body.Rows?.Select(row => row.WaterWeightG).ToArray() ?? []);

            return await ExecuteAsync(
                token => service.CalculateAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is PesoResult.Calculation(var value)
                    ? Results.Ok(new PesoCalculationResponse(
                        value.CmId,
                        value.PendingToolId,
                        value.WaterTemperature,
                        value.WaterDensityGCm3,
                        value.GlassDensityGCm3,
                        value.Rows
                            .Select(row => new PesoRowCalculationResponse(
                                row.RowPosition,
                                row.WaterWeightG,
                                row.CapacityCm3,
                                row.GlassWeightG))
                            .ToArray()))
                    : null);
        });

        // Route 9 — submit the SAME peso_id into the reviewable handoff (backend attribution).
        group.MapPost("/pesos/{pesoId:guid}/submit", async (
            Guid pesoId,
            SubmitPesoRequest? body,
            IControloCreateService service,
            ICurrentAccountContext currentAccount,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var actor = await ResolveCurrentUserIdAsync(currentAccount, cancellationToken);
            if (actor is null)
            {
                return Results.Forbid();
            }

            if (body is null)
            {
                return ValidationFailed(ControloCreateValidationErrors.PesoAnchorRequired);
            }

            var command = new SubmitPesoCommand(pesoId, body.ExpectedVersion, actor.Value);

            return await ExecuteAsync(
                token => service.SubmitAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is PesoResult.Submitted(var submittedId, var version, var submittedAt)
                    ? Results.Ok(new PesoSubmittedResponse(submittedId, version, submittedAt))
                    : null);
        });

        // Route 10 — explicit pending → production association (candidate cm_id must resolve to the
        // anchor tool_id; the direct Tool anchor is cleared on success).
        group.MapPost("/pesos/{pesoId:guid}/associate", async (
            Guid pesoId,
            AssociatePesoRequest? body,
            IControloCreateService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloCreateValidationErrors.CmContextNotFound);
            }

            var command = new AssociatePesoCommand(pesoId, body.CmId, body.ExpectedVersion);

            return await ExecuteAsync(
                token => service.AssociateAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is PesoResult.Associated(var associatedId, var version, var cmId)
                    ? Results.Ok(new PesoAssociatedResponse(associatedId, version, cmId))
                    : null);
        });

        // Route 11 — create the MISSING CM context through the Job On application contract
        // (Keep every fact + Set the CM slot): the cm_contexts row is created by Job On's own
        // application/repository code, never by a Controlo repository (§21.4). The date is kept
        // unchanged; acknowledging the existing-state threshold is not a Job On change.
        group.MapPost("/jobons/{jobonId:guid}/cm-association", async (
            Guid jobonId,
            CmAssociationRequest? body,
            IJobOnService jobOns,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloCreateValidationErrors.ToolNotFound);
            }

            var result = await jobOns.GetAsync(jobonId, cancellationToken);

            if (result is not JobOnResult.Ficha(var ficha))
            {
                return MapJobOnResult(result);
            }

            var command = new UpdateJobOnCommand(
                jobonId,
                body.ExpectedJobOnVersion,
                ficha.Reference,
                ficha.ProductionNumber,
                ficha.Machine,
                ficha.ProductionDate,
                [new ToolAssociationChange(ToolContextType.Cm, ToolAssociationAction.Set, body.ToolId)],
                DateThresholdWarningAcknowledged: true);

            var update = await jobOns.UpdateAsync(command, cancellationToken);

            if (update is not JobOnResult.Updated(var updatedJobOnId, var updatedVersion))
            {
                return MapJobOnResult(update);
            }

            // The ficha is re-read so the response can name the REAL context identity (which the
            // Job On Set created or updated in place).
            var after = await jobOns.GetAsync(updatedJobOnId, cancellationToken);

            if (after is JobOnResult.Ficha(var afterFicha))
            {
                var cm = afterFicha.Contexts.FirstOrDefault(context =>
                    context.ContextType == ToolContextType.Cm);

                if (cm is not null)
                {
                    return Results.Created(
                        $"{ControloCreateBasePath}/jobons/{updatedJobOnId}",
                        new CmAssociationCreatedResponse(updatedJobOnId, cm.ContextId, updatedVersion));
                }
            }

            return Results.Conflict(new
            {
                reason = "dependency-exists",
                message = "The Job On update committed but no CM context could be read back.",
            });
        });

        return app;
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<Guid?> ResolveCurrentUserIdAsync(
        ICurrentAccountContext currentAccount,
        CancellationToken cancellationToken)
    {
        var current = await currentAccount.GetCurrentAsync(cancellationToken);

        return current is CurrentAccount.User(var account)
            ? account.AccountId
            : null;
    }

    private static async Task<IResult> ExecuteAsync(
        Func<CancellationToken, Task<PesoResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken,
        Func<PesoResult, IResult?>? success = null)
    {
        var result = await operation(cancellationToken);

        // Status-level observability only: never payloads, never secrets.
        if (result is PesoResult.Refused(var reason, var message))
        {
            logger.LogWarning("Controlo Create operation refused ({Reason}): {Message}", reason, message);
        }

        if (success is not null && success(result) is { } mapped)
        {
            return mapped;
        }

        return MapResult(result);
    }

    private static IResult MapResult(PesoResult result) => result switch
    {
        PesoResult.Created(var pesoId, var version) => Results.Created(
            $"{ControloCreateBasePath}/pesos/{pesoId}",
            new PesoCreatedResponse(pesoId, version)),

        PesoResult.Updated(var pesoId, var version) => Results.Ok(
            new PesoUpdatedResponse(pesoId, version)),

        PesoResult.Submitted(var pesoId, var version, var submittedAt) => Results.Ok(
            new PesoSubmittedResponse(pesoId, version, submittedAt)),

        PesoResult.Associated(var pesoId, var version, var cmId) => Results.Ok(
            new PesoAssociatedResponse(pesoId, version, cmId)),

        PesoResult.Calculation(var value) => Results.Ok(new PesoCalculationResponse(
            value.CmId,
            value.PendingToolId,
            value.WaterTemperature,
            value.WaterDensityGCm3,
            value.GlassDensityGCm3,
            value.Rows
                .Select(row => new PesoRowCalculationResponse(
                    row.RowPosition,
                    row.WaterWeightG,
                    row.CapacityCm3,
                    row.GlassWeightG))
                .ToArray())),

        PesoResult.Found(var sheet) => Results.Ok(ToSheetResponse(sheet)),

        PesoResult.Candidates(var value) => Results.Ok(new PesoAssociationCandidateResponse(
            value
                .Select(candidate => new PesoAssociationCandidateItemResponse(
                    candidate.CmContextId,
                    candidate.JobOnId,
                    candidate.Reference,
                    candidate.ProductionNumber,
                    candidate.Machine))
                .ToArray())),

        PesoResult.ValidationFailed(var errors) => ValidationFailed(errors),

        PesoResult.NotFound(var pesoId) => Results.NotFound(new { reason = "not-found", pesoId }),

        PesoResult.Refused(var reason, var message) =>
            Results.Conflict(new PesoRefusalResponse(RefusalToken(reason), message)),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    /// <summary>
    /// Maps the composed Job On read results (routes 2/3/11) through the accepted closed Job On
    /// result set. The P2-T05 read model is one query chain over the P2-T04 application contract
    /// (AC-R2); the denial is never an empty list and a validation failure is never an empty
    /// result.
    /// </summary>
    private static IResult MapJobOnResult(JobOnResult result) => result switch
    {
        JobOnResult.ProductionsFound(var productions) => Results.Ok(
            new JobOnEndpoints.JobOnProductionListResponse(
                productions
                    .Select(production => new JobOnEndpoints.JobOnProductionItemResponse(
                        production.JobOnId,
                        production.Reference,
                        production.ProductionNumber,
                        production.Machine,
                        production.ProductionDate))
                    .ToArray())),

        JobOnResult.Ficha(var ficha) => Results.Ok(ToFichaResponse(ficha)),

        JobOnResult.ValidationFailed(var errors) => ValidationFailed(errors),

        JobOnResult.NotFound(var jobOnId) => Results.NotFound(new { reason = "not-found", jobonId = jobOnId }),

        JobOnResult.Refused(var reason, var message, _, _) =>
            Results.Conflict(new
            {
                reason = JobOnEndpoints.RefusalToken(reason),
                message,
            }),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult ValidationFailed(params string[] errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    private static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    /// <summary>The exact transport token of a Peso refusal (contract §26.2).</summary>
    public static string RefusalToken(PesoRefusalReason reason) => reason switch
    {
        PesoRefusalReason.StaleVersion => "stale-version",
        PesoRefusalReason.AlreadySubmitted => "already-submitted",
        PesoRefusalReason.AlreadyAssociated => "already-associated",
        PesoRefusalReason.AssociationMismatch => "association-mismatch",
        PesoRefusalReason.CalculationConfigurationMissing => "calculation-configuration-missing",
        PesoRefusalReason.DependencyExists => "dependency-exists",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown refusal reason."),
    };

    private static JobOnEndpoints.JobOnFichaResponse ToFichaResponse(JobOnFicha ficha) => new(
        ficha.JobOnId,
        ficha.Reference,
        ficha.ProductionNumber,
        ficha.Machine,
        ficha.ProductionDate,
        ficha.CopiedFromJobOnId,
        ficha.Version,
        ficha.Contexts.Select(context => new JobOnEndpoints.JobOnContextResponse(
            ToolTokens.ToToken(context.ContextType),
            context.ContextId,
            context.ToolId,
            ToolTokens.ToToken(context.ToolType),
            context.ToolReference,
            context.ToolLot,
            new JobOnEndpoints.JobOnToolProjectionResponse(
                context.Tool.ToolId,
                ToolTokens.ToToken(context.Tool.Type),
                context.Tool.Reference,
                context.Tool.Lot,
                ToolTokens.ToToken(context.Tool.Processo),
                context.Tool.Quantity,
                context.Tool.CompatibleMachines.Select(machine => machine.Value).ToArray()))).ToArray());

    private static ProductionResumoResponse ToResumoResponse(ProductionResumoReadModel resumo) => new(
        resumo.JobOnId,
        resumo.Reference,
        resumo.ProductionNumber,
        resumo.Machine,
        resumo.ProductionDate,
        resumo.Version,
        resumo.Cm is { } cm
            ? new CmResumoResponse(
                cm.CmId,
                cm.ToolId,
                cm.FrozenToolType,
                cm.FrozenToolReference,
                cm.FrozenToolLot,
                cm.LiveToolReference,
                cm.LiveToolLot,
                ToolTokens.ToToken(cm.Processo),
                cm.Quantity)
            : null);

    private static PesoSheetResponse ToSheetResponse(PesoSheetReadModel sheet) => new(
        sheet.PesoId,
        sheet.Version,
        sheet.Status,
        sheet.CmId,
        sheet.ToolId,
        sheet.Context is { } context
            ? new PesoContextResponse(
                context.CmId,
                context.ToolId,
                context.FrozenToolType,
                context.FrozenToolReference,
                context.FrozenToolLot,
                ToToolProjection(context.Tool))
            : null,
        sheet.Pending is { } pending
            ? new PesoPendingResponse(pending.ToolId, ToToolProjection(pending.Tool))
            : null,
        sheet.Production is { } production
            ? new PesoProductionResponse(
                production.Reference,
                production.ProductionNumber,
                production.Machine,
                production.ProductionDate)
            : null,
        sheet.CreatedByUserId,
        sheet.CreatedAt,
        sheet.SubmittedByUserId,
        sheet.SubmittedAt,
        sheet.WaterTemperature,
        sheet.VolumeMarisaBq,
        sheet.VolumePuncaoPu,
        sheet.GlassDensityGCm3,
        sheet.PreviousProductionEndReference,
        sheet.PreviousAverageWeightReference,
        sheet.Rows
            .Select(row => new PesoRowResponse(
                row.PesoMeasurementRowId,
                row.RowPosition,
                row.WaterWeightG,
                row.CapacityCm3,
                row.GlassWeightG))
            .ToArray());

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
    // Transport shapes (route table §21.3)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Route 5 create carrier: exactly one anchor + the Peso input facts.</summary>
    public sealed record CreatePesoRequest(
        Guid? CmId,
        Guid? PendingToolId,
        decimal WaterTemperature,
        decimal? VolumeMarisaBq,
        decimal? VolumePuncaoPu,
        string? PreviousProductionEndReference,
        string? PreviousAverageWeightReference,
        IReadOnlyList<PesoRowRequest>? Rows);

    /// <summary>Route 7 edit carrier (same row set shape; the anchor is not editable).</summary>
    public sealed record UpdatePesoRequest(
        int ExpectedVersion,
        decimal WaterTemperature,
        decimal? VolumeMarisaBq,
        decimal? VolumePuncaoPu,
        string? PreviousProductionEndReference,
        string? PreviousAverageWeightReference,
        IReadOnlyList<PesoRowRequest>? Rows);

    /// <summary>Route 8 stateless calculate carrier: exactly one anchor (cmId XOR pendingToolId).</summary>
    public sealed record CalculatePesoRequest(
        Guid? CmId,
        Guid? PendingToolId,
        decimal WaterTemperature,
        decimal? VolumeMarisaBq,
        decimal? VolumePuncaoPu,
        string? PreviousProductionEndReference,
        string? PreviousAverageWeightReference,
        IReadOnlyList<PesoRowRequest>? Rows);

    /// <summary>One measurement row of a create/edit/calculate carrier ("Peso de água" in g).</summary>
    /// <remarks>
    /// The row carries the entered fact only; dense 1-based positions are derived from the supplied
    /// order by the backend (Q-ROWLBL). No canonical identity is ever declared here (P2-T03 opaque
    /// keys stay opaque).
    /// </remarks>
    public sealed record PesoRowRequest(decimal WaterWeightG);

    /// <summary>Route 9 submit carrier.</summary>
    public sealed record SubmitPesoRequest(int ExpectedVersion);

    /// <summary>Route 10 associate carrier: the explicit, human-confirmed candidate cm_id.</summary>
    public sealed record AssociatePesoRequest(Guid CmId, int ExpectedVersion);

    /// <summary>Route 11 missing-CM association carrier.</summary>
    public sealed record CmAssociationRequest(Guid ToolId, int ExpectedJobOnVersion);

    /// <summary>Route 5 response: the real backend-allocated identity and version.</summary>
    public sealed record PesoCreatedResponse(Guid PesoId, int Version);

    /// <summary>Route 7 response.</summary>
    public sealed record PesoUpdatedResponse(Guid PesoId, int Version);

    /// <summary>Route 9 response.</summary>
    public sealed record PesoSubmittedResponse(Guid PesoId, int Version, DateTimeOffset SubmittedAt);

    /// <summary>Route 10 response.</summary>
    public sealed record PesoAssociatedResponse(Guid PesoId, int Version, Guid CmId);

    /// <summary>Route 8 response: per-row full-precision results + resolved display facts.</summary>
    public sealed record PesoCalculationResponse(
        Guid? CmId,
        Guid? PendingToolId,
        decimal WaterTemperature,
        decimal WaterDensityGCm3,
        decimal GlassDensityGCm3,
        IReadOnlyList<PesoRowCalculationResponse> Rows);

    /// <summary>One per-row calculation result.</summary>
    public sealed record PesoRowCalculationResponse(
        int RowPosition,
        decimal WaterWeightG,
        decimal CapacityCm3,
        decimal GlassWeightG);

    /// <summary>Route 4 response: the real association candidates.</summary>
    public sealed record PesoAssociationCandidateResponse(
        IReadOnlyList<PesoAssociationCandidateItemResponse> Candidates);

    /// <summary>One association candidate.</summary>
    public sealed record PesoAssociationCandidateItemResponse(
        Guid CmId,
        Guid JobonId,
        string Reference,
        string ProductionNumber,
        string Machine);

    /// <summary>Route 6 response: the published Peso sheet (§26.3 shape).</summary>
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

    /// <summary>Route 11 response: the real CM context created/updated by the Job On contract.</summary>
    public sealed record CmAssociationCreatedResponse(Guid JobonId, Guid CmId, int Version);

    /// <summary>
    /// Resumo da produção response: the Controlo entry projection of one occurrence (contract
    /// §31.1) — production facts + the CM context the Peso is populated from; the live Tool summary
    /// is the light packet (no compatible machines, no history, no outputs).
    /// </summary>
    public sealed record ProductionResumoResponse(
        Guid JobonId,
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate,
        int Version,
        CmResumoResponse? Cm);

    /// <summary>The CM context of the Resumo: real cm_id/tool_id, the frozen triple and the live
    /// Tool entry facts (reference, lot, processo, quantity).</summary>
    public sealed record CmResumoResponse(
        Guid CmId,
        Guid ToolId,
        string FrozenToolType,
        string FrozenToolReference,
        string FrozenToolLot,
        string LiveToolReference,
        string LiveToolLot,
        string? Processo,
        int? Quantity);

    /// <summary>Typed, actionable refusal response.</summary>
    public sealed record PesoRefusalResponse(string Reason, string Message);
}