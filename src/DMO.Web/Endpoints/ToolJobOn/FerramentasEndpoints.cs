using DMO.Application.Access;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints.ToolJobOn;

/// <summary>
/// Minimal API surface of the contextual Ferramentas Light capability (canonical Tool search/create).
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §13.2 routes 12 and 13 (with route 14 as the contextual ficha page).
/// <para>
/// Every route carries exactly one policy — <c>dmo.module.ferramentas</c>. Ferramentas is
/// contextual-only: it has no destination id, it gains no top-level destination and it is absent from
/// navigation; these routes are reachable only from an operational context that holds the Ferramentas
/// grant. No route carries <c>ferramentas-approve</c>, because P2-T04 implements no approval
/// capability.
/// </para>
/// <para>
/// The endpoints are thin: they bind transport shapes, call the application service and map the
/// closed result set to HTTP. No domain rule and no persistence live here, and observability is
/// restricted to status-level warnings (never payloads or secrets).
/// </para>
/// </remarks>
public static class FerramentasEndpoints
{
    /// <summary>Base path of the contextual Ferramentas surface.</summary>
    public const string FerramentasBasePath = "/ferramentas";

    /// <summary>Non-static logger category marker (static types cannot be generic arguments).</summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the contextual Ferramentas endpoints onto the application.</summary>
    public static WebApplication MapFerramentasEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(FerramentasBasePath)
            .RequireAuthorization(ModuleAuthorizationPolicies.PolicyName(ModuleCatalog.Ferramentas));

        // Route 12 — bounded canonical Tool search/list. A denied caller is denied, never shown an
        // empty list, and a validation failure is never aliased to "no results".
        group.MapGet("/tools", async (
            string? query,
            string? type,
            string? reference,
            string? lot,
            string? machine,
            int? limit,
            IToolService service,
            CancellationToken cancellationToken) =>
        {
            var parsedType = ParseToolType(type, out var typeError);
            if (typeError is not null)
            {
                return ValidationFailed(typeError);
            }

            var parsedMachine = ParseMachine(machine, out var machineError);
            if (machineError is not null)
            {
                return ValidationFailed(machineError);
            }

            var searchQuery = new ToolSearchQuery(
                query,
                parsedType,
                reference,
                lot,
                parsedMachine,
                limit ?? 0);

            var result = await service.SearchAsync(searchQuery, cancellationToken);

            return result is ToolResult.SearchResults(var items)
                ? Results.Ok(new ToolSearchResponse(
                    items.Select(ToItemResponse).ToArray(),
                    searchQuery.Limit))
                : MapResult(result);
        });

        // Route 13 — create the canonical Tool and return its REAL canonical identity. The origin
        // surface never mints an identity, and no provisional or display-key identity is returned.
        group.MapPost("/tools", async (
            CreateToolRequest? body,
            IToolService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest(new
                {
                    reason = "validation-failed",
                    errors = new[] { ToolValidationErrors.ToolTypeRequired },
                });
            }

            var command = new CreateToolCommand(
                body.Type,
                body.Reference,
                body.Lot,
                body.Processo,
                body.Quantity,
                body.Machines);

            var result = await service.CreateAsync(command, cancellationToken);

            if (result is ToolResult.DuplicateIdentity(var existingToolId, var message))
            {
                logger.LogWarning("Canonical Tool create refused: identity already exists ({ExistingToolId}).", existingToolId);
            }

            return MapResult(result);
        });

        return app;
    }

    private static IResult MapResult(ToolResult result) => result switch
    {
        ToolResult.SearchResults(var items) => Results.Ok(new ToolSearchResponse(
            items.Select(ToItemResponse).ToArray(),
            items.Count)),

        ToolResult.Created(var toolId) => Results.Created(
            $"{FerramentasBasePath}/tools/{toolId}",
            new ToolCreatedResponse(toolId)),

        ToolResult.Found(var ficha) => Results.Ok(new ToolFichaResponse(
            ficha.ToolId,
            ToolTokens.ToToken(ficha.Type),
            ficha.Reference,
            ficha.Lot,
            ToolTokens.ToToken(ficha.Processo),
            ficha.Quantity,
            ficha.CompatibleMachines.Select(machine => machine.Value).ToArray(),
            ficha.UsageOccurrences
                .Select(usage => new ToolUsageResponse(
                    usage.JobOnId, usage.Reference, usage.ProductionNumber, usage.Machine))
                .ToArray())),

        ToolResult.ValidationFailed(var errors) => ValidationFailed(errors),

        ToolResult.NotFound(var toolId) => Results.NotFound(new { reason = "not-found", toolId }),

        ToolResult.DuplicateIdentity(var existingToolId, var message) => Results.Conflict(new
        {
            reason = "duplicate-identity",
            message,
            existingToolId,
        }),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult ValidationFailed(params string[] errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    private static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Results.BadRequest(new { reason = "validation-failed", errors });

    private static ToolType? ParseToolType(string? value, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (ToolTokens.ParseType(value.Trim()) is { } type)
        {
            return type;
        }

        // An unparseable filter is refused, never silently ignored: ignoring it would widen the
        // result set behind the operator's back.
        error = ToolValidationErrors.ToolTypeUnknown;
        return null;
    }

    private static MachineCode? ParseMachine(string? value, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (MachineCode.Parse(value.Trim()) is { } machine)
        {
            return machine;
        }

        error = ToolValidationErrors.MachineUnknown;
        return null;
    }

    private static ToolSearchItemResponse ToItemResponse(ToolSearchItem item) => new(
        item.ToolId,
        ToolTokens.ToToken(item.Type),
        item.Reference,
        item.Lot,
        ToolTokens.ToToken(item.Processo),
        item.Quantity,
        item.CompatibleMachines.Select(machine => machine.Value).ToArray());

    /// <summary>Create transport shape: the six Beta minimum Tool facts.</summary>
    public sealed record CreateToolRequest(
        string? Type,
        string? Reference,
        string? Lot,
        string? Processo,
        int? Quantity,
        IReadOnlyList<string>? Machines);

    /// <summary>Bounded Tool search response.</summary>
    public sealed record ToolSearchResponse(IReadOnlyList<ToolSearchItemResponse> Items, int Limit);

    /// <summary>One canonical Tool search item: identity plus Tool-owned facts only.</summary>
    public sealed record ToolSearchItemResponse(
        Guid ToolId,
        string Type,
        string Reference,
        string Lot,
        string? Processo,
        int? Quantity,
        IReadOnlyList<string> CompatibleMachines);

    /// <summary>Tool create response: the real canonical identity.</summary>
    public sealed record ToolCreatedResponse(Guid ToolId);

    /// <summary>Contextual Tool ficha response.</summary>
    public sealed record ToolFichaResponse(
        Guid ToolId,
        string Type,
        string Reference,
        string Lot,
        string? Processo,
        int? Quantity,
        IReadOnlyList<string> CompatibleMachines,
        IReadOnlyList<ToolUsageResponse> UsageOccurrences);

    /// <summary>One Job On occurrence using a canonical Tool.</summary>
    public sealed record ToolUsageResponse(
        Guid JobonId,
        string Reference,
        string ProductionNumber,
        string Machine);
}
