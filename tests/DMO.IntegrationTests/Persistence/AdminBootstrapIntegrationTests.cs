using DMO.Application.Accounts;
using DMO.Application.Repositories;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using DMO.Web.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P1-T03 env-gated integration test — the real single-ADMIN bootstrap against a database:
/// absent → insert; same email + same subject → idempotent no-op; conflicting email or
/// subject → explicit failure that never overwrites.
/// </summary>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class AdminBootstrapIntegrationTests
{
    [SkippableFact]
    public async Task Bootstrap_AbsentRow_InsertsSingleton_ThenIsIdempotent_ThenConflictsExplicitly()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        try
        {
            await PersistenceTestDatabase.ClearAdminAccountsAsync(context);

            var repository = new AdminAccountRepository(context);

            // 1. Absent row -> insert with the fixed AdminAccountId; success.
            var created = await BootstrapAsync(repository, "admin@dmo.test", "DMO Admin", "subject-1");
            Assert.Equal(StartupCommands.SuccessExitCode, created);

            var row = await context.AdminAccounts.SingleAsync();
            Assert.Equal(AdminAccountId.Value, row.AdminId);
            Assert.Equal("admin@dmo.test", row.Email);
            Assert.Equal("subject-1", row.AuthIdentityId);
            Assert.Equal("DMO Admin", row.DisplayName);
            Assert.True(row.Active);

            // 2. Same email + same subject -> no-op success; still exactly one row.
            var noOp = await BootstrapAsync(repository, "admin@dmo.test", "DMO Admin", "subject-1");
            Assert.Equal(StartupCommands.SuccessExitCode, noOp);
            Assert.Equal(1, await context.AdminAccounts.CountAsync());

            // 3. Same email but different subject -> explicit conflict; row unchanged.
            var subjectConflict = await BootstrapAsync(repository, "admin@dmo.test", "DMO Admin", "subject-2");
            Assert.Equal(StartupCommands.FailureExitCode, subjectConflict);
            Assert.Equal(1, await context.AdminAccounts.CountAsync());
            Assert.Equal("subject-1", (await context.AdminAccounts.SingleAsync()).AuthIdentityId);

            // 4. Different email -> explicit conflict; row unchanged (never overwritten).
            var emailConflict = await BootstrapAsync(repository, "other@dmo.test", "Other", "subject-1");
            Assert.Equal(StartupCommands.FailureExitCode, emailConflict);
            Assert.Equal(1, await context.AdminAccounts.CountAsync());
            Assert.Equal("admin@dmo.test", (await context.AdminAccounts.SingleAsync()).Email);
        }
        finally
        {
            await PersistenceTestDatabase.ClearAdminAccountsAsync(context);
        }
    }

    [SkippableFact]
    public async Task Bootstrap_MissingConfiguration_FailsWithoutTouchingTheTable()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        await PersistenceTestDatabase.ClearAdminAccountsAsync(context);

        var repository = new AdminAccountRepository(context);

        var missingEmail = await BootstrapAsync(repository, email: null, "DMO Admin", "subject-1");
        Assert.Equal(StartupCommands.FailureExitCode, missingEmail);
        Assert.Equal(0, await context.AdminAccounts.CountAsync());

        var missingSubject = await BootstrapAsync(repository, "admin@dmo.test", "DMO Admin", null);
        Assert.Equal(StartupCommands.FailureExitCode, missingSubject);
        Assert.Equal(0, await context.AdminAccounts.CountAsync());

        var whitespaceOnly = await BootstrapAsync(repository, "   ", "DMO Admin", "subject-1");
        Assert.Equal(StartupCommands.FailureExitCode, whitespaceOnly);
        Assert.Equal(0, await context.AdminAccounts.CountAsync());
    }

    private static Task<int> BootstrapAsync(
        IAdminAccountRepository repository,
        string? email,
        string? displayName,
        string? authIdentityId)
    {
        var command = new AdminBootstrapCommand(
            repository,
            Options.Create(new AdminBootstrapOptions
            {
                Email = email,
                DisplayName = displayName,
                AuthIdentityId = authIdentityId,
            }),
            NullLogger<AdminBootstrapCommand>.Instance);

        return command.ExecuteAsync(CancellationToken.None);
    }
}