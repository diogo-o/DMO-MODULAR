using DMO.Application.Accounts;
using DMO.Application.UserAdministration;
using DMO.Web.Authorization;

namespace DMO.Web.Endpoints.Administration;

/// <summary>
/// Minimal API surface of the ADMIN-only USER administration (P1-T05).
/// </summary>
/// <remarks>
/// <para>
/// Every route carries the single administration policy
/// (<see cref="AdministrationAuthorizationPolicies.PolicyName"/>, <c>dmo.administration</c>):
/// only an active ADMIN session passes; a USER (any role/Template/Modules), a session-less
/// caller and an inactive/unresolved account are denied by the handler.
/// </para>
/// <para>
/// The endpoints are thin: they bind transport shapes, call the application service and map
/// the closed result set to HTTP (200/201/400/404/409/502/503). No business rule, no
/// provider call and no persistence live here; observability is restricted to status-level
/// warnings (never payloads or secrets). The Razor pages implement the same operations
/// through the same service.
/// </para>
/// <para>
/// Response payloads carry application facts only; <c>auth_identity_id</c> (internal provider
/// linkage) is never exposed.
/// </para>
/// </remarks>
public static class UserAdministrationEndpoints
{
    /// <summary>Base path of the USER administration surface.</summary>
    public const string UsersBasePath = "/administration/users";

    /// <summary>
    /// Non-static logger category marker: static types cannot be used as generic type
    /// arguments, so endpoint logging uses <see cref="ILogger{T}"/> over this marker.
    /// </summary>
    public sealed class LoggerCategory;

    /// <summary>Maps the ADMIN-only USER administration endpoints onto the application.</summary>
    public static WebApplication MapUserAdministrationEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup(UsersBasePath)
            .RequireAuthorization(AdministrationAuthorizationPolicies.PolicyName);

