using System.Text.Encodings.Web;
using DMO.Application.Access;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Repositories;
using DMO.Application.Session;
using DMO.IntegrationTests.Controlo.Pesos;
using DMO.IntegrationTests.Host;
using DMO.IntegrationTests.Navigation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DMO.IntegrationTests.Controlo.Approve;

/// <summary>
/// P2-T06 test host composition: the real web application with the access, account and
/// persistence seams replaced by controlled test doubles.
/// </summary>
/// <remarks>
/// <para>
/// The Module registry and the effective-access outcome are test-owned, exactly like the accepted
/// P2-T04/P2-T05 compositions: production availability is never edited, and
/// <c>ModuleRegistrations.CurrentBuildAvailable</c> stays empty in the real composition. The Peso
/// repositories and the anchor-traversal read run against the accepted
/// <see cref="P2T05TestComposition"/>; the review repository runs against the in-memory
/// <see cref="P2T06ReviewStore"/>; the P2-T04 contracts (Job On, Tool) run against the REAL
/// services over the accepted <see cref="P2T04TestStore"/>, so the composition rows prove the real
/// orchestration.</para>
/// <para>
/// The test authentication scheme marks a request as authenticated so a policy denial surfaces as
/// HTTP 403 instead of a 401 challenge. It grants nothing: the effective access outcome decides.</para>
/// </remarks>
internal static class P2T06TestHost
{
    /// <summary>The test-only authentication scheme name.</summary>
    public const string SchemeName = "dmo.p2t06.test.auth";

    /// <summary>The header that marks a request as authenticated.</summary>
    public const string HeaderName = "X-Dmo-P2T06-Test-Auth";

    /// <summary>The fixed backend actor of every granted USER request (assertible, AC-D4).</summary>
    public static readonly Guid ActorUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>The Controlo Approve Module made available by the controlled test registry.</summary>
    public static IReadOnlyList<ModuleDefinition> AvailableModules { get; } =
    [
        TestNavigationComposition.Definition(
            ModuleCatalog.ControloApprove, "Controlo Approve", "controlo", "Controlo"),
    ];

    /// <summary>
    /// Creates the test host: the supplied caller, the granted Modules, and fresh in-memory stores
    /// behind the real P2-T06 + P2-T05 + P2-T04 services.
    /// </summary>
    public static WebApplicationFactory<Program> Create(
        CurrentAccount current,
        IReadOnlyList<ModuleDefinition> granted,
        P2T06TestComposition? composition = null,
        Action<IServiceCollection>? additional = null)
    {
        var baseFactory = new DmoWebApplicationFactory();
        var store = composition ?? new P2T06TestComposition();

        return TestNavigationComposition.ConfigureFactory(
            baseFactory,
            current,
            AvailableModules,
            new AccessOutcome.Granted(null, granted),
            new Dictionary<string, string>(),
            services =>
            {
                services.RemoveAll<IPesoRepository>();
                services.RemoveAll<IRepairerRepository>();
                services.RemoveAll<IMachineRepairerAssignmentRepository>();
                services.RemoveAll<IPdfDirectorySettingsRepository>();
                services.RemoveAll<IEmailListRepository>();
                services.RemoveAll<IEmailTemplateRepository>();
                services.RemoveAll<IGlassDensitySettingsRepository>();
                services.RemoveAll<IPesoContextRead>();
                services.RemoveAll<IControloCalculationConfiguration>();
                services.RemoveAll<IPesoReviewRepository>();

                services.AddSingleton<IPesoRepository>(store.Pesos);
                services.AddSingleton<IRepairerRepository>(store.Pesos);
                services.AddSingleton<IMachineRepairerAssignmentRepository>(store.Pesos);
                services.AddSingleton<IPdfDirectorySettingsRepository>(store.Pesos);
                services.AddSingleton<IEmailListRepository>(store.Pesos);
                services.AddSingleton<IEmailTemplateRepository>(store.Pesos);
                services.AddSingleton<IGlassDensitySettingsRepository>(store.Pesos);
                services.AddSingleton<IPesoContextRead>(store.Pesos);
                services.AddSingleton<IControloCalculationConfiguration>(store.Pesos.Calculation);
                services.AddSingleton<IPesoReviewRepository>(store.Review);

                // The real Job On/Tool services run over the accepted P2-T04 store.
                services.RemoveAll<IToolRepository>();
                services.RemoveAll<IJobOnRepository>();
                services.AddSingleton<IToolRepository>(store.Pesos.JobOnToolStore);
                services.AddSingleton<IJobOnRepository>(store.Pesos.JobOnToolStore);

                services.Configure<AuthenticationOptions>(options =>
                {
                    if (!options.Schemes.Any(scheme => scheme.Name == SchemeName))
                    {
                        options.AddScheme<TestAuthenticationHandler>(SchemeName, displayName: null);
                    }

                    options.DefaultScheme = SchemeName;
                    options.DefaultChallengeScheme = SchemeName;
                    options.DefaultForbidScheme = SchemeName;
                });

                // A failing request must report the REAL server-side cause, never a bare 500.
                services.AddSingleton<IStartupFilter>(new ExceptionCaptureStartupFilter());

                additional?.Invoke(services);
            });
    }

