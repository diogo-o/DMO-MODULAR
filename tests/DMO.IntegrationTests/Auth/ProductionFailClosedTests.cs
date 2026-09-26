using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Infrastructure;
using DMO.Infrastructure.Database;
using DMO.IntegrationTests.Auth.Fakes;
using DMO.IntegrationTests.Host;
using DMO.Web.Auth;
using DMO.Web.Endpoints.Access;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DMO.IntegrationTests.Auth;

/// <summary>
/// P1-T03 production-posture tests — what happens with production composition: the ADMIN
/// and USER authentication transports are stubbed at the HTTP boundary (never the real
/// project), the real <see cref="SupabaseAuthenticationService"/> logic runs, and account
/// resolution is the real <see cref="AccountResolver"/> over the production-registered
/// <see cref="DMO.Infrastructure.Persistence.PersistenceAccountLookup"/> shape. The
/// persistence data source is the documented test-host seam (empty mapping): the real DB
/// emptiness behaviour is covered by the env-gated persistence integration tests.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProductionFailClosedTests
{
    [Fact]
    public async Task ProductionLogin_Admin_FailsClosedAtAccountStep()
    {
        // Preconditions: production composition, with ONLY the Supabase transport stubbed
        // (the real SupabaseAuthenticationService logic runs over a fake transport), and the
        // persistence data source empty (no ADMIN mapping exists).
        using var factory = new DmoWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ReplaceTransport(services);
                services.RemoveAll<IAccountLookup>();
                services.AddScoped<IAccountLookup>(_ => new FakeTestAccountLookup());
            }));
        using var client = factory.CreateClient();

        // Action: a credentials-correct ADMIN authentication attempt.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { email = "admin@dmo.test", password = "secret" });

        // Assertions: authentication succeeds, but account resolution fails closed with no
        // persisted ADMIN mapping — no session is established.
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("none", me.GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task ProductionLogin_User_FailsClosedAtAccountStep()
    {
        // Preconditions: production composition with the real extended USER flow (stubbed
        // transport; the carrier lookup supplies a persisted carrier email) and an empty
        // account mapping.
        using var factory = new DmoWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ReplaceTransport(services);
                services.RemoveAll<IUserAuthenticationLookup>();
                services.AddScoped<IUserAuthenticationLookup>(
                    _ => new FakeIntegrationUserAuthenticationLookup(
                        new UserLoginIdentity("carrier@dmo.test")));
                services.RemoveAll<IAccountLookup>();
                services.AddScoped<IAccountLookup>(_ => new FakeTestAccountLookup());
            }));
        using var client = factory.CreateClient();

        // Action: a company_number + password USER attempt.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { companyNumber = "2661", password = "secret" });

        // Assertions: the USER flow is real since P1-T03 (no longer 503 by declaration), but
        // with no persisted USER mapping the resolution fails closed — no session.
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("none", me.GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task ProductionLogin_User_UnknownCompanyNumber_ReturnsUnauthorized()
    {
        // Preconditions: production composition with real extended service; the company
        // number has no persisted carrier mapping (lookup returns null) — no provider call.
        using var factory = new DmoWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                ReplaceTransport(services);
                services.RemoveAll<IUserAuthenticationLookup>();
                services.AddScoped<IUserAuthenticationLookup>(
                    _ => new FakeIntegrationUserAuthenticationLookup(null));
            }));
        using var client = factory.CreateClient();

        // Action: USER login with an unknown company number.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { companyNumber = "999999", password = "secret" });

        // Assertions: rejected as invalid credentials at the authentication step (401); no
        // company_number is ever sent to the provider as an email.
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("none", me.GetProperty("accountType").GetString());
    }

    [Fact]
    public void ProductionStartup_WithoutSupabaseConfig_FailsLoud()
    {
        // Preconditions: database configuration valid, Supabase configuration absent. This
        // mirrors the StartupConfigurationTests precedent — the composition boundary is
        // tested directly because the entry point's original exception type is not the
        // acceptance contract.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseOptions.ConnectionStringKey] = DmoWebApplicationFactory.PlaceholderConnectionString,
            })
            .Build();

        // Database configuration is valid, so the only missing piece is Supabase.
        services.AddDmoInfrastructure(configuration);

        var supabaseOptions = new SupabaseOptions();
        configuration.GetSection(SupabaseOptions.SectionName).Bind(supabaseOptions);

        // Action + assertion: absent Supabase configuration fails loudly with the key named.
        var exception = Assert.Throws<SupabaseConfigurationException>(() => supabaseOptions.Validate());
        Assert.Contains(SupabaseOptions.ProjectUrlKey, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Replaces the Supabase transport with a stub answering the GoTrue token + user
    /// endpoints. Never touches the real project. The real service logic still runs.
    /// </summary>
    private static void ReplaceTransport(IServiceCollection services)
    {
        services.RemoveAll<SupabaseAuthenticationService>();
        services.RemoveAll<IAuthenticationBoundary>();
        services.AddScoped<SupabaseAuthenticationService>(provider =>
            new SupabaseAuthenticationService(
                new HttpClient(new StubTransportHandler())
                {
                    BaseAddress = new Uri("https://dmo-test-placeholder.invalid/"),
                },
                provider.GetRequiredService<IOptions<SupabaseOptions>>(),
                provider.GetRequiredService<ILogger<SupabaseAuthenticationService>>(),
                provider.GetRequiredService<IUserAuthenticationLookup>()));
        services.AddScoped<IAuthenticationBoundary>(provider =>
            provider.GetRequiredService<SupabaseAuthenticationService>());
    }

    /// <summary>
    /// Test-only transport stub answering the GoTrue token + user endpoints with fixed
    /// success bodies. Never touches the real project.
    /// </summary>
    private sealed class StubTransportHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, new { access_token = "stubbed-token" }));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, new { id = "stubbed-subject" }));
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, object body) =>
            new(statusCode) { Content = JsonContent.Create(body) };
    }
}