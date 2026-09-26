using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Application.Repositories;
using DMO.Application.Session;
using DMO.Application.UserAdministration;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using DMO.IntegrationTests.Host;
using DMO.Web.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace DMO.IntegrationTests.Auth;

/// <summary>
/// P1-T03 test — production composition registers the real boundaries and no fakes: the
/// real Supabase authentication boundary, the persistence-backed account/authentication
/// lookups, the real resolver, the real current-account context and the repository
/// primitives.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProductionCompositionTests
{
    [Fact]
    public void ProductionComposition_RegistersRealBoundariesAndNoFakes()
    {
        // Preconditions: the production test host (credential-free placeholder config).
        using var factory = new DmoWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        // Assertions: every boundary maps to the real production class.
        Assert.IsType<SupabaseAuthenticationService>(services.GetRequiredService<IAuthenticationBoundary>());
        Assert.IsType<PersistenceAccountLookup>(services.GetRequiredService<IAccountLookup>());
        Assert.IsType<UserAuthenticationLookup>(services.GetRequiredService<IUserAuthenticationLookup>());
        Assert.IsType<AccountResolver>(services.GetRequiredService<IAccountResolver>());
        Assert.IsType<SessionAuthentication>(services.GetRequiredService<ISessionAuthentication>());
        Assert.IsType<CurrentAccountContext>(services.GetRequiredService<ICurrentAccountContext>());

        // Repository primitives are registered (scoped), replacing the P1-T02 empty source.
        Assert.IsType<UserRepository>(services.GetRequiredService<IUserRepository>());
        Assert.IsType<AdminAccountRepository>(services.GetRequiredService<IAdminAccountRepository>());
        Assert.IsType<TemplateRepository>(services.GetRequiredService<ITemplateRepository>());
        Assert.IsType<TemplateModuleRepository>(services.GetRequiredService<ITemplateModuleRepository>());

        // P1-T05 administration: the real privileged provider boundary (server-only service
        // role; never the login path) and the real administration service are registered.
        Assert.IsType<SupabaseAdminUserService>(services.GetRequiredService<IUserIdentityProvisioner>());
        Assert.IsType<UserAdministrationService>(services.GetRequiredService<IUserAdministrationService>());

        // Required non-effect: no fake adapter, no sample user, no hidden bootstrap and no
        // hard-coded mapping exist in production registration (enforced by the concrete
        // types above; fakes live only in test projects).
    }
}