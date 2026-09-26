using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.UnitTests.Accounts;

/// <summary>
/// P1-T03 tests — <see cref="PersistenceAccountLookup"/> mapping behaviour over an
/// in-memory persistence model.
/// </summary>
/// <remarks>
/// The lookup is the data boundary only: it returns candidate matches from the persisted
/// mapping keyed by provider subject and never classifies (active/inactive/path decisions
/// stay in <see cref="AccountResolver"/>). Real-database behaviour is covered by the
/// env-gated integration tests.
/// </remarks>
public sealed class PersistenceAccountLookupTests
{
    private static DmoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseInMemoryDatabase($"lookup-{Guid.NewGuid():N}")
            .Options;
        return new DmoDbContext(options);
    }

    private static PersistenceAccountLookup Lookup(DmoDbContext context) => new(context);

    [Fact]
    public async Task AdminPath_SubjectMatch_ReturnsSingleAdminMatch()
    {
        using var context = CreateContext();
        context.AdminAccounts.Add(new AdminAccountEntity
        {
            AdminId = AdminAccountId.Value,
            AuthIdentityId = "admin-subject-1",
            DisplayName = "DMO Admin",
            Email = "admin@dmo.test",
            Active = true,
            Version = 1,
        });
        await context.SaveChangesAsync();

        var matches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("admin-subject-1", AuthenticationPath.Admin),
            CancellationToken.None);

        var match = Assert.IsType<AccountMatch.Admin>(Assert.Single(matches));
        Assert.Equal(AdminAccountId.Value, match.Account.AccountId);
        Assert.Equal("admin@dmo.test", match.Account.Email);
    }

    [Fact]
    public async Task AdminPath_NoMatch_ReturnsEmpty()
    {
        using var context = CreateContext();

        var matches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("unknown-subject", AuthenticationPath.Admin),
            CancellationToken.None);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task UserPath_SubjectMatch_ReturnsSingleUserMatch()
    {
        using var context = CreateContext();
        context.Users.Add(new UserEntity
        {
            UserId = Guid.NewGuid(),
            AuthIdentityId = "user-subject-1",
            Name = "João Silva",
            CompanyNumber = "2661",
            Email = "joao@dmo.test",
            Role = "Reparador",
            Active = true,
            Version = 1,
        });
        await context.SaveChangesAsync();

        var matches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("user-subject-1", AuthenticationPath.User),
            CancellationToken.None);

        var match = Assert.IsType<AccountMatch.User>(Assert.Single(matches));
        Assert.Equal("2661", match.Account.CompanyNumber);
        Assert.Equal("Reparador", match.Account.RoleLabel);
        Assert.Null(match.Account.TemplateId);
    }

    [Fact]
    public async Task UserPath_NoMatch_ReturnsEmpty()
    {
        using var context = CreateContext();

        var matches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("unknown-subject", AuthenticationPath.User),
            CancellationToken.None);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task NeverClassifies_ReturnsMatchesForInactiveRows()
    {
        // Active filtering is resolver-only: the lookup must still return the candidate match
        // for an inactive row so the resolver can produce NoAccess(InactiveUser/InactiveAdmin).
        using var context = CreateContext();
        context.Users.Add(new UserEntity
        {
            UserId = Guid.NewGuid(),
            AuthIdentityId = "inactive-user-subject",
            Name = "Inactive",
            CompanyNumber = "9999",
            Email = "inactive@dmo.test",
            Active = false,
            Version = 1,
        });
        context.AdminAccounts.Add(new AdminAccountEntity
        {
            AdminId = AdminAccountId.Value,
            AuthIdentityId = "inactive-admin-subject",
            DisplayName = "Inactive Admin",
            Email = "inactive-admin@dmo.test",
            Active = false,
            Version = 1,
        });
        await context.SaveChangesAsync();

        var userMatches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("inactive-user-subject", AuthenticationPath.User),
            CancellationToken.None);

        var user = Assert.IsType<AccountMatch.User>(Assert.Single(userMatches));
        Assert.False(user.Account.IsActive);

        var adminMatches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("inactive-admin-subject", AuthenticationPath.Admin),
            CancellationToken.None);

        var admin = Assert.IsType<AccountMatch.Admin>(Assert.Single(adminMatches));
        Assert.False(admin.Account.IsActive);
    }

    [Fact]
    public async Task PathBoundary_DoesNotLeakAcrossAccountTables()
    {
        // A subject existing only in users must never match through the Admin path, and
        // vice versa: the lookup queries exactly the table of the authentication path.
        using var context = CreateContext();
        context.Users.Add(new UserEntity
        {
            UserId = Guid.NewGuid(),
            AuthIdentityId = "shared-subject",
            Name = "User",
            CompanyNumber = "2661",
            Email = "user@dmo.test",
            Active = true,
            Version = 1,
        });
        context.AdminAccounts.Add(new AdminAccountEntity
        {
            AdminId = AdminAccountId.Value,
            AuthIdentityId = "admin-only-subject",
            DisplayName = "Admin",
            Email = "admin@dmo.test",
            Active = true,
            Version = 1,
        });
        await context.SaveChangesAsync();

        var adminPathMatches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("shared-subject", AuthenticationPath.Admin),
            CancellationToken.None);
        Assert.Empty(adminPathMatches);

        var userPathMatches = await Lookup(context).LookupAsync(
            new AuthenticatedIdentity("admin-only-subject", AuthenticationPath.User),
            CancellationToken.None);
        Assert.Empty(userPathMatches);
    }
}