        group.MapGet("", async (
            IUserAdministrationService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        group.MapPost("", async (
            CreateUserRequest? body,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new UserAdministrationCommands.CreateUserCommand(
                body.Name, body.CompanyNumber, body.Email, body.Role,
                body.Active, body.TemplateId, body.InviteRedirectUrl);
            return await ExecuteAsync(
                token => service.CreateAsync(command, token),
                logger,
                cancellationToken);
        });

        group.MapGet("/templates", async (
            IUserAdministrationService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListTemplateOptionsAsync(cancellationToken)));

        group.MapGet("/{userId:guid}", async (
            Guid userId,
            IUserAdministrationService service,
            CancellationToken cancellationToken) =>
        {
            var ficha = await service.GetAsync(userId, cancellationToken);
            return ficha is null ? Results.NotFound() : Results.Ok(ficha);
        });

        group.MapPut("/{userId:guid}", async (
            Guid userId,
            UpdateUserRequest? body,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new UserAdministrationCommands.UpdateUserCommand(
                userId, body.Name, body.CompanyNumber, body.Email, body.Role, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.UpdateAsync(command, token),
                logger,
                cancellationToken);
        });

        group.MapPost("/{userId:guid}/activate", async (
            Guid userId,
            SetActiveRequest? body,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new UserAdministrationCommands.SetUserActiveCommand(
                userId, Active: true, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.SetActiveAsync(command, token),
                logger,
                cancellationToken);
        });

        group.MapPost("/{userId:guid}/deactivate", async (
            Guid userId,
            SetActiveRequest? body,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new UserAdministrationCommands.SetUserActiveCommand(
                userId, Active: false, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.SetActiveAsync(command, token),
                logger,
                cancellationToken);
        });

        group.MapPut("/{userId:guid}/template", async (
            Guid userId,
            SetTemplateRequest? body,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new UserAdministrationCommands.SetUserTemplateCommand(
                userId, body.TemplateId, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.SetTemplateAsync(command, token),
                logger,
                cancellationToken);
        });

        group.MapPost("/{userId:guid}/reset-password", async (
            Guid userId,
            ConfirmedActionRequest? body,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new UserAdministrationCommands.RequestedPasswordResetCommand(
                userId, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.InitiatePasswordResetAsync(command, token),
                logger,
                cancellationToken);
        });

        group.MapPost("/{userId:guid}/resend-invite", async (
            Guid userId,
            ConfirmedActionRequest? body,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
            {
                return Results.BadRequest();
            }

            var command = new UserAdministrationCommands.ResendInviteCommand(
                userId, body.ExpectedVersion);
            return await ExecuteAsync(
                token => service.ResendInviteAsync(command, token),
                logger,
                cancellationToken);
        });

        group.MapDelete("/{userId:guid}", async (
            Guid userId,
            [Microsoft.AspNetCore.Mvc.FromQuery] int expectedVersion,
            IUserAdministrationService service,
            ILogger<LoggerCategory> logger,
            CancellationToken cancellationToken) =>
        {
            var command = new UserAdministrationCommands.DeleteUserCommand(userId, expectedVersion);
            return await ExecuteAsync(
                token => service.DeleteAsync(command, token),
                logger,
                cancellationToken);
        });

        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        Func<CancellationToken, Task<UserAdministrationResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var result = await operation(cancellationToken);

        // Status-level observability only: never payloads, never secrets.
        if (result is UserAdministrationResult.Conflict(var reason, var message))
        {
            logger.LogWarning("User administration conflict ({Reason}): {Message}", reason, message);
        }
        else if (result is UserAdministrationResult.ProviderFailed(var kind))
        {
            logger.LogWarning("User administration provider failure ({Kind}).", kind);
        }

        return MapResult(result);
    }

    private static IResult MapResult(UserAdministrationResult result) => result switch
    {
        UserAdministrationResult.Success(var account) => Results.Ok(ToResponse(account)),
        UserAdministrationResult.Created(var account) =>
            Results.Created($"{UsersBasePath}/{account.AccountId}", ToResponse(account)),
        UserAdministrationResult.RecoveredProvisioning(var account) =>
            Results.Created($"{UsersBasePath}/{account.AccountId}", ToResponse(account)),
        UserAdministrationResult.IdempotentReplay(var account) => Results.Ok(ToResponse(account)),
        UserAdministrationResult.ValidationFailed(var errors) => Results.BadRequest(new { errors }),
        UserAdministrationResult.NotFound(var userId) => Results.NotFound(new { userId }),
        UserAdministrationResult.Conflict(var reason, var message) =>
            Results.Conflict(new { reason = reason.ToString(), message }),
        UserAdministrationResult.ProviderFailed(var kind) =>
            kind == ProviderFailureKind.Unavailable
                ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
                : Results.StatusCode(StatusCodes.Status502BadGateway),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static UserResponse ToResponse(UserAccount account) => new(
        account.AccountId,
        account.DisplayName,
        account.CompanyNumber,
        account.Email,
        account.RoleLabel,
        account.IsActive,
        account.TemplateId,
        account.Version);

    /// <summary>Create transport shape. No password field exists — initial credentials come from the provider invitation.</summary>
    public sealed record CreateUserRequest(
        string Name,
        string CompanyNumber,
        string Email,
        string Role,
        bool Active,
        Guid? TemplateId,
        string? InviteRedirectUrl);

    /// <summary>Edit transport shape (application facts + expected version).</summary>
    public sealed record UpdateUserRequest(
        string Name,
        string CompanyNumber,
        string Email,
        string Role,
        int ExpectedVersion);

    /// <summary>Activate/deactivate transport shape (the action verb is in the route).</summary>
    public sealed record SetActiveRequest(int ExpectedVersion);

    /// <summary>Template assignment/reassignment/removal transport shape.</summary>
    public sealed record SetTemplateRequest(Guid? TemplateId, int ExpectedVersion);

    /// <summary>Explicit-confirmation transport shape for reset/resend (expected version from the confirmed ficha).</summary>
    public sealed record ConfirmedActionRequest(int ExpectedVersion);

    /// <summary>USER response payload (no internal provider linkage).</summary>
    public sealed record UserResponse(
        Guid UserId,
        string Name,
        string CompanyNumber,
        string Email,
        string Role,
        bool Active,
        Guid? TemplateId,
        int Version);
}