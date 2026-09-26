using DMO.Application.Boquilhas;
using ControloDefinicoesErrors = DMO.Application.Controlo.Settings.ControloDefinicoesValidationErrors;

namespace DMO.Web.Endpoints;

/// <summary>
/// The <c>Boquilhas > Definições</c> minimal-API surface: the repairer register and the independent
/// line/machine → repairer assignments — the repairer family owned by Boquilhas (Owner
/// clarification P2-T07 §34.3; P2-T05 §31.3; supersedes the affected P2-T05/P2-T07 ownership
/// wording). Controlo_Create is no longer the operational surface of this family; Admin gains
/// nothing (the canonical <c>boquilhas</c> Module gate).
/// </summary>
/// <remarks>
/// <para>
/// Every route declares exactly ONE canonical Module policy — the SAME
/// <c>BoquilhasEndpoints.Policy</c> (<c>dmo.module.boquilhas</c>) as every other Boquilhas route.
/// The physical <c>repairers</c>/<c>machine_repairer_assignments</c> tables stay exactly where they
/// are — this surface is ownership/service/UI only, no schema migration (Owner rule). The
/// PDF/email/document settings are NOT here; they remain under
/// <c>Controlo_Create → Definições</c>.</para>
/// <para>
/// Shape rules of the closed model, unchanged as shape: name is the only required repairer data
/// (no delete path), ONE independent assignment per machine (B1/B2/B3/C1/C2/C3 — changing one
/// machine never touches another), no grouping, current-state only; historical movement facts are
/// frozen and a default-repairer change never rewrites them.</para>
/// </remarks>
public static class BoquilhasDefinicoesEndpoints
{
    /// <summary>Base path of the Boquilhas Definições surface (inside the Boquilhas module).</summary>
    public const string DefinicoesBasePath = "/boquilhas/definicoes";

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the Boquilhas Definições endpoints onto the application.</summary>
    public static WebApplication MapBoquilhasDefinicoesEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(DefinicoesBasePath)
            .RequireAuthorization(BoquilhasEndpoints.Policy);

        // Repairers: list / add / rename (name is the only required data; the same repairer_id is
        // retained across the rename; no delete path exists).
        group.MapGet("/repairers", async (
            IBoquilhasDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListRepairersAsync(cancellationToken);

            return result is BoquilhasDefinicoesResult.RepairersFound(var repairers)
                ? Results.Ok(new RepairersResponse(
                    repairers
                        .Select(repairer => new RepairerItemResponse(
                            repairer.RepairerId,
                            repairer.Name,
                            repairer.Version))
                        .ToArray()))
                : MapResult(result);
        });

        group.MapPost("/repairers", async (
            CreateRepairerRequest? body,
            IBoquilhasDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesErrors.NameRequired);
            }

            var command = new CreateRepairerCommand(body.Name ?? string.Empty);

