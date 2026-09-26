using DMO.Application.Accounts;
using DMO.Application.Persistence;
using DMO.Application.Templates;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using DMO.Infrastructure.Persistence.Entities;
using DMO.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DMO.IntegrationTests.UserAdministration;

/// <summary>
/// P1-T05 env-gated integration test — the USER administration persistence contract against a
/// disposable PostgreSQL database: additive <see cref="DMO.Application.Repositories.IUserRepository"/>
/// primitives, typed constraint mapping (unique company number / unique provider subject /
/// template FK), the non-destructive delete sequence with the recoverable stale-race at the
/// row boundary, and the carrier email feeding the authentication lookup.
/// Runs only when <c>DMO_TEST_POSTGRES_CONNECTION</c> points at a disposable database.
/// </summary>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class UserAdministrationPersistenceIntegrationTests
{
    private const string TestTokenPrefix = "pt05-";

    [SkippableFact]
    public async Task AdditiveReads_VersionedDelete_RoundTrip()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        var account = Account($"cn-{token}", $"user-{token}@dmo.test");

        try
        {
            var repository = new UserRepository(context);
            await repository.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            // Additive reads: exact email, auth linkage, and the full list.
            var byEmail = await repository.GetByEmailAsync($"user-{token}@dmo.test", CancellationToken.None);
            Assert.Equal(account.AccountId, byEmail!.AccountId);
            Assert.Null(await repository.GetByEmailAsync($"USER-{token}@dmo.test", CancellationToken.None));

            Assert.Equal($"subject-{token}", await repository.GetAuthIdentityIdAsync(account.AccountId, CancellationToken.None));
            Assert.Null(await repository.GetAuthIdentityIdAsync(Guid.NewGuid(), CancellationToken.None));

            var all = await repository.ListAsync(CancellationToken.None);
            Assert.Contains(all, candidate => candidate.AccountId == account.AccountId);

            // Versioned delete: success path with the observed version.
            await repository.DeleteAsync(account.AccountId, expectedVersion: 1, CancellationToken.None);
            Assert.Null(await repository.GetByIdAsync(account.AccountId, CancellationToken.None));

            // A missing row completes as a no-op (the caller decides how to interpret it).
            await repository.DeleteAsync(account.AccountId, expectedVersion: 1, CancellationToken.None);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task UniqueCompanyNumber_UniqueProviderSubject_MapToTypedPersistenceExceptions()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        var first = Account($"cn-{token}", $"user-{token}@dmo.test");

        try
        {
            var repository = new UserRepository(context);
            await repository.CreatedAsync(first, $"subject-{token}", CancellationToken.None);

            // Same company number -> typed duplicate, never a raw DbUpdateException.
            var duplicateCompany = await Assert.ThrowsAsync<UserPersistenceException>(() =>
                repository.CreatedAsync(
                    Account($"cn-{token}", $"other-{token}@dmo.test"),
                    $"subject-other-{token}",
                    CancellationToken.None));
            Assert.Equal(UserPersistenceFailureReason.DuplicateCompanyNumber, duplicateCompany.Reason);
            Assert.Contains("company number", duplicateCompany.Message, StringComparison.OrdinalIgnoreCase);
            context.ChangeTracker.Clear(); // a failed save leaves its entity pending; start clean

            // Same provider subject -> typed duplicate (CREATE path: the unique
            // users_auth_identity_id_key constraint decides which fact collided).
            var duplicateSubject = await Assert.ThrowsAsync<UserPersistenceException>(() =>
                repository.CreatedAsync(
                    Account($"cn-other-{token}", $"user-other-{token}@dmo.test"),
                    $"subject-{token}",
                    CancellationToken.None));
            Assert.Equal(UserPersistenceFailureReason.DuplicateProviderSubject, duplicateSubject.Reason);
            context.ChangeTracker.Clear(); // a failed save leaves its entity pending; start clean

            // The duplicate carrier email is ALLOWED at the persistence boundary (no app-level
            // unique constraint on users.email by design); the administration service guards
            // the one-identity-per-carrier rule above this primitive.
            await repository.CreatedAsync(
                Account($"cn-third-{token}", $"user-{token}@dmo.test"),
                $"subject-third-{token}",
                CancellationToken.None);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task StaleVersionedDelete_LeavesRow_WithAuthIdentityAnchor()
    {
        // The final step of the accepted delete sequence: a concurrently bumped version makes
        // the versioned row delete fail stale; the row SURVIVES with its provider linkage
        // (retry anchor) — nothing is deleted and nothing is overwritten.
        PersistenceTestDatabase.SkipIfNotConfigured();

        var token = UniqueToken();
        var account = Account($"cn-{token}", $"user-{token}@dmo.test");

        try
        {
            await using var contextA = PersistenceTestDatabase.CreateContext();
            await PersistenceTestDatabase.ApplyMigrationsAsync(contextA);
            var repositoryA = new UserRepository(contextA);
            await repositoryA.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            // Independent context: another writer bumps the row after the pre-check.
            await using var contextB = PersistenceTestDatabase.CreateContext();
            await new UserRepository(contextB).SetActiveAsync(
                account.AccountId, active: false, expectedVersion: 1, CancellationToken.None);

            // Context A runs the final versioned delete with its stale observation.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                repositoryA.DeleteAsync(account.AccountId, expectedVersion: 1, CancellationToken.None));

            // The row survives WITH the auth_identity_id anchor and the concurrent writer's state.
            await using var reload = PersistenceTestDatabase.CreateContext();
            var row = await reload.Users.SingleAsync(user => user.UserId == account.AccountId);
            Assert.Equal(2, row.Version);
            Assert.False(row.Active);
            Assert.Equal($"subject-{token}", row.AuthIdentityId);

            // Retry with the current version completes the delete.
            await new UserRepository(reload).DeleteAsync(account.AccountId, expectedVersion: 2, CancellationToken.None);
            Assert.Null(await reload.Users.SingleOrDefaultAsync(user => user.UserId == account.AccountId));
        }
        finally
        {
            await using var cleanup = PersistenceTestDatabase.CreateContext();
            await CleanupAsync(cleanup, token);
        }
    }

    [SkippableFact]
    public async Task EmailEdit_PersistsNewCarrier_And_AuthenticationLookup_SeesIt()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        var account = Account($"cn-{token}", $"user-{token}@dmo.test");

        try
        {
            var repository = new UserRepository(context);
            await repository.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            var observed = (await repository.GetByIdAsync(account.AccountId, CancellationToken.None))!;
            await repository.UpdatedAsync(
                observed with { Email = $"user-new-{token}@dmo.test" },
                CancellationToken.None);

            // The persistence contract keeps the auth lookup in sync: the same company number
            // now resolves the NEW carrier email.
            var lookup = new UserAuthenticationLookup(context);
            var identity = await lookup.GetByCompanyNumberAsync($"cn-{token}", CancellationToken.None);
            Assert.NotNull(identity);
            Assert.Equal($"user-new-{token}@dmo.test", identity!.CarrierEmail);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task TemplateAssignment_Reassign_Remove_AndFkRestrict_Evidence()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        var account = Account($"cn-{token}", $"user-{token}@dmo.test");

        try
        {
            // Seed two Templates through the real repository (it owns the identity: the
            // RETURNED ids are the authoritative template ids).
            var templates = new TemplateRepository(context);
            var templateId = await templates.CreatedAsync(
                new Template(Guid.NewGuid(), $"tpl-{token}-A", LandingDestinationId: null, Version: 1),
                [],
                CancellationToken.None);
            var otherTemplateId = await templates.CreatedAsync(
                new Template(Guid.NewGuid(), $"tpl-{token}-B", LandingDestinationId: null, Version: 1),
                [],
                CancellationToken.None);

            var repository = new UserRepository(context);
            await repository.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            // Assign -> reassign -> remove: always exactly one relationship.
            await repository.SetTemplateAsync(account.AccountId, templateId, expectedVersion: 1, CancellationToken.None);
            Assert.Equal(templateId, (await repository.GetByIdAsync(account.AccountId, CancellationToken.None))!.TemplateId);

            await repository.SetTemplateAsync(account.AccountId, otherTemplateId, expectedVersion: 2, CancellationToken.None);
            Assert.Equal(otherTemplateId, (await repository.GetByIdAsync(account.AccountId, CancellationToken.None))!.TemplateId);

            await repository.SetTemplateAsync(account.AccountId, null, expectedVersion: 3, CancellationToken.None);
            Assert.Null((await repository.GetByIdAsync(account.AccountId, CancellationToken.None))!.TemplateId);

            // Schema evidence (migrations unchanged): a referenced template cannot be deleted
            // (ON DELETE RESTRICT, users_template_id_fkey) — direct SQL delete is rejected at
            // the database, proving the FK constraint guards against dangling template links.
            await repository.SetTemplateAsync(account.AccountId, templateId, expectedVersion: 4, CancellationToken.None);
            var fkViolation = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM templates WHERE template_id = @p",
                    new Npgsql.NpgsqlParameter("p", templateId)));
            Assert.Equal("23503", fkViolation.SqlState);

            // After removing the association the template becomes deletable.
            await repository.SetTemplateAsync(account.AccountId, null, expectedVersion: 5, CancellationToken.None);
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM templates WHERE template_id = @p",
                new Npgsql.NpgsqlParameter("p", templateId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task InvalidTemplateReference_OnCreateAndTemplateWrite_MapToTypedPersistenceException()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        var account = Account($"cn-{token}", $"user-{token}@dmo.test");

        try
        {
            var repository = new UserRepository(context);
            var missingTemplateId = Guid.NewGuid();

            // Create referencing a non-existent template (FK 23503 -> typed invalid reference).
            var create = await Assert.ThrowsAsync<UserPersistenceException>(() =>
                repository.CreatedAsync(
                    account with { TemplateId = missingTemplateId },
                    $"subject-{token}",
                    CancellationToken.None));
            Assert.Equal(UserPersistenceFailureReason.InvalidTemplateReference, create.Reason);
            context.ChangeTracker.Clear(); // a failed save leaves its entity pending; start clean

            // Create a valid row, then a template assignment to a missing template.
            await repository.CreatedAsync(account, $"subject-{token}", CancellationToken.None);
            var assign = await Assert.ThrowsAsync<UserPersistenceException>(() =>
                repository.SetTemplateAsync(account.AccountId, missingTemplateId, expectedVersion: 1, CancellationToken.None));
            Assert.Equal(UserPersistenceFailureReason.InvalidTemplateReference, assign.Reason);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task ListAsync_OrdersByDisplayName_ThenCompanyNumber()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        var zeta = Account($"cn-z-{token}", $"z-{token}@dmo.test", name: "Zeta");
        var alfaB = Account($"cn-b-{token}", $"ab-{token}@dmo.test", name: "Alfa");
        var alfaA = Account($"cn-a-{token}", $"aa-{token}@dmo.test", name: "Alfa");

        try
        {
            var repository = new UserRepository(context);
            await repository.CreatedAsync(zeta, $"s-z-{token}", CancellationToken.None);
            await repository.CreatedAsync(alfaB, $"s-ab-{token}", CancellationToken.None);
            await repository.CreatedAsync(alfaA, $"s-aa-{token}", CancellationToken.None);

            var all = await repository.ListAsync(CancellationToken.None);
            var names = all
                .Where(candidate => candidate.CompanyNumber.StartsWith("cn-", StringComparison.Ordinal)
                    && (candidate.CompanyNumber == zeta.CompanyNumber
                        || candidate.CompanyNumber == alfaA.CompanyNumber
                        || candidate.CompanyNumber == alfaB.CompanyNumber))
                .Select(candidate => (candidate.DisplayName, candidate.CompanyNumber))
                .ToArray();

            Assert.Equal(
                new[] { ("Alfa", alfaA.CompanyNumber), ("Alfa", alfaB.CompanyNumber), ("Zeta", zeta.CompanyNumber) },
                names);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task Deactivate_ExcludesFromActiveList_ReactivateRestores()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        var account = Account($"cn-{token}", $"user-{token}@dmo.test");

        try
        {
            var repository = new UserRepository(context);
            await repository.CreatedAsync(account, $"subject-{token}", CancellationToken.None);

            await repository.SetActiveAsync(account.AccountId, active: false, expectedVersion: 1, CancellationToken.None);
            Assert.DoesNotContain(
                await repository.ListActiveAsync(CancellationToken.None),
                candidate => candidate.AccountId == account.AccountId);

            await repository.SetActiveAsync(account.AccountId, active: true, expectedVersion: 2, CancellationToken.None);
            Assert.Contains(
                await repository.ListActiveAsync(CancellationToken.None),
                candidate => candidate.AccountId == account.AccountId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string UniqueToken() =>
        $"{TestTokenPrefix}{Guid.NewGuid():N}";

    private static UserAccount Account(string companyNumber, string email, string? name = null) => new(
        Guid.NewGuid(),
        CompanyNumber: companyNumber,
        DisplayName: name ?? "João Silva",
        Email: email,
        RoleLabel: "Reparador",
        IsActive: true,
        TemplateId: null,
        Version: 1);

    private static Task CleanupAsync(DmoDbContext context, string token) =>
        context.Database.ExecuteSqlRawAsync(
            "DELETE FROM users WHERE company_number LIKE '%' || @t || '%' "
            + "OR email LIKE '%' || @t || '%'; "
            + "DELETE FROM templates WHERE name LIKE '%' || @t || '%'",
            new Npgsql.NpgsqlParameter("t", token));
}