using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DMO.Application.Access;
using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.IntegrationTests.Auth.Fakes;
using DMO.IntegrationTests.Host;
using DMO.Web.Auth;
using DMO.Web.Endpoints.Access;
using DMO.Web.Frontend.Shell;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DMO.IntegrationTests.Navigation;

/// <summary>
/// P1-T07 public login page. Test:
/// Purpose: prove the /Login surface renders publicly, delegates the complete login
/// orchestration to <see cref="SessionLoginService"/> (real service, real session
/// mechanism, stubbed provider transport + fake lookup, exactly like the accepted auth host
/// tests), and dispatches ADMIN/USER exactly per the accepted post-login contract.
/// Master behavior being verified: request P1-T07 §17 (public login page + session
/// orchestration) and §13 dispatch (ADMIN → /Administration; USER → landing service).
/// Preconditions: test host with real login/session pipeline and controlled access seams.
/// Action: GET /Login; POST /Login with form fields + antiforgery token.
/// Assertions: exact status/location; generic error and no session cookie on every failure;
/// dmo.session cookie + landing dispatch on success.
/// Required non-effects: no session on failure, no reason mapping at the UI, no ReturnUrl,
/// no new auth model, no shell handling of unauthenticated users.
/// What this proves: the accepted login page contract and behavior-preserving extraction.
/// What this does NOT prove: real provider behavior (skipped live tests unchanged) or the
/// session claims themselves (P1-T02/T03 tests).
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class LoginPageTests
{
    private const string GenericError = "A autenticação falhou. Verifique os dados introduzidos e tente novamente.";

    [Fact]
    public async Task LoginPage_RendersPublicly_WithoutOperationalShell()
    {
        using var factory = LoginHost();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Login");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Iniciar sessão", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Email\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"CompanyNumber\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Password\"", html, StringComparison.Ordinal);
        // The operational A2 shell is not involved on the public surface.
        Assert.DoesNotContain("dmo-app-header", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dmo-primary-nav", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DmoShell", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPage_AdminCredentials_EstablishSessionAndRedirectToAdministration()
    {
        using var factory = LoginHost(new AccountMatch.Admin(AdminAccount));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, email: AdminAccount.Email, password: "secret");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Administration", response.Headers.Location?.ToString());
        Assert.Contains(
            response.Headers,
            header => header.Key == "Set-Cookie" && header.Value.Any(value => value.Contains("dmo.session", StringComparison.Ordinal)));

        // The real session was established: /auth/me resolves the admin through it.
        var me = await client.GetFromJsonAsync<AuthEndpoints.MeResponse>("/auth/me");
        Assert.NotNull(me);
        Assert.Equal("admin", me.AccountType);
    }

    [Fact]
    public async Task LoginPage_UserCredentials_EstablishSessionAndDispatchThroughSameLandingService()
    {
        var jobOn = TestNavigationComposition.Definition(ModuleCatalog.JobOnView, "Job On View", "job-on", "Job On");
        using var factory = LoginHostForUserDispatch(
            [jobOn],
            TestNavigationComposition.GrantedWithLanding("job-on", jobOn),
            new Dictionary<string, string> { ["job-on"] = "/implemented/job-on" },
            new AccountMatch.User(ActiveUser));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, companyNumber: "2661", password: "secret");

        // The USER is dispatched through UserLandingService → the explicit landing route.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/implemented/job-on", response.Headers.Location?.ToString());
        Assert.Contains(
            response.Headers,
            header => header.Key == "Set-Cookie" && header.Value.Any(value => value.Contains("dmo.session", StringComparison.Ordinal)));

        var me = await client.GetFromJsonAsync<AuthEndpoints.MeResponse>("/auth/me");
        Assert.NotNull(me);
        Assert.Equal("user", me.AccountType);
    }

    [Fact]
    public async Task LoginPage_User_NullLanding_RedirectsToFirstDestinationInOrder()
    {
        var controlo = TestNavigationComposition.Definition(ModuleCatalog.ControloCreate, "Controlo Create", "controlo", "Controlo");
        var jobOn = TestNavigationComposition.Definition(ModuleCatalog.JobOnView, "Job On View", "job-on", "Job On");
        using var factory = LoginHostForUserDispatch(
            [controlo, jobOn],
            TestNavigationComposition.GrantedWithLanding(null, controlo, jobOn),
            new Dictionary<string, string>
            {
                ["controlo"] = "/implemented/controlo",
                ["job-on"] = "/implemented/job-on",
            },
            new AccountMatch.User(ActiveUser));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, companyNumber: "2661", password: "secret");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/implemented/controlo", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task LoginPage_User_NoRoutableDestination_RedirectsToAccessDenied()
    {
        using var factory = LoginHostForUserDispatch(
            [],
            TestNavigationComposition.Granted(),
            new Dictionary<string, string>(),
            new AccountMatch.User(ActiveUser));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await PostLoginAsync(client, companyNumber: "2661", password: "secret");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessDenied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task LoginPage_InvalidCredentials_NoSessionAndGenericErrorOnly()
    {
        using var factory = LoginHost(transport: new RejectingTransportHandler());
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, email: AdminAccount.Email, password: "wrong");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(GenericError, html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidCredentials", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            response.Headers,
            header => header.Key == "Set-Cookie" && header.Value.Any(value => value.Contains("dmo.session", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LoginPage_UnknownApplicationAccount_NoSessionAndGenericErrorOnly()
    {
        // Authentication succeeds at the provider (stub) but no application account matches:
        // fail closed, generic page error, no session.
        using var factory = LoginHost();
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, companyNumber: "9999", password: "secret");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(GenericError, html, StringComparison.Ordinal);
        Assert.DoesNotContain("dmo.session", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPage_MissingPassword_GenericErrorAndNoSession()
    {
        using var factory = LoginHost();
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, email: AdminAccount.Email, password: null);
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(GenericError, html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            response.Headers,
            header => header.Key == "Set-Cookie" && header.Value.Any(value => value.Contains("dmo.session", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LoginPage_BothIdentifiers_GenericErrorAndNoSession()
    {
        using var factory = LoginHost();
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, email: AdminAccount.Email, companyNumber: "2661", password: "secret");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(GenericError, System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.DoesNotContain(
            response.Headers,
            header => header.Key == "Set-Cookie" && header.Value.Any(value => value.Contains("dmo.session", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LoginPage_NeitherIdentifier_GenericErrorAndNoSession()
    {
        using var factory = LoginHost();
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, companyNumber: null, email: null, password: "secret");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(GenericError, System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.DoesNotContain(
            response.Headers,
            header => header.Key == "Set-Cookie" && header.Value.Any(value => value.Contains("dmo.session", StringComparison.Ordinal)));
    }

    // ---- composition -------------------------------------------------------------

    private static readonly AdminAccount AdminAccount = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "DMO Admin", "admin@dmo.test", IsActive: true);

    private static readonly UserAccount ActiveUser = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        CompanyNumber: "2661",
        DisplayName: "João Silva",
        Email: "joao@dmo.test",
        RoleLabel: "Reparador",
        IsActive: true,
        TemplateId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Version: 1);

    private static WebApplicationFactory<Program> LoginHost(params AccountMatch[] matches) =>
        LoginHostCore(matches, lookup: null, available: [], outcome: TestNavigationComposition.Granted(), routes: new Dictionary<string, string>(), transport: null);

    private static WebApplicationFactory<Program> LoginHostForUserDispatch(
        IReadOnlyList<ModuleDefinition> available,
        AccessOutcome outcome,
        IReadOnlyDictionary<string, string> routes,
        params AccountMatch[] matches) =>
        LoginHostCore(
            matches,
            lookup: null,
            available,
            outcome,
            routes,
            transport: null);

    private static WebApplicationFactory<Program> LoginHost(HttpMessageHandler transport, params AccountMatch[] matches) =>
        LoginHostCore(matches, lookup: null, available: [], outcome: TestNavigationComposition.Granted(), routes: new Dictionary<string, string>(), transport);

    private static WebApplicationFactory<Program> LoginHostCore(
        IReadOnlyList<AccountMatch> matches,
        IUserAuthenticationLookup? lookup,
        IReadOnlyList<ModuleDefinition> available,
        AccessOutcome outcome,
        IReadOnlyDictionary<string, string> routes,
        HttpMessageHandler? transport)
    {
        return new DmoWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // Real authentication service over a stub transport + fake carrier lookup
                // (same seam as the accepted auth host tests).
                services.RemoveAll<SupabaseAuthenticationService>();
                services.RemoveAll<IAuthenticationBoundary>();
                services.AddScoped<SupabaseAuthenticationService>(provider =>
                    new SupabaseAuthenticationService(
                        new HttpClient(transport ?? new AllowAllTransportHandler())
                        {
                            BaseAddress = new Uri("https://dmo-test-placeholder.invalid/"),
                        },
                        provider.GetRequiredService<IOptions<SupabaseOptions>>(),
                        provider.GetRequiredService<ILogger<SupabaseAuthenticationService>>(),
                        lookup ?? new FakeIntegrationUserAuthenticationLookup()));
                services.AddScoped<IAuthenticationBoundary>(provider =>
                    provider.GetRequiredService<SupabaseAuthenticationService>());

                // Real account resolver over a fake persisted mapping (data source only).
                services.RemoveAll<IAccountLookup>();
                services.AddScoped<IAccountLookup>(_ => new FakeTestAccountLookup(matches.ToArray()));

                // Controlled navigation seams for the USER landing dispatch.
                services.RemoveAll<IModuleAccessService>();
                services.RemoveAll<IModuleRegistry>();
                services.RemoveAll<IDestinationRouteRegistry>();
                services.AddSingleton<IModuleRegistry>(ModuleRegistry.Create(available));
                services.AddSingleton<IModuleAccessService>(new TestNavigationComposition.FixedAccessService(outcome));
                services.AddSingleton<IDestinationRouteRegistry>(new TestNavigationComposition.DictionaryRouteRegistry(routes));
            }));
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string? email = null,
        string? companyNumber = null,
        string? password = null)
    {
        // Razor Pages antiforgery: fetch the token from the rendered form first.
        var page = await client.GetAsync("/Login");
        var pageHtml = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(
            pageHtml,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
            RegexOptions.Singleline).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "The login form must render its antiforgery token.");

        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        };
        if (email is not null)
        {
            fields["Email"] = email;
        }

        if (companyNumber is not null)
        {
            fields["CompanyNumber"] = companyNumber;
        }

        if (password is not null)
        {
            fields["Password"] = password;
        }

        var result = await client.PostAsync("/Login", new FormUrlEncodedContent(fields));

        return result;
    }

    /// <summary>Test-only transport stub answering the GoTrue token + user endpoints with success.</summary>
    private sealed class AllowAllTransportHandler : HttpMessageHandler
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

    /// <summary>Test-only transport stub rejecting every credential exchange (400).</summary>
    private sealed class RejectingTransportHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error = "invalid_grant" }),
            });
    }
}