    /// <summary>Creates the host for a USER holding exactly the supplied Modules (fixed backend actor).</summary>
    public static WebApplicationFactory<Program> ForUser(
        IReadOnlyList<ModuleDefinition> granted,
        P2T06TestComposition? composition = null,
        Action<IServiceCollection>? additional = null) =>
        Create(
            TestNavigationComposition.User(accountId: ActorUserId),
            granted,
            composition,
            additional);

    /// <summary>Creates the host for an ADMIN session (which must gain nothing operational).</summary>
    public static WebApplicationFactory<Program> ForAdmin(
        IReadOnlyList<ModuleDefinition> granted,
        P2T06TestComposition? composition = null) =>
        Create(TestNavigationComposition.Admin(), granted, composition);

    /// <summary>Creates the host for a caller with no session at all.</summary>
    public static WebApplicationFactory<Program> ForAnonymous(
        IReadOnlyList<ModuleDefinition> granted,
        P2T06TestComposition? composition = null) =>
        Create(new CurrentAccount.None(), granted, composition);

    /// <summary>The granted Controlo Approve Module.</summary>
    public static IReadOnlyList<ModuleDefinition> AllGranted() => AvailableModules;

    /// <summary>The granted Controlo Create Module (for the sibling-denial and reopen-regression rows).</summary>
    public static IReadOnlyList<ModuleDefinition> CreateOnly() =>
    [
        TestNavigationComposition.Definition(
            ModuleCatalog.ControloCreate, "Controlo Create", "controlo", "Controlo"),
    ];

    /// <summary>Both Controlo grants (the shared <c>controlo</c> destination with separate gates).</summary>
    public static IReadOnlyList<ModuleDefinition> BothGranted() =>
    [
        TestNavigationComposition.Definition(
            ModuleCatalog.ControloCreate, "Controlo Create", "controlo", "Controlo"),
        TestNavigationComposition.Definition(
            ModuleCatalog.ControloApprove, "Controlo Approve", "controlo", "Controlo"),
    ];

    /// <summary>Sends a request marked as authenticated by the test scheme.</summary>
    public static Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpClient client,
        HttpRequestMessage request)
    {
        request.Headers.Add(HeaderName, "test-caller");

        return client.SendAsync(request);
    }

    /// <summary>Sends an authenticated GET.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        SendAuthenticatedAsync(client, new HttpRequestMessage(HttpMethod.Get, path));

    /// <summary>Sends an authenticated request with a JSON body.</summary>
    public static Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string json)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

        return SendAuthenticatedAsync(client, request);
    }

    /// <summary>Builds the JSON body of a request from a serialized anonymous object.</summary>
    public static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    /// <summary>
    /// Test-only middleware that captures an unhandled request exception into the response body, so
    /// a failing test reports the REAL server-side cause instead of a bare 500.
    /// </summary>
    private sealed class ExceptionCaptureStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextPipeline) =>
                {
                    try
                    {
                        await nextPipeline();
                    }
                    catch (Exception exception)
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.WriteAsync("TEST-CAPTURED: " + exception);
                    }
                });

                next(app);
            };
    }

    /// <summary>
    /// Test authentication scheme: marks requests carrying the test header as authenticated, so a
    /// policy failure surfaces as HTTP 403. It grants no role and carries no access claims.
    /// </summary>
    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers[HeaderName];
            if (string.IsNullOrWhiteSpace(header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, header.ToString())],
                    SchemeName));

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, SchemeName)));
        }
    }
}