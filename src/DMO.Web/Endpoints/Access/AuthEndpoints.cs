using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Application.Session;
using DMO.Web.Auth;

namespace DMO.Web.Endpoints.Access;

/// <summary>
/// Authentication and current-account surface.
/// </summary>
/// <remarks>
/// <para>
/// P1-T03 posture: <c>POST /auth/login</c> performs real authentication (ADMIN through
/// Supabase Auth DEV/TEST; USER through the persistence-backed company_number → carrier
/// email → Supabase password grant), then real account resolution against the persisted
/// mapping (<c>PersistenceAccountLookup</c>). A session is established <b>only</b> when
/// authentication succeeds <b>and</b> resolution returns an active ADMIN/USER; any other
/// outcome fails closed — the surface can never be mistaken for completed login
/// functionality when no mapping or no active account exists.
/// </para>
/// <para>
/// P1-T07 accepted extraction: the common login orchestration (authenticate → resolve
/// application account → establish session only when allowed) lives in
/// <see cref="SessionLoginService"/> and is shared by this endpoint and the public
/// <c>/Login</c> page. This endpoint delegates to the service and keeps its exact
/// status/response contracts unchanged.
/// </para>
/// <para>
/// <c>POST /auth/logout</c> only clears the runtime session state. <c>GET /auth/me</c> is
/// read-only and never grants access.
/// </para>
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>Path of the login endpoint.</summary>
    public const string LoginPath = "/auth/login";

    /// <summary>Path of the logout endpoint.</summary>
    public const string LogoutPath = "/auth/logout";

    /// <summary>Path of the current-account endpoint.</summary>
    public const string CurrentAccountPath = "/auth/me";

    /// <summary>Maps the authentication/current-account endpoints onto the application.</summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(LoginPath, async (
            SessionLoginService.LoginRequest? body,
            SessionLoginService loginService,
            CancellationToken cancellationToken) =>
        {
            var result = await loginService.LoginAsync(body, cancellationToken);

            // Exact accepted status contracts: invalid shape → 400; provider failures keep
            // their mapped statuses; resolution no-access → 403; success → 200. The session
            // was already established only for the success outcome (service contract).
            return result switch
            {
                SessionLoginResult.InvalidRequest => Results.BadRequest(),
                SessionLoginResult.AuthenticationFailed(var failure) => failure switch
                {
                    AuthenticationFailureReason.InvalidCredentials => Results.Unauthorized(),
                    AuthenticationFailureReason.ProviderUnavailable => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
                    AuthenticationFailureReason.ProviderError => Results.StatusCode(StatusCodes.Status502BadGateway),
                    _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
                },
                SessionLoginResult.NoAccess => Results.StatusCode(StatusCodes.Status403Forbidden),
                SessionLoginResult.Established => Results.Ok(),
                _ => Results.BadRequest(),
            };
        });

        app.MapPost(LogoutPath, async (
            ISessionAuthentication session,
            CancellationToken cancellationToken) =>
        {
            await session.SignOutAsync(cancellationToken);
            return Results.Ok();
        });

        app.MapGet(CurrentAccountPath, async (
            ICurrentAccountContext currentAccountContext,
            CancellationToken cancellationToken) =>
        {
            var current = await currentAccountContext.GetCurrentAsync(cancellationToken);

            return current switch
            {
                CurrentAccount.Admin(var account) => Results.Ok(new MeResponse(
                    AccountType: "admin",
                    DisplayName: account.DisplayName,
                    Email: account.Email,
                    CompanyNumber: null,
                    RoleLabel: null)),
                CurrentAccount.User(var account) => Results.Ok(new MeResponse(
                    AccountType: "user",
                    DisplayName: account.DisplayName,
                    Email: account.Email,
                    CompanyNumber: account.CompanyNumber,
                    RoleLabel: account.RoleLabel)),
                _ => Results.Ok(new MeResponse(
                    AccountType: "none",
                    DisplayName: null,
                    Email: null,
                    CompanyNumber: null,
                    RoleLabel: null)),
            };
        });

        return app;
    }

    /// <summary>Minimal read-only current-account payload. Carries no Template/access facts.</summary>
    /// <param name="AccountType"><c>admin</c>, <c>user</c>, or the state <c>none</c>.</param>
    /// <param name="DisplayName">Application account display name when resolved.</param>
    /// <param name="Email">Account email when resolved (ADMIN uses it as identifier; USER email is independent).</param>
    /// <param name="CompanyNumber">USER company number when resolved.</param>
    /// <param name="RoleLabel">USER presentation-only role label when resolved.</param>
    public sealed record MeResponse(
        string AccountType,
        string? DisplayName,
        string? Email,
        string? CompanyNumber,
        string? RoleLabel);
}