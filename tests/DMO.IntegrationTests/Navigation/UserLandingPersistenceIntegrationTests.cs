using DMO.Application.Access;
using DMO.Application.Accounts;
using DMO.Application.Session;
using DMO.Application.Templates;
using DMO.IntegrationTests.Persistence;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using DMO.Web.Frontend.Shell;
using DMO.Web.Navigation;
using Microsoft.EntityFrameworkCore;

namespace DMO.IntegrationTests.Navigation;

/// <summary>
/// P1-T07 env-gated persistence tests — the landing facts reach the runtime through the
/// real persisted Template read.
/// Test:
/// Purpose: prove persisted landing behavior end-to-end against the disposable PostgreSQL
/// database: explicit landing reaches the resolver (and the landing chain), null landing
/// round-trips, persisted Template order drives first-valid selection, and validation rules
/// (whole-resolution denial, empty/contextual compositions) fail closed — with zero schema
/// changes (existing migrations 001/002 only).
/// Master behavior being verified: ACCESS_MODEL Â§3/Â§12 (Template landing + navigation
/// projection) and request P1-T07 Â§26.3.
/// Preconditions: <c>DMO_TEST_POSTGRES_CONNECTION</c> pointing at a disposable PostgreSQL
/// database (otherwise every test skips); real repositories, real resolver, real projection
/// and real landing chain with a controlled test registry.
/// Action: persist a Template/User, run the real AccessResolver â†’ NavigationProjectionService
/// â†’ LandingSelector/UserLandingService chain, clean up.
/// Assertions: exact Granted landing fact and landing result per case.
/// Required non-effects: no schema change, no migration beyond 001/002, no live Supabase.
/// What this proves: the persisted landing fact and Template order flow through the accepted
/// runtime without new persistence.
/// What this does NOT prove: production Module availability (the controlled registry owns
/// availability here; ModuleRegistrations.CurrentBuildAvailable stays []).
/// </summary>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class UserLandingPersistenceIntegrationTests
{
    [SkippableFact]
    public async Task PersistedExplicitLanding_ReachesResolverAndRouting()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var (token, accountId, templateId) = await SeedAsync(
            context,
            templateName: $"tpl-{Guid.NewGuid():N}",
            landing: "controlo",
            modules: [("controlo-create", 1), ("controlo-approve", 2), ("job-on-view", 3)]);

        try
        {
            var (user, resolver) = await LoadAsync(context, token, accountId);

            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(user), CancellationToken.None);
            var granted = Assert.IsType<AccessOutcome.Granted>(outcome);
            Assert.Equal("controlo", granted.LandingDestinationId);
            Assert.Equal(
                new[] { ModuleCatalog.ControloCreate, ModuleCatalog.ControloApprove, ModuleCatalog.JobOnView },
                granted.EffectiveModules.Select(module => module.Id).ToArray());

            // The full landing chain: real projection â†’ selector â†’ service.
            var landing = await ResolveLandingAsync(context, user);
            var redirect = Assert.IsType<UserLanding.RedirectTo>(landing);
            Assert.Equal("/implemented/controlo", redirect.Route);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedNullLanding_SelectsFirstDestinationInPersistedOrder()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var (token, accountId, templateId) = await SeedAsync(
            context,
            templateName: $"tpl-{Guid.NewGuid():N}",
            landing: null,
            // Persisted order deliberately differs from canonical id order.
            modules: [("controlo-approve", 1), ("job-on-view", 2), ("controlo-create", 3)]);

        try
        {
            var (user, resolver) = await LoadAsync(context, token, accountId);

            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(user), CancellationToken.None);
            var granted = Assert.IsType<AccessOutcome.Granted>(outcome);
            Assert.Null(granted.LandingDestinationId);
            Assert.Equal(
                new[]
                {
                    ModuleCatalog.ControloApprove,
                    ModuleCatalog.JobOnView,
                    ModuleCatalog.ControloCreate,
                },
                granted.EffectiveModules.Select(module => module.Id).ToArray());

            // First valid destination per persisted Template order: controlo (approve first).
            var landing = await ResolveLandingAsync(context, user);
            var redirect = Assert.IsType<UserLanding.RedirectTo>(landing);
            Assert.Equal("/implemented/controlo", redirect.Route);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedUnavailableModule_DeniesWholeResolution_NoLanding()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // armazem is canonical but NOT available in the controlled test registry: the whole
        // resolution fails closed — even though an available sibling (controlo-create) and an
        // explicit landing were persisted.
        var (token, accountId, templateId) = await SeedAsync(
            context,
            templateName: $"tpl-{Guid.NewGuid():N}",
            landing: "controlo",
            modules: [("controlo-create", 1), ("armazem", 2)]);

        try
        {
            var (user, resolver) = await LoadAsync(context, token, accountId);

            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(user), CancellationToken.None);
            var denied = Assert.IsType<AccessOutcome.Denied>(outcome);
            Assert.Equal(AccessDenialReason.UnavailableModule, denied.Reason);

            var landing = await ResolveLandingAsync(context, user);
            Assert.IsType<UserLanding.NoAccess>(landing);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedUnknownModule_WholeResolutionDenial_NoLanding()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var (token, accountId, templateId) = await SeedAsync(
            context,
            templateName: $"tpl-{Guid.NewGuid():N}",
            landing: "controlo",
            modules: [("controlo-create", 1), ("nao-existe", 2)]);

        try
        {
            var (user, resolver) = await LoadAsync(context, token, accountId);

            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(user), CancellationToken.None);
            var denied = Assert.IsType<AccessOutcome.Denied>(outcome);
            Assert.Equal(AccessDenialReason.UnknownModule, denied.Reason);

            var landing = await ResolveLandingAsync(context, user);
            Assert.IsType<UserLanding.NoAccess>(landing);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedEmptyComposition_GrantedWithoutModules_NoLanding()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var (token, accountId, templateId) = await SeedAsync(
            context,
            templateName: $"tpl-{Guid.NewGuid():N}",
            landing: "controlo",
            modules: []);

        try
        {
            var (user, resolver) = await LoadAsync(context, token, accountId);

            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(user), CancellationToken.None);
            var granted = Assert.IsType<AccessOutcome.Granted>(outcome);
            Assert.Equal("controlo", granted.LandingDestinationId);
            Assert.Empty(granted.EffectiveModules);

            // Zero live destinations (no Module â†’ no destination): fail closed, and the
            // explicit landing cannot be represented â†’ no access.
            var landing = await ResolveLandingAsync(context, user);
            Assert.IsType<UserLanding.NoAccess>(landing);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedContextualOnlyComposition_GrantedWithoutDestinations_NoLanding()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // Ferramentas is contextual-only in the controlled registry: no top-level
        // destination can ever be produced from it.
        var (token, accountId, templateId) = await SeedAsync(
            context,
            templateName: $"tpl-{Guid.NewGuid():N}",
            landing: null,
            modules: [("ferramentas", 1)]);

        try
        {
            var (user, resolver) = await LoadAsync(context, token, accountId);

            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(user), CancellationToken.None);
            var granted = Assert.IsType<AccessOutcome.Granted>(outcome);
            Assert.Equal(
                new[] { ModuleCatalog.Ferramentas },
                granted.EffectiveModules.Select(module => module.Id).ToArray());

            var landing = await ResolveLandingAsync(context, user);
            Assert.IsType<UserLanding.NoAccess>(landing);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedSharedDestination_CollapsesAndKeepsCanonicalGrants()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var (token, accountId, templateId) = await SeedAsync(
            context,
            templateName: $"tpl-{Guid.NewGuid():N}",
            landing: "job-on",
            modules: [("job-on-view", 1), ("job-on-create", 2)]);

        try
        {
            var (user, resolver) = await LoadAsync(context, token, accountId);

            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(user), CancellationToken.None);
            var granted = Assert.IsType<AccessOutcome.Granted>(outcome);
            Assert.Equal("job-on", granted.LandingDestinationId);
            Assert.Equal(
                new[] { ModuleCatalog.JobOnView, ModuleCatalog.JobOnCreate },
                granted.EffectiveModules.Select(module => module.Id).ToArray());

            var landing = await ResolveLandingAsync(context, user);
            var redirect = Assert.IsType<UserLanding.RedirectTo>(landing);
            Assert.Equal("/implemented/job-on", redirect.Route);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    // ---- infrastructure ----------------------------------------------------------

    private static async Task<(string Token, Guid AccountId, Guid TemplateId)> SeedAsync(
        DmoDbContext context,
        string templateName,
        string? landing,
        IReadOnlyList<(string ModuleId, int Order)> modules)
    {
        var token = Guid.NewGuid().ToString("N");
        var accountId = Guid.NewGuid();
        var templates = new TemplateRepository(context);
        var users = new UserRepository(context);

        var templateId = await templates.CreatedAsync(
            new Template(Guid.Empty, templateName, LandingDestinationId: landing, Version: 1),
            modules
                .Select(entry => new TemplateModule(Guid.Empty, entry.ModuleId, entry.Order))
                .ToArray(),
            CancellationToken.None);

        var account = new UserAccount(
            accountId,
            CompanyNumber: $"cn-{token}",
            DisplayName: "JoÃ£o Silva",
            Email: $"user-{token}@dmo.test",
            RoleLabel: "Reparador",
            IsActive: true,
            TemplateId: templateId,
            Version: 1);
        await users.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

        return (token, accountId, templateId);
    }

    private static async Task<(UserAccount User, IAccessResolver Resolver)> LoadAsync(
        DmoDbContext context,
        string token,
        Guid accountId)
    {
        var users = new UserRepository(context);
        var templates = new TemplateRepository(context);
        var moduleReader = new TemplateModuleRepository(context);

        var account = await users.GetByCompanyNumberAsync($"cn-{token}", CancellationToken.None)
            ?? throw new InvalidOperationException("Seeded USER was not found.");
        Assert.Equal(accountId, account.AccountId);

        var resolver = new AccessResolver(TestRegistry(), templates, moduleReader);
        return (account, resolver);
    }

    private static async Task<UserLanding> ResolveLandingAsync(
        DmoDbContext context,
        UserAccount user)
    {
        var registry = TestRegistry();
        var projection = new NavigationProjectionService(
            new ModuleAccessService(
                new AccessResolver(
                    registry,
                    new TemplateRepository(context),
                    new TemplateModuleRepository(context))),
            registry,
            new DictionaryRouteRegistry(new Dictionary<string, string>
            {
                ["controlo"] = "/implemented/controlo",
                ["job-on"] = "/implemented/job-on",
            }));

        return await new UserLandingService(projection).ResolveAsync(
            new CurrentAccount.User(user), CancellationToken.None);
    }

    /// <summary>
    /// Controlled test registry: controlo-create/controlo-approve share destination
    /// <c>controlo</c>; job-on-view/job-on-create share <c>job-on</c>; ferramentas is
    /// contextual-only; <c>armazem</c> is canonical but deliberately not available.
    /// </summary>
    private static IModuleRegistry TestRegistry() => ModuleRegistry.Create(
    [
        Available("controlo-create", "Controlo Create", "controlo", ["peso-create", "submission"]),
        Available("controlo-approve", "Controlo Approve", "controlo", ["peso-approve", "send-to-production"]),
        Available("job-on-view", "Job On View", "job-on", ["read", "verificacao-confirm"]),
        Available("job-on-create", "Job On Create", "job-on", ["create", "edit", "manage"]),
        Available("ferramentas", "Ferramentas", null, isContextualOnly: true),
    ]);

    private static ModuleDefinition Available(
        string moduleId,
        string displayName,
        string? destinationId,
        string[]? protectedActions = null,
        bool isContextualOnly = false) =>
        new(
            ModuleCatalog.FindByValue(moduleId) ?? throw new ArgumentException($"Not canonical: {moduleId}"),
            displayName,
            destinationId,
            IsDefaultLandingEligible: false,
            new ModuleSurfaceDescriptor(
                displayName,
                destinationId,
                isContextualOnly,
                protectedActions ?? []));

    private static async Task CleanupAsync(
        DmoDbContext context,
        string token,
        Guid accountId,
        Guid templateId)
    {
        if (accountId != Guid.Empty)
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM users WHERE company_number = @p OR user_id = @q",
                new Npgsql.NpgsqlParameter("p", $"cn-{token}"),
                new Npgsql.NpgsqlParameter("q", accountId));
        }

        if (templateId != Guid.Empty)
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM templates WHERE template_id = @t",
                new Npgsql.NpgsqlParameter("t", templateId));
        }
    }

    private sealed class DictionaryRouteRegistry : IDestinationRouteRegistry
    {
        private readonly IReadOnlyDictionary<string, string> _routes;

        public DictionaryRouteRegistry(IReadOnlyDictionary<string, string> routes) => _routes = routes;

        public bool TryGetRoute(string destinationId, out string route) =>
            _routes.TryGetValue(destinationId, out route!);
    }
}