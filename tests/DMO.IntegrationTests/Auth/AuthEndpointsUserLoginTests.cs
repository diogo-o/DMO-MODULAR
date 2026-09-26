using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.IntegrationTests.Auth.Fakes;
using DMO.IntegrationTests.Host;
using DMO.Web.Auth;
using DMO.Web.Endpoints.Access;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DMO.IntegrationTests.Auth;

/// <summary>
/// P1-T03 host tests — the USER login surface (<c>company_number + password</c>) through the
/// real endpoints, the real session mechanism and the real extended
/// <see cref="SupabaseAuthenticationService"/> (with stubbed transport and fake lookup).
/// </summary>
/// <remarks>
/// The session element, the endpoints and the current-account context are the real production
/// classes; the authentication transport and the persistence data source are the documented
/// test-host seams.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AuthEndpointsUserLoginTests
{
    private static readonly UserAccount ActiveUser = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        CompanyNumber: "2661",
        DisplayName: "João Silva",
        Email: "joao@dmo.test",
        RoleLabel: "Reparador",
        IsActive: true,
        TemplateId: null,
        Version: 1);

    private static readonly UserAccount InactiveUser = ActiveUser with { IsActive = false };

    private static WebApplicationFactory<Program> UserHostWith(
        IUserAuthenticationLookup? lookup,
        params AccountMatch[] matches) =>
        new DmoWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // Replace the real service with the real logic over a stub transport.
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
                        lookup ?? new FakeIntegrationUserAuthenticationLookup()));
                services.AddScoped<IAuthenticationBoundary>(provider =>
                    provider.GetRequiredService<SupabaseAuthenticationService>());

                services.RemoveAll<IAccountLookup>();
                services.AddScoped<IAccountLookup>(_ => new FakeTestAccountLookup(matches));
            }));

    [Fact]
    public async Task UserLogin_CompanyNumberAndPassword_EstablishesSession_MeReturnsUser()
    {
        // Preconditions: the real USER flow (stubbed transport + carrier lookup) and a
        // persisted mapping to the active USER.
        using var factory = UserHostWith(lookup: null, new AccountMatch.User(ActiveUser));
        using var client = factory.CreateClient();

        // Action: USER company_number + password login.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { companyNumber = "2661", password = "secret" });

        // Assertions: session established and the current account is the USER.
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("user", me.GetProperty("accountType").GetString());
        Assert.Equal("2661", me.GetProperty("companyNumber").GetString());
        Assert.Equal("Reparador", me.GetProperty("roleLabel").GetString());
    }

    [Fact]
    public async Task UserLogin_WithInactiveUser_FailsClosed_NoSession()
    {
        using var factory = UserHostWith(lookup: null, new AccountMatch.User(InactiveUser));
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { companyNumber = "2661", password = "secret" });

        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("none", me.GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task UserLogin_UnknownCompanyNumber_ReturnsUnauthorized_WithoutProviderRoundTrip()
    {
        // The lookup carries no persisted carrier for the unknown company number.
        using var factory = UserHostWith(
            lookup: new FakeIntegrationUserAuthenticationLookup(null),
            new AccountMatch.User(ActiveUser));
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { companyNumber = "999999", password = "secret" });

        // The boundary returns InvalidCredentials (unknown company number -> no provider call).
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task LoginWithEmailOnly_RoutesTheAdminContract_NeverTheUserContract()
    {
        // The transport contract routes email -> ADMIN and companyNumber -> USER; email is
        // never accepted as a USER identifier. The account lookup is the empty seam so the
        // outcome is resolution-failure (403) — the boundary routing is what matters here.
        var recorded = new RecordingBoundary();
        using var factory = new DmoWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthenticationBoundary>();
                services.AddScoped<IAuthenticationBoundary>(_ => recorded);
                services.RemoveAll<IAccountLookup>();
                services.AddScoped<IAccountLookup>(_ => new FakeTestAccountLookup());
            }));
        using var client = factory.CreateClient();

        var emailLogin = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { email = "admin@dmo.test", password = "secret" });

        // Assertions: the email login produced exactly an AdminLoginRequest at the boundary.
        Assert.Equal(HttpStatusCode.Forbidden, emailLogin.StatusCode);
        Assert.IsType<AdminLoginRequest>(Assert.Single(recorded.Requests));

        var companyLogin = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { companyNumber = "2661", password = "secret" });

        Assert.Equal(HttpStatusCode.Forbidden, companyLogin.StatusCode);
        Assert.IsType<UserLoginRequest>(recorded.Requests[1]);
    }

    [Fact]
    public async Task Login_BothIdentifiers_ReturnsBadRequest()
    {
        using var factory = UserHostWith(lookup: null);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath,
            new { email = "admin@dmo.test", companyNumber = "2661", password = "secret" });

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task Login_NeitherIdentifier_ReturnsBadRequest()
    {
        using var factory = UserHostWith(lookup: null);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(AuthEndpoints.LoginPath, new { });

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    /// <summary>Test-only boundary that records the requests it receives (never authenticates).</summary>
    private sealed class RecordingBoundary : IAuthenticationBoundary
    {
        public List<AuthenticationRequest> Requests { get; } = [];

        public Task<AuthenticationOutcome> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request is AdminLoginRequest
                ? AuthenticationPath.Admin
                : AuthenticationPath.User;
            return Task.FromResult<AuthenticationOutcome>(
                new AuthenticationOutcome.Authenticated(
                    new AuthenticatedIdentity("sys-test-subject", path)));
        }
    }

    /// <summary>Test-only transport stub answering the GoTrue token + user endpoints.</summary>
    private sealed class StubTransportHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, new { access_token = "stubbed-user-token" }));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, new { id = "stubbed-user-subject" }));
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, object body) =>
            new(statusCode) { Content = JsonContent.Create(body) };
    }
}