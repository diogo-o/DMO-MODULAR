using System.Text.Encodings.Web;
using DMO.Application.Access;
using DMO.Application.ControloCreate;
using DMO.Application.Documents;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Application.Session;
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

namespace DMO.IntegrationTests.JobOn;

/// <summary>
/// P2-T04 test host composition: the real web application with the access, account and persistence
/// seams replaced by controlled test doubles.
/// </summary>
/// <remarks>
/// <para>
/// The Module registry and the effective-access outcome are test-owned, exactly like the accepted
/// P1-T07/A2/P2-T03 compositions: production availability is never edited, and
/// <c>ModuleRegistrations.CurrentBuildAvailable</c> stays empty in the real composition.
/// </para>
/// <para>
/// The test authentication scheme marks a request as authenticated so a policy denial surfaces as
/// HTTP 403 instead of a 401 challenge. It grants nothing: the effective access outcome decides.
/// </para>
/// </remarks>
internal static class P2T04TestHost
{
    /// <summary>The test-only authentication scheme name.</summary>
    public const string SchemeName = "dmo.p2t04.test.auth";

    /// <summary>The header that marks a request as authenticated.</summary>
    public const string HeaderName = "X-Dmo-P2T04-Test-Auth";

    /// <summary>The three P2-T04 Modules made available by the controlled test registry.</summary>
    public static IReadOnlyList<ModuleDefinition> AvailableModules { get; } =
    [
        Definition(ModuleCatalog.JobOnView, "Job On View", "job-on", "Job On"),
        Definition(ModuleCatalog.JobOnCreate, "Job On Create", "job-on", "Job On"),
        Definition(ModuleCatalog.Ferramentas, "Ferramentas", null, "Ferramentas", contextual: true),
    ];

    /// <summary>Builds a Module definition for the controlled test registry.</summary>
    public static ModuleDefinition Definition(
        ModuleId id,
        string displayName,
        string? destinationId,
        string surfaceName,
        bool contextual = false) =>
        TestNavigationComposition.Definition(id, displayName, destinationId, surfaceName, contextual);

    /// <summary>
    /// Creates the test host: the supplied caller, the granted Modules, and a fresh in-memory store
    /// behind the real P2-T04 services.
    /// </summary>
    public static WebApplicationFactory<Program> Create(
        CurrentAccount current,
        IReadOnlyList<ModuleDefinition> granted,
        P2T04TestStore? store = null,
        Action<IServiceCollection>? additional = null)
    {
        var baseFactory = new DmoWebApplicationFactory();
        var applicationStore = store ?? new P2T04TestStore();

        return TestNavigationComposition.ConfigureFactory(
            baseFactory,
            current,
            AvailableModules,
            new AccessOutcome.Granted(null, granted),
            new Dictionary<string, string>(),
            services =>
            {
                services.RemoveAll<IToolRepository>();
                services.RemoveAll<IJobOnRepository>();
                services.AddSingleton<IToolRepository>(applicationStore);
                services.AddSingleton<IJobOnRepository>(applicationStore);

                // The outputs-slice seams (IPesoOutputRead + IPesoPdfDocumentRead) are store-backed
                // in this composition: the real JobOnControlOutputsService and JobOnEndpoints run
                // over controlled arrangements (seeded Pesos and seeded PDF contents), exactly as
                // the repositories — no database is touched by the Job On sheet or its document
                // route here; the DB-backed reads are proven by the persistence-class tests.
                services.RemoveAll<IPesoOutputRead>();
                services.RemoveAll<IPesoPdfDocumentRead>();
                services.AddSingleton<IPesoOutputRead>(applicationStore);
                services.AddSingleton<IPesoPdfDocumentRead>(applicationStore);

                // The real lineage probe reads the database; in this store-backed composition the
                // lineage lives in the store, so the probe reports exactly the same fact from the
                // same data the repository writes. The DB-class suite proves the real probe against
                // the real schema, and the seam is unchanged: one registration per contributing
                // module. Additional probes registered through `additional` are appended, so the
                // delete flow evaluates every registered probe.
                services.RemoveAll<IJobOnDependencyProbe>();
                services.AddSingleton<IJobOnDependencyProbe>(new StoreLineageDependencyProbe(applicationStore));

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

    /// <summary>Creates the host for a USER holding exactly the supplied Modules.</summary>
    public static WebApplicationFactory<Program> ForUser(
        IReadOnlyList<ModuleDefinition> granted,
        P2T04TestStore? store = null,
        Action<IServiceCollection>? additional = null) =>
        Create(TestNavigationComposition.User(), granted, store, additional);

    /// <summary>Creates the host for an ADMIN session (which must gain nothing operational).</summary>
    public static WebApplicationFactory<Program> ForAdmin(
        IReadOnlyList<ModuleDefinition> granted,
        P2T04TestStore? store = null) =>
        Create(TestNavigationComposition.Admin(), granted, store);

    /// <summary>Creates the host for a caller with no session at all.</summary>
    public static WebApplicationFactory<Program> ForAnonymous(
        IReadOnlyList<ModuleDefinition> granted,
        P2T04TestStore? store = null) =>
        Create(new CurrentAccount.None(), granted, store);

    /// <summary>The three granted P2-T04 Modules (Job On View + Job On Create + Ferramentas).</summary>
    public static IReadOnlyList<ModuleDefinition> AllGranted() => AvailableModules;

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
    /// Store-backed duplication-lineage probe: the same fact and the same kind the real
    /// <c>JobOnLineageDependencyProbe</c> reports, read from the store the repository writes to.
    /// </summary>
    internal sealed class StoreLineageDependencyProbe : IJobOnDependencyProbe
    {
        private readonly P2T04TestStore _store;

        public StoreLineageDependencyProbe(P2T04TestStore store) => _store = store;

        public Task<JobOnDependencyReport> InspectAsync(
            JobOnDependencyTarget target,
            CancellationToken cancellationToken)
        {
            var dependencies = _store.LineageDependencies(target.JobOnId);

            return Task.FromResult(dependencies.Count == 0
                ? JobOnDependencyReport.None("job-on")
                : new JobOnDependencyReport("job-on", dependencies));
        }
    }

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