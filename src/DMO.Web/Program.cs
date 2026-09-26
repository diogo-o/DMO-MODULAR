using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Application.Boquilhas;
using DMO.Application.ControloApprove;
using DMO.Application.ControloComparacao;
using DMO.Application.ControloCreate;
using DMO.Application.Documents;
using DMO.Application.JobOn;
using DMO.Application.Migrations;
using DMO.Application.Session;
using DMO.Application.TemplateAdministration;
using DMO.Application.Tools;
using DMO.Application.UserAdministration;
using DMO.Infrastructure;
using DMO.Infrastructure.Configuration;
using DMO.Infrastructure.Database;
using DMO.Web.Auth;
using DMO.Web.Authorization;
using DMO.Web.Endpoints;
using DMO.Web.Frontend.Shared;
using DMO.Web.Navigation;
using DMO.Web.Startup;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

// Local development uses the repository-root .env when present. Load it before the default
// .NET configuration pipeline is built so the application and integration tests consume the
// same process-level keys. Explicit process variables retain precedence.
LocalEnvironmentFile.LoadFromRepositoryRoot();

var builder = WebApplication.CreateBuilder(args);

// Composition root. The host wires the runtime: configuration binding, shared
// infrastructure registration and the HTTP pipeline. It owns no industrial business rule.
try
{
    builder.Services.AddDmoInfrastructure(builder.Configuration);

    // ---- P1-T02/P1-T03 authentication + account boundary ---------------------------
    // ADMIN authentication is real Supabase Auth against the DEV/TEST project using only
    // the project URL and the publishable key. The USER flow (P1-T03) resolves the
    // persisted carrier email for company_number through the narrow IUserAuthenticationLookup
    // and then uses the same Supabase password grant. No service_role, no secret key, no
    // fake provider, no in-memory account store.

    builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.SectionName));

    // Fail fast at startup, exactly like the database configuration: the host must never
    // report itself as started when the Supabase configuration it will need is absent or
    // invalid, and it must never substitute a default.
    var supabaseOptions = new SupabaseOptions();
    builder.Configuration.GetSection(SupabaseOptions.SectionName).Bind(supabaseOptions);
    supabaseOptions.Validate();

    builder.Services.AddHttpContextAccessor();

    // Runtime session element (cookie scheme). A session is established only when
    // authentication succeeds AND account resolution returns an active ADMIN/USER.
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = "dmo.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

    builder.Services.AddHttpClient<SupabaseAuthenticationService>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<SupabaseOptions>>().Value;
        client.BaseAddress = new Uri(options.ProjectUrl!.TrimEnd('/') + "/");
    });

    builder.Services.AddScoped<IAuthenticationBoundary>(static provider =>
        provider.GetRequiredService<SupabaseAuthenticationService>());
    builder.Services.AddScoped<IAccountResolver, AccountResolver>();
    builder.Services.AddScoped<ISessionAuthentication, SessionAuthentication>();
    builder.Services.AddScoped<ICurrentAccountContext, CurrentAccountContext>();

    // ---- P1-T07 navigation + USER shell ------------------------------------------------
    // Login orchestration (shared by POST /auth/login and the /Login page) and runtime USER
    // landing resolution (consumed by the root router, the /Login dispatch and the no-access
    // flow). Both compose the accepted A2 projection; no second navigation architecture.
    builder.Services.AddScoped<SessionLoginService>();
    builder.Services.AddScoped<UserLandingService>();

    // ---- P1-T04 server-side Module gate ---------------------------------------------
    // Authorization policies are a thin ASP.NET projection of the canonical Module ids:
    // one policy per ModuleCatalog identity, each carrying exactly one
    // ModuleAuthorizationRequirement. The handler resolves effective access through the
    // Module access service (registry + persisted Template); it grants nothing from claims,
    // roles, Template names or navigation visibility.
    builder.Services.AddModuleAuthorization();

    // ---- A2 shared frontend seam ----------------------------------------------------
    // Razor Pages and shared presentation services only. Industrial routes, feature
    // services and Module registrations remain with their owning workstreams.
    builder.Services.AddDmoSharedFrontend();

    // ---- P1-T03 single-ADMIN bootstrap (deployment-only command) -------------------
    // Exactly three inputs (env/user-secrets only): AdminBootstrap__Email,
    // AdminBootstrap__DisplayName, AdminBootstrap__AuthIdentityId (the exact provider
    // subject of the existing Supabase Auth ADMIN identity). No provider call is made.
    builder.Services.Configure<AdminBootstrapOptions>(
        builder.Configuration.GetSection(AdminBootstrapOptions.SectionName));
    builder.Services.AddScoped<AdminBootstrapCommand>();

    // ---- P1-T05 ADMIN-only USER administration --------------------------------------
    // The privileged provider boundary uses the service-role secret (server-only), validated
    // eagerly like every other required configuration: the host must never start against a
    // missing service-role secret when the administration surface is registered.
    builder.Services.Configure<SupabaseAdminOptions>(
        builder.Configuration.GetSection(SupabaseAdminOptions.SectionName));
    var supabaseAdminOptions = new SupabaseAdminOptions();
    builder.Configuration.GetSection(SupabaseAdminOptions.SectionName).Bind(supabaseAdminOptions);
    supabaseAdminOptions.Validate();

    builder.Services.AddHttpClient<SupabaseAdminUserService>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<SupabaseOptions>>().Value;
        client.BaseAddress = new Uri(options.ProjectUrl!.TrimEnd('/') + "/");
    });

    builder.Services.AddScoped<IUserIdentityProvisioner>(static provider =>
        provider.GetRequiredService<SupabaseAdminUserService>());
    builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();

    // ---- P1-T06 ADMIN-only Template administration -------------------------------------
    // Orchestrates the persistence primitives + the Module Registry (availability/landing
    // validation). Pages and endpoints share the same service and the same ADMIN gate.
    builder.Services.AddScoped<ITemplateAdministrationService, TemplateAdministrationService>();

    // ---- P2-T04 domain core: canonical Tool identity + Job On production occurrence -------
    // One shared Tool search/select/create orchestration and the Job On occurrence service,
    // composed over the P2-T03 shared primitives. No policy, no availability entry, no
    // destination route and no second registry is added here.
    builder.Services.AddScoped<IToolService, ToolService>();
    builder.Services.AddScoped<IJobOnService, JobOnService>();

    // ---- P2-T05 Controlo Create: Peso core + Controlo_Create → Definições ----------------
    // The Peso create/measurement/submission service (composing the P2-T04 application
    // contracts), the Definições settings service over the five configuration areas, the
    // calculation configuration (Q-CALC: configuration-provided divisor/density mappings) and
    // the server-host PDF-directory probe (Q-PDF). No policy, no availability entry, no
    // destination route and no second registry is added here: every P2-T05 route is
    // server-gated by the accepted `controlo-create` policy and stays denied to every caller
    // until P2-T10 registers availability (contract §21.6).
    builder.Services.AddScoped<IControloCreateService, ControloCreateService>();
    builder.Services.AddScoped<IControloDefinicoesService, ControloDefinicoesService>();
    builder.Services.AddSingleton<IControloCalculationConfiguration, ConfigurationCalculationConfiguration>();
    builder.Services.AddSingleton<IPdfDirectoryProbe, ServerHostPdfDirectoryProbe>();

    // ---- P2-T06 Controlo Approve: Aprovar + Histórico de Pesos -------------------------------
    // The review/decision service over the SAME persisted peso_id (composing the shared P2-T05
    // read, the review repository and the backend actor). No policy, no availability entry, no
    // destination route and no second registry is added here: every P2-T06 route is server-gated
    // by the accepted `controlo-approve` policy and stays denied to every caller until P2-T10
    // registers availability (contract §13.3).
    builder.Services.AddScoped<IControloApproveService, ControloApproveService>();

    // ---- Peso Comparação slice: the optional per-CM re-measurement core ---------------------------
    // The Comparação service over the SAME peso_id (composing the shared Peso read, the cm-context
    // traversal read and the Comparação repository). No policy, no availability entry, no
    // destination route and no second registry is added here: the Comparação is an optional child
    // of the initial Peso and never changes its values, status or approval.
    builder.Services.AddScoped<IControloComparacaoService, ControloComparacaoService>();

    // ---- P2-T08 documents: Peso PDF generation/storage + manual email send (this slice) ---------
    // The generation service composes the SHARED P2-T05 Peso read (the same read model Create and
    // Approve render), the configured pdf_directory_settings and the server-host workspace; the
    // send service reuses the same read + the configured email templates/lists (single source of
    // truth) and attaches the EXISTING generated PDF through the file store. The renderer and the
    // file store are stateless adapters; the SMTP transport is bound from Email:Transport (plain
    // values — no fail-fast at startup: sending is optional operational work, refused typed at
    // send time when unconfigured). No policy, no availability entry and no destination route is
    // added here: the routes are gated by the owning Controlo Create policy.
    builder.Services.AddScoped<IPesoPdfService, PesoPdfService>();
    builder.Services.AddScoped<IPesoPdfSendService, PesoPdfSendService>();
    builder.Services.AddSingleton<IPesoPdfRenderer, PesoPdfRenderer>();
    builder.Services.AddSingleton<IPesoPdfFileStore, ServerHostPesoPdfFileStore>();
    builder.Services.AddSingleton<IEmailTransport>(static provider =>
    {
        var options = new EmailTransportOptions();
        provider
            .GetRequiredService<IConfiguration>()
            .GetSection(EmailTransportOptions.SectionName)
            .Bind(options);

        return new SmtpEmailTransport(options);
    });

    // ---- Controlo outputs on the Job On sheet (this slice: the Peso PDF) -----------------------
    // The read-only projection service composes the occurrence, the related Pesos (Controlo-owned
    // read seam over the real CM context rows) and the Peso PDF availability/content read
    // (Documents area: configured base directory + deterministic naming + file store). The Job On
    // surface never touches the filesystem and no absolute path ever crosses the application
    // boundary; the open route is gated by the owning job-on-view policy.
    builder.Services.AddScoped<IPesoPdfDocumentRead, PesoPdfDocumentReadService>();
    builder.Services.AddScoped<IJobOnControlOutputsService, JobOnControlOutputsService>();

    // ---- P2-T07 Boquilhas: Registo + Novo + Histórico + Definições ----------------------------
    // The aggregate/movement service composing the closed P2-T04/P2-T05 application contracts
    // (Tool/Job On/repairer/assignments) and the Boquilhas repository; the Definições service
    // owns the repairer family (Owner clarification P2-T07 §34.3 — same physical tables, moved
    // ownership/service/UI only). No policy, no availability entry, no destination route and no
    // second registry is added here: every P2-T07 route is server-gated by the accepted
    // `boquilhas` policy and stays denied to every caller until P2-T10 registers availability
    // (contract §13.3). The repository and the one additive dependency-probe line are registered
    // in AddPersistenceFoundation (§27).
    builder.Services.AddScoped<IBoquilhasService, BoquilhasService>();
    builder.Services.AddScoped<IBoquilhasDefinicoesService, BoquilhasDefinicoesService>();

    // One ADMIN-only policy (dmo.administration) + scoped handler; deliberately outside the
    // Module policy namespace. Administration is ADMIN-account functionality, not a Module.
    builder.Services.AddAdministrationAuthorization();

    // Razor Pages infrastructure is provided by the A2 shared frontend seam above
    // (AddDmoSharedFrontend); the P1-T05 ADMIN-only pages are mapped through it.
}
catch (DatabaseConfigurationException ex)
{
    // Fail loudly and legibly. The host must never start against a missing or invalid
    // database configuration, and must never substitute a default connection.
    Console.Error.WriteLine($"Startup failed: {ex.Message}");
    return StartupCommands.FailureExitCode;
}
catch (SupabaseConfigurationException ex)
{
    // Fail loudly and legibly for the same reason: no default Supabase configuration is
    // ever assumed and no unauthenticated fallback mode exists.
    Console.Error.WriteLine($"Startup failed: {ex.Message}");
    return StartupCommands.FailureExitCode;
}

