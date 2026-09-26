using DMO.Application.TemplateAdministration;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints.Administration;

/// <summary>
/// Minimal API surface of the ADMIN-only Template administration (P1-T06).
/// </summary>
/// <remarks>
/// <para>
/// Every route carries the single administration policy
/// (<see cref="AdministrationAuthorizationPolicies.PolicyName"/>, <c>dmo.administration</c>):
/// the exact same gate P1-T05 established for the USER surface — only an active ADMIN
/// session passes; a USER (any role/Template/Modules), a session-less caller and an
/// inactive/unresolved account are denied by the shared handler.
/// </para>
/// <para>
/// The endpoints are thin: they bind transport shapes, call the application service and map
/// the closed result set to HTTP (200/201/400/404/409). No business rule, no provider call
/// and no persistence live here; observability is restricted to status-level warnings (never
/// payloads or secrets). The Razor pages implement the same operations through the same
/// service.
/// </para>
/// <para>
/// Membership routes assign/remove/reassign through the single <c>users.template_id</c>
/// relation (the same versioned primitive the USER ficha uses); no membership table exists.
/// </para>
/// </remarks>
public static class TemplateAdministrationEndpoints
{
    /// <summary>Base path of the Template administration surface.</summary>
    public const string TemplatesBasePath = "/administration/templates";

    /// <summary>
    /// Non-static logger category marker: static types cannot be used as generic type
    /// arguments, so endpoint logging uses <see cref="ILogger{T}"/> over this marker.
    /// </summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the ADMIN-only Template administration endpoints onto the application.</summary>
    public static WebApplication MapTemplateAdministrationEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(TemplatesBasePath)
            .RequireAuthorization(AdministrationAuthorizationPolicies.PolicyName);

