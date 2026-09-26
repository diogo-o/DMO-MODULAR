using DMO.Application.Authentication;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using DMO.IntegrationTests.Auth.Fakes;
using DMO.Web.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DMO.IntegrationTests.Auth;

/// <summary>
/// P1-T03 environment-gated live test — real USER authentication (company_number → persisted
/// carrier email → Supabase password grant) against the DEV/TEST Supabase project.
/// </summary>
/// <remarks>
/// <para>
/// <b>Skipped by default.</b> Like the ADMIN live test, this runs only when the operator
/// explicitly opts in with real DEV/TEST configuration, so it can never silently target the
/// DEV/TEST project from a routine test run.
/// </para>
/// <para>
/// It proves the real <c>company_number → carrier → grant</c> flow when a provisioned carrier
/// identity exists (a <c>users</c> row with <c>company_number</c> whose <c>email</c> is a real
/// Supabase Auth user with the presented password). It never creates or mutates anything, and
/// the company number is never sent to the provider as an email.
/// </para>
/// </remarks>
public sealed class LiveDevTestSupabaseUserAuthTests
{
    /// <summary>Opt-in flag (<c>1</c> or <c>true</c>) enabling this live test.</summary>
    public const string LiveTestEnvironmentVariable = "DMO_SUPABASE_LIVE_TEST";

    /// <summary>DEV/TEST USER company number used only by the live test (never committed).</summary>
    public const string UserCompanyNumberEnvironmentVariable = "DMO_SUPABASE_USER_COMPANY_NUMBER";

    /// <summary>DEV/TEST USER password used only by the live test (never committed).</summary>
    public const string UserPasswordEnvironmentVariable = "DMO_SUPABASE_USER_PASSWORD";

    /// <summary>Database connection (same key the runtime binds) used only by the live test.</summary>
    public const string DatabaseConnectionEnvironmentVariable = "Database__ConnectionString";

    [SkippableFact]
    public async Task UserLogin_LiveDevTestSupabase_AuthenticatesIdentity()
    {
        // Preconditions: explicit opt-in plus real DEV/TEST configuration.
        var enabled = Environment.GetEnvironmentVariable(LiveTestEnvironmentVariable);
        var projectUrl = Environment.GetEnvironmentVariable(SupabaseOptions.ProjectUrlEnvironmentVariable);
        var publishableKey = Environment.GetEnvironmentVariable(SupabaseOptions.PublishableKeyEnvironmentVariable);
        var connectionString = Environment.GetEnvironmentVariable(DatabaseConnectionEnvironmentVariable);
        var companyNumber = Environment.GetEnvironmentVariable(UserCompanyNumberEnvironmentVariable);
        var password = Environment.GetEnvironmentVariable(UserPasswordEnvironmentVariable);

        Skip.If(
            !string.Equals(enabled, "1", StringComparison.Ordinal)
            && !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase),
            $"Set {LiveTestEnvironmentVariable}=1 to run the live DEV/TEST Supabase test; skipped by default.");

        Skip.If(
            string.IsNullOrWhiteSpace(projectUrl)
            || string.IsNullOrWhiteSpace(publishableKey)
            || string.IsNullOrWhiteSpace(connectionString)
            || string.IsNullOrWhiteSpace(companyNumber)
            || string.IsNullOrWhiteSpace(password),
            $"The live test requires {SupabaseOptions.ProjectUrlEnvironmentVariable}, " +
            $"{SupabaseOptions.PublishableKeyEnvironmentVariable}, {DatabaseConnectionEnvironmentVariable}, " +
            $"{UserCompanyNumberEnvironmentVariable} and {UserPasswordEnvironmentVariable}.");

        // The carrier email comes from the real persisted users table (read-only lookup).
        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var context = new DmoDbContext(options);
        var lookup = new UserAuthenticationLookup(context);
        var loginIdentity = await lookup.GetByCompanyNumberAsync(companyNumber!, CancellationToken.None);

        Skip.If(
            loginIdentity is null,
            $"No persisted carrier identity exists for company number '{companyNumber}'. " +
            "Provisioning is P1-T05; skipped.");

        // Action: real company_number → carrier email → password grant (publishable key only).
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(projectUrl!.TrimEnd('/') + "/"),
        };
        var service = new SupabaseAuthenticationService(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                ProjectUrl = projectUrl,
                PublishableKey = publishableKey,
            }),
            NullLogger<SupabaseAuthenticationService>.Instance,
            lookup);

        var outcome = await service.AuthenticateAsync(
            new UserLoginRequest(companyNumber!, password!), CancellationToken.None);

        // Assertions: a real authenticated USER identity is established on the User path.
        var authenticated = Assert.IsType<AuthenticationOutcome.Authenticated>(outcome);
        Assert.Equal(AuthenticationPath.User, authenticated.Identity.AuthenticationPath);
        Assert.False(string.IsNullOrWhiteSpace(authenticated.Identity.ProviderSubject));
    }
}