var app = builder.Build();

// `--migrate` (or `migrate`) is a technical entry point that applies pending migrations and
// exits. P1-T03 adds the first two product migrations (AccountAndTemplateFoundation and
// TemplateModuleComposition).
if (StartupCommands.IsMigrationCommand(args))
{
    return await StartupCommands.RunMigrateAsync(app.Services, app.Logger);
}

// `--bootstrap-admin` (or `bootstrap-admin`) is the deployment-only single-ADMIN bootstrap
// (DEV/TEST), idempotent and provider-free.
if (StartupCommands.IsBootstrapAdminCommand(args))
{
    return await StartupCommands.RunBootstrapAdminAsync(app.Services, app.Logger);
}

app.UseDmoSharedFrontend();
app.UseAuthentication();
app.UseAuthorization();

app.MapTechnicalEndpoints();
app.MapAuthEndpoints();
app.MapDmoSharedFrontend();
app.MapUserAdministrationEndpoints();
app.MapTemplateAdministrationEndpoints();
app.MapJobOnEndpoints();
app.MapFerramentasEndpoints();
app.MapControloCreateEndpoints();
app.MapControloDefinicoesEndpoints();
app.MapControloApproveEndpoints();
app.MapDocumentsEndpoints();
app.MapBoquilhasEndpoints();
app.MapBoquilhasDefinicoesEndpoints();

return await StartupCommands.RunHostAsync(app);