            return await ExecuteAsync(
                token => service.CreateRepairerAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is BoquilhasDefinicoesResult.RepairerCreated(var id, var version)
                    ? Results.Created(
                        $"{DefinicoesBasePath}/repairers/{id}",
                        new RepairerCreatedResponse(id, version))
                    : null);
        });

        group.MapPut("/repairers/{repairerId:guid}", async (
            Guid repairerId,
            RenameRepairerRequest? body,
            IBoquilhasDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesErrors.NameRequired);
            }

            var command = new RenameRepairerCommand(
                repairerId,
                body.ExpectedVersion,
                body.Name ?? string.Empty);

            return await ExecuteAsync(
                token => service.RenameRepairerAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is BoquilhasDefinicoesResult.RepairerRenamed(var id, var version)
                    ? Results.Ok(new RepairerRenamedResponse(id, version))
                    : null);
        });

        // Machine assignments: list all six / set-change-clear ONE independently. A null repairer
        // id clears the assignment (explicit operator action); the other five machines are never
        // touched.
        group.MapGet("/machine-assignments", async (
            IBoquilhasDefinicoesService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListMachineAssignmentsAsync(cancellationToken);

            return result is BoquilhasDefinicoesResult.AssignmentsFound(var assignments)
                ? Results.Ok(new MachineAssignmentsResponse(
                    assignments
                        .Select(assignment => new MachineAssignmentItemResponse(
                            assignment.Machine,
                            assignment.RepairerId,
                            assignment.Version))
                        .ToArray()))
                : MapResult(result);
        });

        group.MapPut("/machine-assignments/{machine}", async (
            string machine,
            SetMachineAssignmentRequest? body,
            IBoquilhasDefinicoesService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return ValidationFailed(ControloDefinicoesErrors.MachineUnknown);
            }

            var command = new SetMachineAssignmentCommand(
                machine,
                body.RepairerId,
                body.ExpectedVersion);

            return await ExecuteAsync(
                token => service.SetMachineAssignmentAsync(command, token),
                logger,
                cancellationToken,
                success: result => result is BoquilhasDefinicoesResult.AssignmentSet(var setMachine, var version)
                    ? Results.Ok(new MachineAssignmentResponse(setMachine, version))
                    : result is BoquilhasDefinicoesResult.AssignmentCleared(var clearedMachine)
                        ? Results.Ok(new MachineAssignmentResponse(clearedMachine, Version: 0))
                        : null);
        });

        return app;
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<IResult> ExecuteAsync(
        Func<CancellationToken, Task<BoquilhasDefinicoesResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken,
        Func<BoquilhasDefinicoesResult, IResult?>? success = null)
    {
        var result = await operation(cancellationToken);

        // Status-level observability only: never payloads, never secrets.
        if (result is BoquilhasDefinicoesResult.Refused(var reason, var message))
        {
            logger.LogWarning("Boquilhas Definições operation refused ({Reason}): {Message}", reason, message);
        }

        if (success is not null && success(result) is { } mapped)
        {
            return mapped;
        }

        return MapResult(result);
    }

    private static IResult MapResult(BoquilhasDefinicoesResult result) => result switch
    {
        BoquilhasDefinicoesResult.RepairersFound(var repairers) => Results.Ok(new RepairersResponse(
            repairers
                .Select(repairer => new RepairerItemResponse(
                    repairer.RepairerId,
                    repairer.Name,
                    repairer.Version))
                .ToArray())),

        BoquilhasDefinicoesResult.RepairerCreated(var id, var version) => Results.Created(
            $"{DefinicoesBasePath}/repairers/{id}",
            new RepairerCreatedResponse(id, version)),

        BoquilhasDefinicoesResult.RepairerRenamed(var id, var version) => Results.Ok(
            new RepairerRenamedResponse(id, version)),

        BoquilhasDefinicoesResult.AssignmentsFound(var assignments) => Results.Ok(
            new MachineAssignmentsResponse(
                assignments
                    .Select(assignment => new MachineAssignmentItemResponse(
                        assignment.Machine,
                        assignment.RepairerId,
                        assignment.Version))
                    .ToArray())),

        BoquilhasDefinicoesResult.AssignmentSet(var machine, var version) => Results.Ok(
            new MachineAssignmentResponse(machine, version)),

        BoquilhasDefinicoesResult.AssignmentCleared(var machine) => Results.Ok(
            new MachineAssignmentResponse(machine, Version: 0)),

        BoquilhasDefinicoesResult.ValidationFailed(var errors) => ValidationFailed(errors),

        BoquilhasDefinicoesResult.NotFound(var id) => Results.NotFound(new { reason = "not-found", id }),

        BoquilhasDefinicoesResult.Refused(_, var message) =>
            Results.Conflict(new { reason = "stale-version", message }),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult ValidationFailed(string error) =>
        ValidationFailed(new[] { error });

    private static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    // ---------------------------------------------------------------------------------------------
    // Transport shapes
    // ---------------------------------------------------------------------------------------------

    /// <summary>Add-repairer carrier: name is the only required data.</summary>
    public sealed record CreateRepairerRequest(string? Name);

    /// <summary>Rename-repairer carrier.</summary>
    public sealed record RenameRepairerRequest(int ExpectedVersion, string? Name);

    /// <summary>One-machine assignment carrier: a null repairer id clears the assignment.</summary>
    public sealed record SetMachineAssignmentRequest(Guid? RepairerId, int? ExpectedVersion);

    /// <summary>Repairers response.</summary>
    public sealed record RepairersResponse(IReadOnlyList<RepairerItemResponse> Repairers);

    /// <summary>One repairer of the list.</summary>
    public sealed record RepairerItemResponse(Guid RepairerId, string Name, int Version);

    /// <summary>Add-repairer response.</summary>
    public sealed record RepairerCreatedResponse(Guid RepairerId, int Version);

    /// <summary>Rename-repairer response.</summary>
    public sealed record RepairerRenamedResponse(Guid RepairerId, int Version);

    /// <summary>Machine-assignments list response (all six machines; absent = no repairer).</summary>
    public sealed record MachineAssignmentsResponse(IReadOnlyList<MachineAssignmentItemResponse> Assignments);

    /// <summary>One machine's current assignment.</summary>
    public sealed record MachineAssignmentItemResponse(string Machine, Guid? RepairerId, int Version);

    /// <summary>Set/clear response.</summary>
    public sealed record MachineAssignmentResponse(string Machine, int Version);
}