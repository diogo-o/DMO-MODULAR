using DMO.Application.Access;
using DMO.Application.JobOn;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints.ToolJobOn;

/// <summary>
/// Minimal API surface of the Job On occurrence (P2-T04 contract §13.2 routes 2, 5, 6, 8, 10 and 11,
/// plus the additive consult route of the outputs slice: open the Peso PDF of a related Peso).
/// </summary>
/// <remarks>
/// <para>
/// The consult reads carry <c>dmo.module.job-on-view</c>; the mutating surface and the reads the
/// create workflow needs carry <c>dmo.module.job-on-create</c>. No route is shared between two
/// policies, and <c>Job On View</c> gains no create/edit/duplicate/delete capability.
/// </para>
/// <para>
/// The endpoints perform no domain decision: they bind transport shapes, call the application service
/// and switch on the closed result set. Every "the current persisted state refuses this write" outcome
/// is a 409 distinguished by its reason token, and no failure body ever contains a secret, a path, a
/// connection string or another user's data.
/// </para>
/// </remarks>
public static class JobOnEndpoints
{
    /// <summary>Base path of the Job On surface.</summary>
    public const string JobOnBasePath = "/jobon";

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the Job On endpoints onto the application.</summary>
    public static WebApplication MapJobOnEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var consult = app.MapGroup(JobOnBasePath)
            .RequireAuthorization(ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.JobOnView));

        var manage = app.MapGroup(JobOnBasePath)
            .RequireAuthorization(ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.JobOnCreate));

        // Route 2 — reference → productions for the consult surface.
        consult.MapGet("/productions", async (
            string? reference,
            IJobOnService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.FindProductionsAsync(
                new FindProductionsQuery(reference ?? string.Empty),
                cancellationToken);

            return MapResult(result);
        });

        // Additive consult route of the outputs slice — open/stream the Peso PDF of ONE related
        // Peso of this production. The route carries only identities: the deterministic target is
        // resolved and the bytes are read by the Documents application contract
        // (IPesoPdfDocumentRead) from the operator-configured base directory — no absolute path and
        // no file:// target ever cross this surface, and nothing here reads the filesystem. The
        // related-peso scope is enforced against the real cm_contexts of the occurrence: a Peso of
        // another production is 404, never served. Missing/not-yet-generated states are typed
        // (409 pdf-not-generated), never a dead button and never a file error conflated with
        // "no file yet".
        consult.MapGet("/{jobonId:guid}/pesos/{pesoId:guid}/peso-pdf", async (
            Guid jobonId,
            Guid pesoId,
            IJobOnControlOutputsService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ReadAsync(jobonId, pesoId, cancellationToken);

            if (result is JobOnControlOutputContentResult.Refused(var reason, var message))
            {
                logger.LogWarning("Peso PDF open refused ({Reason}): {Message}", reason, message);
            }

            return result switch
            {
                // Bytes inline (no Content-Disposition attachment): the action opens/views the PDF
                // in the browser; the file name is never needed at transport level.
                JobOnControlOutputContentResult.Found(_, var content) =>
                    Results.File(content, "application/pdf"),

                JobOnControlOutputContentResult.NotGenerated(var id) =>
                    Results.Conflict(new { reason = "pdf-not-generated", pesoId = id }),

                JobOnControlOutputContentResult.NotFound(var id) =>
                    Results.NotFound(new { reason = "not-found", jobonId = id }),

                JobOnControlOutputContentResult.RelatedPesoNotFound(var jobOnId, var id) =>
                    Results.NotFound(new { reason = "peso-not-found", jobonId = jobOnId, pesoId = id }),

                JobOnControlOutputContentResult.Refused(var refusalReason, var refusalMessage) =>
                    Results.Conflict(new PesoPdfOpenRefusalResponse(
                        OpenRefusalToken(refusalReason),
                        refusalMessage)),

                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        });

        // Route 5 — the same query for the CREATE surface. It exists as its own Create-gated route
        // because "Job On Create" grants the reads technically required to operate its surface, while
        // the frozen policy mechanism has no OR/implication and Job On View never satisfies it.
        manage.MapGet("/create/productions", async (
            string? reference,
            IJobOnService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.FindProductionsAsync(
                new FindProductionsQuery(reference ?? string.Empty),
                cancellationToken);

            return MapResult(result);
        });

        // Route 6 — create the Job On and only the contexts the operator explicitly filled.
        manage.MapPost("", async (
            CreateJobOnRequest? body,
            IJobOnService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(JobOnValidationErrors.ReferenceRequired);
            }

            var command = new CreateJobOnCommand(
                body.Reference ?? string.Empty,
                body.ProductionNumber ?? string.Empty,
                body.Machine ?? string.Empty,
                body.ProductionDate,
                body.CmToolId,
                body.MfToolId,
                body.BqToolId);

            return await ExecuteAsync(
                token => service.CreateAsync(command, token),
                logger,
                cancellationToken);
        });

        // Route 8 — edit the four facts and the three Tool associations under the date-threshold gate.
        manage.MapPut("/{jobonId:guid}", async (
            Guid jobonId,
            UpdateJobOnRequest? body,
            IJobOnService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(JobOnValidationErrors.ReferenceRequired);
            }

            var associations = new List<ToolAssociationChange>(body.Associations?.Count ?? 0);
            foreach (var association in body.Associations ?? [])
            {
                var contextType = ToolTokens.ParseContextType(association.ContextType?.Trim());
                if (contextType is null)
                {
                    return ValidationFailed(JobOnValidationErrors.AssociationActionInvalid);
                }

                if (!Enum.TryParse<ToolAssociationAction>(association.Action?.Trim(), ignoreCase: false, out var action))
                {
                    return ValidationFailed(JobOnValidationErrors.AssociationActionInvalid);
                }

                associations.Add(new ToolAssociationChange(contextType.Value, action, association.ToolId));
            }

            var command = new UpdateJobOnCommand(
                jobonId,
                body.ExpectedVersion,
                body.Reference ?? string.Empty,
                body.ProductionNumber ?? string.Empty,
                body.Machine ?? string.Empty,
                body.ProductionDate,
                associations,
                body.DateThresholdWarningAcknowledged);

            return await ExecuteAsync(
                token => service.UpdateAsync(command, token),
                logger,
                cancellationToken);
        });

        // Route 10 — duplication from an explicitly chosen source, guarded by the previewed version.
        manage.MapPost("/{jobonId:guid}/duplicate", async (
            Guid jobonId,
            DuplicateJobOnRequest? body,
            IJobOnService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(JobOnValidationErrors.ProductionNumberRequired);
            }

            var command = new DuplicateJobOnCommand(
                jobonId,
                body.ExpectedSourceVersion,
                body.ProductionNumber ?? string.Empty,
                body.Machine ?? string.Empty,
                body.ProductionDate);

            return await ExecuteAsync(
                token => service.DuplicateAsync(command, token),
                logger,
                cancellationToken);
        });

        // Route 11 — delete with explicit confirmation, the stronger date acknowledgement and the
        // registered dependency probes. Nothing ever cascades into operational history. The
        // confirmation flags are optional query carriers: a MISSING confirmation must reach the
        // validator and produce the contracted 400 `validation-failed` + `DELETE_NOT_CONFIRMED`,
        // never a transport-level binding error.
        manage.MapDelete("/{jobonId:guid}", async (
            Guid jobonId,
            int? expectedVersion,
            bool? deleteConfirmed,
            bool? dateThresholdAcknowledged,
            IJobOnService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var command = new DeleteJobOnCommand(
                jobonId,
                expectedVersion ?? 0,
                deleteConfirmed ?? false,
                dateThresholdAcknowledged ?? false);

            return await ExecuteAsync(
                token => service.DeleteAsync(command, token),
                logger,
                cancellationToken);
        });

        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        Func<CancellationToken, Task<JobOnResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var result = await operation(cancellationToken);

        // Status-level observability only: never payloads, never secrets.
        if (result is JobOnResult.Refused(var reason, var message, _, _))
        {
            logger.LogWarning("Job On operation refused ({Reason}): {Message}", reason, message);
        }

        return MapResult(result);
    }

    private static IResult MapResult(JobOnResult result) => result switch
    {
        JobOnResult.ProductionsFound(var productions) => Results.Ok(new JobOnProductionListResponse(
            productions.Select(ToItemResponse).ToArray())),

        JobOnResult.Ficha(var ficha) => Results.Ok(ToFichaResponse(ficha)),

        JobOnResult.DuplicationPreview(var sourceJobOnId, var sourceVersion, var source) => Results.Ok(
            new JobOnDuplicationPreviewResponse(sourceJobOnId, sourceVersion, ToFichaResponse(source))),

        JobOnResult.Created(var jobOnId, var version) => Results.Created(
            $"{JobOnBasePath}/{jobOnId}",
            new JobOnCreatedResponse(jobOnId, version)),

        JobOnResult.Updated(var jobOnId, var version) => Results.Ok(new JobOnUpdatedResponse(jobOnId, version)),

        JobOnResult.Duplicated(var jobOnId, var sourceJobOnId, var version) => Results.Created(
            $"{JobOnBasePath}/{jobOnId}",
            new JobOnDuplicatedResponse(jobOnId, sourceJobOnId, version)),

        JobOnResult.Deleted => Results.NoContent(),

        JobOnResult.ValidationFailed(var errors) => ValidationFailed(errors),

        JobOnResult.NotFound(var jobOnId) => Results.NotFound(new { reason = "not-found", jobOnId }),

        JobOnResult.Refused(var reason, var message, var existingJobOnId, var dependencies) =>
            Results.Conflict(new JobOnRefusalResponse(
                RefusalToken(reason),
                message,
                existingJobOnId,
                dependencies?
                    .Select(dependency => new JobOnDependencyResponse(dependency.Kind, dependency.Description))
                    .ToArray())),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult ValidationFailed(params string[] errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    private static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    /// <summary>The exact transport token of a refusal reason (contract §14.2).</summary>
    public static string RefusalToken(JobOnRefusalReason reason) => reason switch
    {
        JobOnRefusalReason.StaleVersion => "stale-version",
        JobOnRefusalReason.DuplicateProduction => "duplicate-production",
        JobOnRefusalReason.DependencyExists => "dependency-exists",
        JobOnRefusalReason.DateThresholdConfirmationRequired => "date-threshold-confirmation-required",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown refusal reason."),
    };

    /// <summary>The exact transport token of a Peso PDF open refusal (outputs slice).</summary>
    public static string OpenRefusalToken(JobOnControlOutputRefusalReason reason) => reason switch
    {
        JobOnControlOutputRefusalReason.PdfDirectoryNotConfigured => "pdf-directory-not-configured",
        JobOnControlOutputRefusalReason.WorkspaceUnavailable => "workspace-unavailable",
        JobOnControlOutputRefusalReason.InvalidFileName => "invalid-file-name",
        JobOnControlOutputRefusalReason.ReadFailed => "document-read-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown open refusal reason."),
    };

    private static JobOnProductionItemResponse ToItemResponse(JobOnProductionListItem item) => new(
        item.JobOnId,
        item.Reference,
        item.ProductionNumber,
        item.Machine,
        item.ProductionDate);

    private static JobOnFichaResponse ToFichaResponse(JobOnFicha ficha) => new(
        ficha.JobOnId,
        ficha.Reference,
        ficha.ProductionNumber,
        ficha.Machine,
        ficha.ProductionDate,
        ficha.CopiedFromJobOnId,
        ficha.Version,
        ficha.Contexts.Select(context => new JobOnContextResponse(
            ToolTokens.ToToken(context.ContextType),
            context.ContextId,
            context.ToolId,
            ToolTokens.ToToken(context.ToolType),
            context.ToolReference,
            context.ToolLot,
            new JobOnToolProjectionResponse(
                context.Tool.ToolId,
                ToolTokens.ToToken(context.Tool.Type),
                context.Tool.Reference,
                context.Tool.Lot,
                ToolTokens.ToToken(context.Tool.Processo),
                context.Tool.Quantity,
                context.Tool.CompatibleMachines.Select(machine => machine.Value).ToArray()))).ToArray());

    /// <summary>Job On create transport shape.</summary>
    public sealed record CreateJobOnRequest(
        string? Reference,
        string? ProductionNumber,
        string? Machine,
        DateOnly? ProductionDate,
        Guid? CmToolId,
        Guid? MfToolId,
        Guid? BqToolId);

    /// <summary>
    /// Job On edit transport shape.
    /// </summary>
    /// <remarks>
    /// The explicit <see cref="Associations"/> list is the association mechanism. No nullable Tool id
    /// is carried here: a nullable id has no independent association meaning, and a parameter that
    /// silently did nothing would be a second, weaker association path.
    /// </remarks>
    public sealed record UpdateJobOnRequest(
        int ExpectedVersion,
        string? Reference,
        string? ProductionNumber,
        string? Machine,
        DateOnly? ProductionDate,
        IReadOnlyList<ToolAssociationChangeRequest>? Associations,
        bool DateThresholdWarningAcknowledged);

    /// <summary>One explicit association change (<c>CM</c>/<c>MF</c>/<c>BQ</c> + Keep/Set/Remove).</summary>
    public sealed record ToolAssociationChangeRequest(string? ContextType, string? Action, Guid? ToolId);

    /// <summary>Job On duplication transport shape.</summary>
    public sealed record DuplicateJobOnRequest(
        int ExpectedSourceVersion,
        string? ProductionNumber,
        string? Machine,
        DateOnly? ProductionDate);

    /// <summary>Reference → productions response.</summary>
    public sealed record JobOnProductionListResponse(IReadOnlyList<JobOnProductionItemResponse> Productions);

    /// <summary>One matching production occurrence: identity plus human production facts only.</summary>
    public sealed record JobOnProductionItemResponse(
        Guid JobonId,
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate);

    /// <summary>Job On create response.</summary>
    public sealed record JobOnCreatedResponse(Guid JobonId, int Version);

    /// <summary>Job On edit response.</summary>
    public sealed record JobOnUpdatedResponse(Guid JobonId, int Version);

    /// <summary>Job On duplication response.</summary>
    public sealed record JobOnDuplicatedResponse(Guid JobonId, Guid SourceJobOnId, int Version);

    /// <summary>Read-only duplication preview response.</summary>
    public sealed record JobOnDuplicationPreviewResponse(
        Guid SourceJobOnId,
        int SourceVersion,
        JobOnFichaResponse Source);

    /// <summary>Typed, actionable refusal response.</summary>
    public sealed record JobOnRefusalResponse(
        string Reason,
        string Message,
        Guid? ExistingJobOnId,
        IReadOnlyList<JobOnDependencyResponse>? Dependencies);

    /// <summary>One reported dependency.</summary>
    public sealed record JobOnDependencyResponse(string Kind, string Description);

    /// <summary>Typed, actionable refusal response of the Peso PDF open route (outputs slice).</summary>
    public sealed record PesoPdfOpenRefusalResponse(string Reason, string Message);

    /// <summary>Job On ficha response.</summary>
    public sealed record JobOnFichaResponse(
        Guid JobonId,
        string Reference,
        string ProductionNumber,
        string Machine,
        DateOnly? ProductionDate,
        Guid? CopiedFromJobOnId,
        int Version,
        IReadOnlyList<JobOnContextResponse> Contexts);

    /// <summary>One context: context identity, canonical relation, frozen triple and live projection.</summary>
    public sealed record JobOnContextResponse(
        string ContextType,
        Guid ContextId,
        Guid ToolId,
        string ToolType,
        string ToolReference,
        string ToolLot,
        JobOnToolProjectionResponse Tool);

    /// <summary>The live Tool facts of the referenced Tool, composed at read time.</summary>
    public sealed record JobOnToolProjectionResponse(
        Guid ToolId,
        string Type,
        string Reference,
        string Lot,
        string? Processo,
        int? Quantity,
        IReadOnlyList<string> CompatibleMachines);
}