        // List (with composition presentation, landing validity and associated-USER count).
        group.MapGet("", async (
            ITemplateAdministrationService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        // Create (name + ordered module ids + optional landing; landing validated by the service).
        group.MapPost("", async (
            CreateTemplateRequest? body,
            ITemplateAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new TemplateAdministrationCommands.CreateTemplateCommand(
                body.Name, body.ModuleIds, body.LandingDestinationId);
            return await ExecuteAsync(
                token => service.CreateAsync(command, token),
                logger,
                cancellationToken);
        });

        // Ficha (facts, ordered composition, landing validity, associated USERs).
        group.MapGet("/{templateId:guid}", async (
            Guid templateId,
            ITemplateAdministrationService service,
            CancellationToken cancellationToken) =>
        {
            var ficha = await service.GetAsync(templateId, cancellationToken);
            return ficha is null
                ? Results.NotFound(new { templateId })
                : Results.Ok(ToResponse(ficha));
        });

        // Edit (name + ordered module ids + landing, against the ficha's expected version).
        group.MapPut("/{templateId:guid}", async (
            Guid templateId,
            UpdateTemplateRequest? body,
            ITemplateAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new TemplateAdministrationCommands.UpdateTemplateCommand(
                templateId, body.Name, body.ModuleIds, body.LandingDestinationId, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.UpdateAsync(command, token),
                logger,
                cancellationToken);
        });

        // Delete with explicit confirmation (expected version from the confirmed ficha).
        group.MapPost("/{templateId:guid}/delete", async (
            Guid templateId,
            ConfirmedTemplateActionRequest? body,
            ITemplateAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new TemplateAdministrationCommands.DeleteTemplateCommand(templateId, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.DeleteAsync(command, token),
                logger,
                cancellationToken);
        });

        // Assign the USER to this Template (single users.template_id write, versioned).
        group.MapPost("/{templateId:guid}/users/{userId:guid}", async (
            Guid templateId,
            Guid userId,
            SetTemplateUserRequest? body,
            ITemplateAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new TemplateAdministrationCommands.SetTemplateUserCommand(
                templateId, userId, TargetTemplateId: templateId, body.UserExpectedVersion);
            return await ExecuteAsync(
                token => service.SetTemplateUserAsync(command, token),
                logger,
                cancellationToken);
        });

        // Remove the USER's Template association (template_id -> null, versioned).
        group.MapDelete("/{templateId:guid}/users/{userId:guid}", async (
            Guid templateId,
            Guid userId,
            [Microsoft.AspNetCore.Mvc.FromQuery] int userExpectedVersion,
            ITemplateAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var command = new TemplateAdministrationCommands.SetTemplateUserCommand(
                templateId, userId, TargetTemplateId: null, userExpectedVersion);
            return await ExecuteAsync(
                token => service.SetTemplateUserAsync(command, token),
                logger,
                cancellationToken);
        });

        // Reassign the USER A -> B (single write to users.template_id = toId, versioned).
        group.MapPost("/{toTemplateId:guid}/users/{userId:guid}/reassign", async (
            Guid toTemplateId,
            Guid userId,
            SetTemplateUserRequest? body,
            ITemplateAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new TemplateAdministrationCommands.SetTemplateUserCommand(
                toTemplateId, userId, TargetTemplateId: toTemplateId, body.UserExpectedVersion);
            return await ExecuteAsync(
                token => service.SetTemplateUserAsync(command, token),
                logger,
                cancellationToken);
        });

        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        Func<CancellationToken, Task<TemplateAdministrationResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var result = await operation(cancellationToken);

        // Status-level observability only: never payloads, never secrets.
        if (result is TemplateAdministrationResult.Conflict(var reason, var message))
        {
            logger.LogWarning("Template administration conflict ({Reason}): {Message}", reason, message);
        }

        return MapResult(result);
    }

    private static IResult MapResult(TemplateAdministrationResult result) => result switch
    {
        TemplateAdministrationResult.Success(var ficha) => Results.Ok(ToResponse(ficha)),
        TemplateAdministrationResult.Created(var templateId) =>
            Results.Created($"{TemplatesBasePath}/{templateId}", new { templateId }),
        TemplateAdministrationResult.ValidationFailed(var errors) => Results.BadRequest(new { errors }),
        TemplateAdministrationResult.NotFound(var templateId) => Results.NotFound(new { templateId }),
        TemplateAdministrationResult.Conflict(var reason, var message) =>
            Results.Conflict(new { reason = reason.ToString(), message }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static TemplateResponse ToResponse(TemplateFicha ficha) => new(
        ficha.TemplateId,
        ficha.Name,
        ficha.Modules
            .Select(module => new TemplateModuleResponse(
                module.ModuleId, module.DisplayName, module.DestinationId, module.State.ToString()))
            .ToArray(),
        ficha.LandingDestinationId,
        ficha.LandingIsValid,
        ficha.Users
            .Select(user => new TemplateUserMemberResponse(
                user.UserId, user.Name, user.CompanyNumber, user.Role, user.Active, user.Version))
            .ToArray(),
        ficha.Version);

    /// <summary>Create transport shape (name + ordered module ids + optional landing).</summary>
    public sealed record CreateTemplateRequest(
        string Name,
        IReadOnlyList<string> ModuleIds,
        string? LandingDestinationId);

    /// <summary>Edit transport shape (application facts + expected version).</summary>
    public sealed record UpdateTemplateRequest(
        string Name,
        IReadOnlyList<string> ModuleIds,
        string? LandingDestinationId,
        int ExpectedVersion);

    /// <summary>Explicit-confirmation transport shape (expected version from the confirmed ficha).</summary>
    public sealed record ConfirmedTemplateActionRequest(int ExpectedVersion);

    /// <summary>Membership transport shape (the action verb + target are in the route).</summary>
    public sealed record SetTemplateUserRequest(int UserExpectedVersion);

    /// <summary>Template ficha response payload.</summary>
    public sealed record TemplateResponse(
        Guid TemplateId,
        string Name,
        IReadOnlyList<TemplateModuleResponse> Modules,
        string? LandingDestinationId,
        bool LandingIsValid,
        IReadOnlyList<TemplateUserMemberResponse> Users,
        int Version);

    /// <summary>One persisted Template → Module entry with its resolution state.</summary>
    public sealed record TemplateModuleResponse(
        string ModuleId,
        string DisplayName,
        string? DestinationId,
        string State);

    /// <summary>One associated USER (single users.template_id relation).</summary>
    public sealed record TemplateUserMemberResponse(
        Guid UserId,
        string Name,
        string CompanyNumber,
        string Role,
        bool Active,
        int Version);
}