using DMO.Application.Access;
using DMO.Application.Accounts;
using DMO.Application.Templates;
using DMO.IntegrationTests.Persistence;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using Microsoft.EntityFrameworkCore;

namespace DMO.IntegrationTests.Access;

/// <summary>
/// P1-T04 env-gated integration test — the real <see cref="AccessResolver"/> end-to-end
/// against the disposable PostgreSQL database using the accepted P1-T03 repositories
/// (<c>users.template_id</c>, <c>templates</c>, <c>template_modules</c>).
/// </summary>
/// <remarks>
/// Same posture as the P1-T03 persistence tests: runs only when
/// <c>DMO_TEST_POSTGRES_CONNECTION</c> points at a disposable PostgreSQL database (otherwise
/// skipped). No schema changes, no migrations, no Supabase contact. The registry used here is
/// a controlled test registry (request §14): selected definitions available, <c>armazem</c>
/// deliberately known-but-unavailable.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class ModuleAccessResolverIntegrationTests
{
    [SkippableFact]
    public async Task PersistedTemplateModules_ResolveThroughRegistry_ToEffectiveModules()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var accountId = Guid.NewGuid();
        Guid templateId = Guid.Empty;

        try
        {
            var templates = new TemplateRepository(context);
            var moduleReader = new TemplateModuleRepository(context);
            var users = new UserRepository(context);

            templateId = await templates.CreatedAsync(
                new Template(Guid.Empty, $"tpl-{token}", LandingDestinationId: null, Version: 1),
                [
                    new TemplateModule(Guid.Empty, "controlo-create", 1),
                    new TemplateModule(Guid.Empty, "controlo-approve", 2),
                ],
                CancellationToken.None);

            var account = new UserAccount(
                accountId,
                CompanyNumber: $"cn-{token}",
                DisplayName: "João Silva",
                Email: $"user-{token}@dmo.test",
                RoleLabel: "Reparador",
                IsActive: true,
                TemplateId: templateId,
                Version: 1);
            await users.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            var resolver = new AccessResolver(TestRegistry(), templates, moduleReader);
            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(account), CancellationToken.None);

            var granted = Assert.IsType<AccessOutcome.Granted>(outcome);
            Assert.Equal(
                new[] { ModuleCatalog.ControloCreate, ModuleCatalog.ControloApprove },
                granted.EffectiveModules.Select(module => module.Id).ToArray());
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedUserWithNullTemplate_FailsClosed()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var accountId = Guid.NewGuid();

        try
        {
            var templates = new TemplateRepository(context);
            var moduleReader = new TemplateModuleRepository(context);
            var users = new UserRepository(context);

            var account = new UserAccount(
                accountId,
                CompanyNumber: $"cn-{token}",
                DisplayName: "João Silva",
                Email: $"user-{token}@dmo.test",
                RoleLabel: "Reparador",
                IsActive: true,
                TemplateId: null,
                Version: 1);
            await users.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            var resolver = new AccessResolver(TestRegistry(), templates, moduleReader);
            var outcome = await resolver.ResolveAccessAsync(
                new AccountResolution.User(account), CancellationToken.None);

            var denied = Assert.IsType<AccessOutcome.Denied>(outcome);
            Assert.Equal(AccessDenialReason.NoTemplate, denied.Reason);
        }
        finally
        {
            await CleanupAsync(context, token, accountId, Guid.Empty);
        }
    }

    [SkippableFact]
    public async Task PersistedUnknownModuleId_FailsClosed()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var accountId = Guid.NewGuid();
        Guid templateId = Guid.Empty;

        try
        {
            var templates = new TemplateRepository(context);
            var moduleReader = new TemplateModuleRepository(context);
            var users = new UserRepository(context);

            templateId = await templates.CreatedAsync(
                new Template(Guid.Empty, $"tpl-{token}", LandingDestinationId: null, Version: 1),
                [
                    new TemplateModule(Guid.Empty, "controlo-approve", 1),
                    new TemplateModule(Guid.Empty, "nao-existe", 2),
                ],
                CancellationToken.None);

            var account = new UserAccount(
                accountId,
                CompanyNumber: $"cn-{token}",
                DisplayName: "João Silva",
                Email: $"user-{token}@dmo.test",
                RoleLabel: "Reparador",
                IsActive: true,
                TemplateId: templateId,
                Version: 1);
            await users.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            var service = new ModuleAccessService(new AccessResolver(TestRegistry(), templates, moduleReader));
            var user = new AccountResolution.User(account);

            var outcome = await service.ResolveUserAccessAsync(user, CancellationToken.None);
            var denied = Assert.IsType<AccessOutcome.Denied>(outcome);
            Assert.Equal(AccessDenialReason.UnknownModule, denied.Reason);

            // No partial grant: the available sibling is NOT granted.
            Assert.False(await service.HasModuleAsync(user, ModuleCatalog.ControloApprove, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedKnownUnavailableModule_FailsClosedEntireResolution()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var accountId = Guid.NewGuid();
        Guid templateId = Guid.Empty;

        try
        {
            var templates = new TemplateRepository(context);
            var moduleReader = new TemplateModuleRepository(context);
            var users = new UserRepository(context);

            // controlo-create is available; armazem is canonical but NOT available in the
            // controlled test registry.
            templateId = await templates.CreatedAsync(
                new Template(Guid.Empty, $"tpl-{token}", LandingDestinationId: null, Version: 1),
                [
                    new TemplateModule(Guid.Empty, "controlo-create", 1),
                    new TemplateModule(Guid.Empty, "armazem", 2),
                ],
                CancellationToken.None);

            var account = new UserAccount(
                accountId,
                CompanyNumber: $"cn-{token}",
                DisplayName: "João Silva",
                Email: $"user-{token}@dmo.test",
                RoleLabel: "Reparador",
                IsActive: true,
                TemplateId: templateId,
                Version: 1);
            await users.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            var service = new ModuleAccessService(new AccessResolver(TestRegistry(), templates, moduleReader));
            var user = new AccountResolution.User(account);

            var outcome = await service.ResolveUserAccessAsync(user, CancellationToken.None);
            var denied = Assert.IsType<AccessOutcome.Denied>(outcome);
            Assert.Equal(AccessDenialReason.UnavailableModule, denied.Reason);

            // NO partial access survives: the available sibling is NOT granted.
            Assert.False(await service.HasModuleAsync(user, ModuleCatalog.ControloCreate, CancellationToken.None));
            Assert.False(await service.HasModuleAsync(user, ModuleCatalog.Armazem, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    [SkippableFact]
    public async Task PersistedSelection_IgnoresRoleAndTemplateName()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var accountId = Guid.NewGuid();
        Guid templateId = Guid.Empty;

        try
        {
            var templates = new TemplateRepository(context);
            var moduleReader = new TemplateModuleRepository(context);
            var users = new UserRepository(context);

            // Template named "Operador" — a name that must grant nothing by itself.
            templateId = await templates.CreatedAsync(
                new Template(Guid.Empty, "Operador", LandingDestinationId: null, Version: 1),
                [new TemplateModule(Guid.Empty, "controlo-approve", 1)],
                CancellationToken.None);

            // USER role "Responsável" — a presentation label that must grant nothing.
            var account = new UserAccount(
                accountId,
                CompanyNumber: $"cn-{token}",
                DisplayName: "João Silva",
                Email: $"user-{token}@dmo.test",
                RoleLabel: "Responsável",
                IsActive: true,
                TemplateId: templateId,
                Version: 1);
            await users.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            var service = new ModuleAccessService(new AccessResolver(TestRegistry(), templates, moduleReader));
            var user = new AccountResolution.User(account);

            var outcome = await service.ResolveUserAccessAsync(user, CancellationToken.None);
            var granted = Assert.IsType<AccessOutcome.Granted>(outcome);
            Assert.Equal(
                new[] { ModuleCatalog.ControloApprove },
                granted.EffectiveModules.Select(module => module.Id).ToArray());

            // Role/name change nothing on the gates either.
            Assert.True(await service.HasModuleAsync(user, ModuleCatalog.ControloApprove, CancellationToken.None));
            Assert.False(await service.HasModuleAsync(user, ModuleCatalog.ControloCreate, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(context, token, accountId, templateId);
        }
    }

    /// <summary>
    /// Controlled test registry for the persistence end-to-end tests: the Modules the tests
    /// treat as available. <c>armazem</c> is canonical but not registered here.
    /// </summary>
    private static IModuleRegistry TestRegistry() => ModuleRegistry.Create(
    [
        Available("controlo-create", "Controlo Create", "controlo", ["peso-create", "submission"]),
        Available("controlo-approve", "Controlo Approve", "controlo", ["peso-approve", "send-to-production"]),
    ]);

    private static ModuleDefinition Available(
        string moduleId,
        string displayName,
        string destinationId,
        string[] protectedActions) =>
        new(
            ModuleCatalog.FindByValue(moduleId) ?? throw new ArgumentException($"Not canonical: {moduleId}"),
            displayName,
            destinationId,
            IsDefaultLandingEligible: false,
            new ModuleSurfaceDescriptor(displayName, destinationId, IsContextualOnly: false, protectedActions));

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
}