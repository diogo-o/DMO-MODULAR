using DMO.Application.Accounts;
using DMO.Application.Authentication;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P1-T03 env-gated integration test — <see cref="DMO.Infrastructure.Persistence.Access.PersistenceAccountLookup"/>
/// against a real database: per-path subject resolution, unknown subject → empty, and
/// active-filtering stays in the resolver.
/// </summary>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class PersistenceAccountLookupIntegrationTests
{
    [SkippableFact]
    public async Task Lookup_ResolvesByProviderSubject_PerPath()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var userSubject = $"user-subject-{token}";
        var adminSubject = $"admin-subject-{token}";

        try
        {
            context.Users.Add(new UserEntity
            {
                UserId = userId,
                AuthIdentityId = userSubject,
                Name = "João Silva",
                CompanyNumber = $"cn-{token}",
                Email = $"user-{token}@dmo.test",
                Active = true,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            context.AdminAccounts.Add(new AdminAccountEntity
            {
                AdminId = AdminAccountId.Value,
                AuthIdentityId = adminSubject,
                DisplayName = "DMO Admin",
                Email = $"admin-{token}@dmo.test",
                Active = true,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();

            var lookup = new DMO.Infrastructure.Persistence.Access.PersistenceAccountLookup(context);

            // User path: subject match -> exactly one User match.
            var userMatches = await lookup.LookupAsync(
                new AuthenticatedIdentity(userSubject, AuthenticationPath.User), CancellationToken.None);
            var userMatch = Assert.IsType<AccountMatch.User>(Assert.Single(userMatches));
            Assert.Equal(userId, userMatch.Account.AccountId);
            Assert.Equal($"cn-{token}", userMatch.Account.CompanyNumber);

            // Admin path: subject match -> exactly one Admin match.
            var adminMatches = await lookup.LookupAsync(
                new AuthenticatedIdentity(adminSubject, AuthenticationPath.Admin), CancellationToken.None);
            var adminMatch = Assert.IsType<AccountMatch.Admin>(Assert.Single(adminMatches));
            Assert.Equal(AdminAccountId.Value, adminMatch.Account.AccountId);

            // Unknown subject -> empty, per path.
            Assert.Empty(await lookup.LookupAsync(
                new AuthenticatedIdentity("unknown-subject", AuthenticationPath.User), CancellationToken.None));
            Assert.Empty(await lookup.LookupAsync(
                new AuthenticatedIdentity("unknown-subject", AuthenticationPath.Admin), CancellationToken.None));

            // Active filtering is resolver-only: an inactive row still yields a match.
            var inactiveSubject = $"inactive-{token}";
            context.Users.Add(new UserEntity
            {
                UserId = Guid.NewGuid(),
                AuthIdentityId = inactiveSubject,
                Name = "Inactive",
                CompanyNumber = $"icn-{token}",
                Email = $"inactive-{token}@dmo.test",
                Active = false,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();

            var inactiveMatches = await lookup.LookupAsync(
                new AuthenticatedIdentity(inactiveSubject, AuthenticationPath.User), CancellationToken.None);
            var inactive = Assert.IsType<AccountMatch.User>(Assert.Single(inactiveMatches));
            Assert.False(inactive.Account.IsActive);

            // And the real resolver turns that into NoAccess(InactiveUser) — the lookup
            // never classifies.
            var resolution = await new AccountResolver(lookup).ResolveAsync(
                new AuthenticatedIdentity(inactiveSubject, AuthenticationPath.User), CancellationToken.None);
            var noAccess = Assert.IsType<AccountResolution.NoAccess>(resolution);
            Assert.Equal(NoAccessReason.InactiveUser, noAccess.Reason);
        }
        finally
        {
            await PersistenceTestDatabase.ClearAdminAccountsAsync(context);
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM users WHERE company_number LIKE '%' || @t || '%'",
                new Npgsql.NpgsqlParameter("t", token));
        }
    }
}