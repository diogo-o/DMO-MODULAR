using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.IntegrationTests.Auth.Fakes;
using DMO.IntegrationTests.Host;
using DMO.Web.Endpoints.Access;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DMO.IntegrationTests.Auth;

/// <summary>
/// P1-T02 host plumbing tests — the auth endpoints, the real session mechanism and the
/// read-only current-account surface. Test-host fakes only; no real Supabase call.
/// </summary>
/// <remarks>
/// The session element (<see cref="DMO.Web.Auth.SessionAuthentication"/>), the endpoints and
/// <see cref="DMO.Web.Auth.CurrentAccountContext"/> are the real production classes; the
/// fake auth boundary and fake lookup are registered only inside the test host.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AuthEndpointsTests
{
    private static readonly AdminAccount TestAdmin = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "DMO Admin", "admin@dmo.test", IsActive: true);

    private static readonly UserAccount TestUser = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        CompanyNumber: "2661",
        DisplayName: "João Silva",
        Email: "joao@dmo.test",
        RoleLabel: "Reparador",
        IsActive: true,
        TemplateId: null,
        Version: 1);

    private static WebApplicationFactory<Program> FactoryWith(params AccountMatch[] matches) =>
        new DmoWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthenticationBoundary>();
                services.AddScoped<IAuthenticationBoundary, FakeTestAuthAdapter>();

                services.RemoveAll<IAccountLookup>();
                services.AddScoped<IAccountLookup>(_ => new FakeTestAccountLookup(matches));
            }));

    [Fact]
    public async Task LoginAdmin_EstablishesSession_MeReturnsAdmin_LogoutClears()
    {
        // Preconditions: host with fake boundaries returning the active ADMIN.
        using var factory = FactoryWith(new AccountMatch.Admin(TestAdmin));
        using var client = factory.CreateClient();

        // Action: ADMIN email + password login.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { email = "admin@dmo.test", password = "secret" });

        // Assertions: the session is established (login succeeds) and the current account
        // is the ADMIN — read-only, with no Template facts.
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("admin", me.GetProperty("accountType").GetString());
        Assert.Equal("admin@dmo.test", me.GetProperty("email").GetString());

        // Action: logout clears the session state.
        var logout = await client.PostAsync(AuthEndpoints.LogoutPath, null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        // Required non-effect: after logout the current account is None.
        var meAfter = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("none", meAfter.GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task LoginUser_CompanyNumber_EstablishesSession_MeReturnsUser()
    {
        // Preconditions: host with fake boundaries returning the active USER.
        using var factory = FactoryWith(new AccountMatch.User(TestUser));
        using var client = factory.CreateClient();

        // Action: USER company_number + password login.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { companyNumber = "2661", password = "secret" });

        // Assertions: the session is established and the current account is the USER with
        // the canonical company number as the identifier.
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("user", me.GetProperty("accountType").GetString());
        Assert.Equal("2661", me.GetProperty("companyNumber").GetString());
        Assert.Equal("Reparador", me.GetProperty("roleLabel").GetString());
    }

    [Fact]
    public async Task AuthEndpoints_NoAccess_ReturnsFailClosedOutcome()
    {
        // Preconditions: host with fake boundaries returning no application mapping.
        using var factory = FactoryWith();
        using var client = factory.CreateClient();

        // Action: a valid ADMIN authentication attempt with no resolvable account.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { email = "admin@dmo.test", password = "secret" });

        // Assertions: fail closed with no session established.
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("none", me.GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task Login_BothEmailAndCompanyNumber_ReturnsBadRequest()
    {
        // Preconditions: host with fake boundaries.
        using var factory = FactoryWith();
        using var client = factory.CreateClient();

        // Action: a body carrying BOTH identifiers — the transport contract forbids both,
        // and no precedence between identifiers is ever invented.
        var login = await client.PostAsJsonAsync(
            AuthEndpoints.LoginPath, new { email = "admin@dmo.test", companyNumber = "2661", password = "secret" });

        // Assertions: rejected at the request surface; no session is established.
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);

        var me = await client.GetFromJsonAsync<JsonElement>(AuthEndpoints.CurrentAccountPath);
        Assert.Equal("none", me.GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task Login_WithoutCredentials_ReturnsBadRequest()
    {
        // Preconditions: host with fake boundaries.
        using var factory = FactoryWith();
        using var client = factory.CreateClient();

        // Action: a body with neither email nor companyNumber (nor password).
        var login = await client.PostAsJsonAsync(AuthEndpoints.LoginPath, new { });

        // Assertions: rejected at the request surface; nothing reaches the boundary.
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutSession_StillSucceeds()
    {
        // Required non-effect: logout only clears session state and cannot error without a
        // session.
        using var factory = FactoryWith();
        using var client = factory.CreateClient();

        var logout = await client.PostAsync(AuthEndpoints.LogoutPath, null);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
    }
}