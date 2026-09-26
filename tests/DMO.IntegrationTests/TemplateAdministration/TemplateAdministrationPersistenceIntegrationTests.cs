using DMO.Application.Accounts;
using DMO.Application.Access;
using DMO.Application.Persistence;
using DMO.Application.TemplateAdministration;
using DMO.Application.Templates;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Access;
using DMO.IntegrationTests.Persistence;
using DMO.Application.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DMO.IntegrationTests.TemplateAdministration;

/// <summary>
/// P1-T06 env-gated integration tests — the Template administration persistence contract
/// against a disposable PostgreSQL database: the exact atomic delete-with-members
/// transaction, the single <c>users.template_id</c> relation read from both directions, the
/// dense ordered composition with its schema backstops, and the optimistic concurrency
/// boundaries (stale Template/User version → conflict, rollback, nothing changed).
/// Runs only when <c>DMO_TEST_POSTGRES_CONNECTION</c> points at a disposable database
/// (migrations 001/002 applied against the real schema — no in-memory substitute).
/// </summary>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class TemplateAdministrationPersistenceIntegrationTests
{
    private const string TestTokenPrefix = "pt06-";

    [SkippableFact]
    public async Task Create_Read_CompositionDense_WithLanding_Version1()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var repository = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(repository, $"T-{token}", "job-on", ["job-on-view", "controlo-create"]);

            var loaded = await repository.GetByIdAsync(templateId, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal($"T-{token}", loaded!.Name);
            Assert.Equal("job-on", loaded.LandingDestinationId);
            Assert.Equal(1, loaded.Version);

            // Ordered composition, dense presentation order 1..n exactly as submitted.
            var composition = await new TemplateModuleRepository(context)
                .GetByTemplateAsync(templateId, CancellationToken.None);
            Assert.Equal(
                ["job-on-view", "controlo-create"],
                composition.Select(module => module.ModuleId).ToArray());
            Assert.Equal([1, 2], composition.Select(module => module.PresentationOrder).ToArray());
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task ListByTemplate_OrdersByNameThenCompany_IncludesActiveAndInactive()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", null, ["job-on-view"]);

            var zeta = await CreateAccountAsync(users, $"cn-z-{token}", name: "Zeta", templateId: templateId);
            var alfaA = await CreateAccountAsync(users, $"cn-a-{token}", name: "Alfa", templateId: templateId);
            var alfaB = await CreateAccountAsync(users, $"cn-b-{token}", name: "Alfa", templateId: templateId, active: false);
            await CreateAccountAsync(users, $"cn-x-{token}", name: "Xis", templateId: null);

            // Active AND inactive both listed; the unassigned user is not.
            var members = await users.ListByTemplateAsync(templateId, CancellationToken.None);
            Assert.Equal(
                new[] { alfaA.AccountId, alfaB.AccountId, zeta.AccountId },
                members.Select(member => member.AccountId).ToArray());
            Assert.Single(members, member => !member.IsActive);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task Assign_Remove_Reassign_SingleColumnWrite_NoDualMembership()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var templateA = await CreateTemplateAsync(templates, $"A-{token}", null, ["job-on-view"]);
            var templateB = await CreateTemplateAsync(templates, $"B-{token}", null, ["controlo-create"]);
            var account = await CreateAccountAsync(users, $"cn-{token}");

            // Assign A.
            await users.SetTemplateAsync(account.AccountId, templateA, expectedVersion: 1, CancellationToken.None);
            var afterA = (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!;
            Assert.Equal(templateA, afterA.TemplateId);
            Assert.Equal(2, afterA.Version);

            // Reassign A -> B: one column, never A + B.
            await users.SetTemplateAsync(account.AccountId, templateB, expectedVersion: 2, CancellationToken.None);
            var afterB = (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!;
            Assert.Equal(templateB, afterB.TemplateId);

            // Remove: template_id -> null; user keeps active state and version bumps.
            await users.SetTemplateAsync(account.AccountId, null, expectedVersion: 3, CancellationToken.None);
            var removed = (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!;
            Assert.Null(removed.TemplateId);
            Assert.True(removed.IsActive);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task Transversal_UserAndTemplateReads_AgreeOnTheSingleRelation()
    {
        // The very same users.template_id column feeds both ficha directions: the USER ficha
        // read (GetByIdAsync.TemplateId) and the Template ficha read (ListByTemplateAsync).
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", null, ["job-on-view"]);
            var account = await CreateAccountAsync(users, $"cn-{token}");
            await users.SetTemplateAsync(account.AccountId, templateId, expectedVersion: 1, CancellationToken.None);

            var fromUserFicha = (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!.TemplateId;
            var fromTemplateFicha = await users.ListByTemplateAsync(templateId, CancellationToken.None);

            Assert.Equal(templateId, fromUserFicha);
            Assert.Contains(fromTemplateFicha, member => member.AccountId == account.AccountId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task DeleteWithMembers_NullsReferences_DeletesRowAndComposition_UsersRemain()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", "job-on", ["job-on-view", "controlo-create"]);
            var active = await CreateAccountAsync(users, $"cn-a-{token}", templateId: templateId);
            var inactive = await CreateAccountAsync(users, $"cn-i-{token}", templateId: templateId, active: false);

            await templates.DeleteWithMembersAsync(templateId, expectedVersion: 1, CancellationToken.None);

            // Template row + composition gone (cascade).
            Assert.Null(await templates.GetByIdAsync(templateId, CancellationToken.None));
            Assert.Empty(await new TemplateModuleRepository(context)
                .GetByTemplateAsync(templateId, CancellationToken.None));

            // Both USER rows remain; references nulled; active state preserved.
            var activeAfter = (await users.GetByIdAsync(active.AccountId, CancellationToken.None))!;
            var inactiveAfter = (await users.GetByIdAsync(inactive.AccountId, CancellationToken.None))!;
            Assert.Null(activeAfter.TemplateId);
            Assert.True(activeAfter.IsActive);
            Assert.Null(inactiveAfter.TemplateId);
            Assert.False(inactiveAfter.IsActive);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task DeleteWithMembers_StaleVersion_RollsBack_EverythingSurvives()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", null, ["job-on-view"]);
            var account = await CreateAccountAsync(users, $"cn-{token}", templateId: templateId);

            // A concurrent writer bumps the Template version between the confirmation read
            // and the delete (the accepted race posture) — the delete must fail stale and
            // roll back: Template, composition and every user reference survive.
            var entity = await context.Templates.SingleAsync(candidate => candidate.TemplateId == templateId);
            entity.Version += 1;
            await context.SaveChangesAsync();

            var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                templates.DeleteWithMembersAsync(templateId, expectedVersion: 1, CancellationToken.None));

            Assert.Contains("reload and retry", conflict.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(await templates.GetByIdAsync(templateId, CancellationToken.None));
            Assert.Single(await new TemplateModuleRepository(context)
                .GetByTemplateAsync(templateId, CancellationToken.None));
            Assert.Equal(templateId, (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!.TemplateId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task DeleteWithMembers_CommittedMembershipBeforeNullOut_Included_RowGone()
    {
        // A membership committed between the confirmation read and the destructive call is
        // included in the null-out (the UPDATE runs inside the same transaction and sees
        // every committed reference); no reference survives, the row goes away.
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", null, ["job-on-view"]);
            var confirmed = (await templates.GetByIdAsync(templateId, CancellationToken.None))!;

            // The "concurrent" assign commits AFTER the confirmation read (version 1) but
            // BEFORE the delete call.
            var account = await CreateAccountAsync(users, $"cn-{token}", templateId: null);
            await users.SetTemplateAsync(account.AccountId, templateId, expectedVersion: 1, CancellationToken.None);

            await templates.DeleteWithMembersAsync(templateId, confirmed.Version, CancellationToken.None);

            Assert.Null(await templates.GetByIdAsync(templateId, CancellationToken.None));
            Assert.Null((await users.GetByIdAsync(account.AccountId, CancellationToken.None))!.TemplateId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task PlainDelete_IsBlockedOnReferencedTemplate_RESTRICT_WithMembersDeleteIsTheWorkingPath()
    {
        // The P1-T03 primitive fails at the database layer on a referenced Template (FK
        // RESTRICT, users_template_id_fkey): the P1-T06 atomic null-out workflow is the
        // working path and the proof the plain delete never half-deletes.
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", null, ["job-on-view"]);
            await CreateAccountAsync(users, $"cn-{token}", templateId: templateId);

            // Clear the change tracker: with no tracked dependent, EF cannot sever the
            // relationship client-side — the database's ON DELETE RESTRICT constraint
            // (users_template_id_fkey) is the decision maker.
            context.ChangeTracker.Clear();

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                templates.DeleteAsync(templateId, expectedVersion: 1, CancellationToken.None));

            // Nothing was half-deleted: the row survived and the FK failure rolled back.
            Assert.NotNull(await templates.GetByIdAsync(templateId, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task Update_ReplacesCompositionAtomically_BumpsVersion_StaleRejected()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", "job-on", ["job-on-view", "controlo-create"]);

            // Full edit: facts + landing + reordered composition.
            await templates.UpdatedAsync(
                new Template(templateId, $"T-{token}-v2", "controlo", Version: 1),
                TestModules(templateId, ["controlo-create", "job-on-create"]),
                CancellationToken.None);

            var loaded = (await templates.GetByIdAsync(templateId, CancellationToken.None))!;
            Assert.Equal(2, loaded.Version);
            Assert.Equal("controlo", loaded.LandingDestinationId);
            Assert.Equal(
                ["controlo-create", "job-on-create"],
                (await new TemplateModuleRepository(context).GetByTemplateAsync(templateId, CancellationToken.None))
                    .Select(module => module.ModuleId)
                    .ToArray());

            // Stale expected version → conflict, composition untouched.
            var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                templates.UpdatedAsync(
                    new Template(templateId, "stale", null, Version: 1),
                    TestModules(templateId, ["job-on-view", "controlo-create"]),
                    CancellationToken.None));
            Assert.Contains("reload and retry", conflict.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                ["controlo-create", "job-on-create"],
                (await new TemplateModuleRepository(context).GetByTemplateAsync(templateId, CancellationToken.None))
                    .Select(module => module.ModuleId)
                    .ToArray());
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    [SkippableFact]
    public async Task Composition_SchemaBackstops_DuplicateModuleId_And_DuplicateOrder_Rejected()
    {
        // Schema 002 backstops: PK (template_id, module_id) and unique (template_id,
        // presentation_order) reject a corrupt composition even if the application layer
        // failed to normalize — proving the database is the final boundary, not EF.
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var templates = new TemplateRepository(context);
            var templateId = await CreateTemplateAsync(templates, $"T-{token}", null, ["job-on-view"]);

            // Duplicate (template_id, module_id).
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
                "INSERT INTO template_modules (template_id, module_id, presentation_order) VALUES (@t, 'job-on-view', 2)",
                new NpgsqlParameter("t", templateId)));

            // Duplicate presentation_order.
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
                "INSERT INTO template_modules (template_id, module_id, presentation_order) VALUES (@t, 'controlo-create', 1)",
                new NpgsqlParameter("t", templateId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ------------------------------------------------- CORRECTIONS A + B (real service path)

    /// <summary>
    /// Architect correction A against the real persistence layer: the direct application
    /// service path — the exact path the minimal API
    /// <c>DELETE /administration/templates/{id}/users/{userId}</c> route builds
    /// (<c>TargetTemplateId: null</c>) — cannot remove a membership that belongs to another
    /// Template. The rule is enforced in the service, not in a Razor page.
    /// </summary>
    /// <remarks>
    /// Test: ServiceRemove_UserBelongsToAnotherTemplate_ValidationFailed_NoWrite.<br/>
    /// Purpose: prove the direct service/API remove path cannot clear a membership owned by a
    /// different Template, against the real schema.<br/>
    /// Master behavior being verified: removal through Template A is an operation on A's
    /// membership; a USER associated with Template B stays in B (ACCESS_MODEL §3/§14).<br/>
    /// Preconditions: Templates A and B persisted; USER U has <c>template_id = B</c>, version 1.<br/>
    /// Action: <c>TemplateAdministrationService.SetTemplateUserAsync(SetTemplateUserCommand(A, U,
    /// TargetTemplateId: null, 1))</c> — no Razor page involved.<br/>
    /// Assertions: <c>ValidationFailed</c>; reloading U from the DB: <c>template_id</c> still B,
    /// version still 1; B's member list contains U; A's does not.<br/>
    /// Required non-effects: no row/column write — version unchanged proves no
    /// <c>SetTemplateAsync</c> reached the DB.<br/>
    /// What this proves: the remove invariant is Application authority over the real schema.<br/>
    /// What this does NOT prove: HTTP status mapping of the route (host smoke tests).
    /// </remarks>
    [SkippableFact]
    public async Task ServiceRemove_UserBelongsToAnotherTemplate_ValidationFailed_NoWrite()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var service = new TemplateAdministrationService(
                templates,
                new TemplateModuleRepository(context),
                users,
                ModuleRegistry.Empty());

            var templateA = await CreateTemplateAsync(templates, $"A-{token}", null, ["job-on-view"]);
            var templateB = await CreateTemplateAsync(templates, $"B-{token}", null, ["controlo-create"]);
            var user = await CreateAccountAsync(users, $"cn-{token}", templateId: templateB);

            var result = await service.SetTemplateUserAsync(
                new TemplateAdministrationCommands.SetTemplateUserCommand(
                    templateA, user.AccountId, TargetTemplateId: null, UserExpectedVersion: user.Version),
                CancellationToken.None);

            Assert.IsType<TemplateAdministrationResult.ValidationFailed>(result);

            var stored = (await users.GetByIdAsync(user.AccountId, CancellationToken.None))!;
            Assert.Equal(templateB, stored.TemplateId);
            Assert.Equal(user.Version, stored.Version);

            var membersB = await users.ListByTemplateAsync(templateB, CancellationToken.None);
            var membersA = await users.ListByTemplateAsync(templateA, CancellationToken.None);
            Assert.Contains(membersB, member => member.AccountId == user.AccountId);
            Assert.DoesNotContain(membersA, member => member.AccountId == user.AccountId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <remarks>
    /// Test: ServiceUpdate_NonPositiveExpectedVersion_ValidationFailed_NoWrite.<br/>
    /// Purpose: prove update with <c>ExpectedVersion &lt;= 0</c> fails validation before any
    /// repository write, against the real schema.<br/>
    /// Master behavior being verified: accepted plan §17 — <c>ExpectedVersion &gt; 0</c>.<br/>
    /// Preconditions: Template persisted with version 1.<br/>
    /// Action: <c>UpdateAsync</c> with version 0 (−1 in a second invocation against a fresh
    /// Template).<br/>
    /// Assertions: <c>ValidationFailed</c>; reload: name/composition/version unchanged.<br/>
    /// Required non-effects: no <c>UpdatedAsync</c> — version stays 1, composition intact.<br/>
    /// What this proves: malformed carriers never reach the versioned write path.<br/>
    /// What this does NOT prove: validator unit rule (covered in unit tests).
    /// </remarks>
    [SkippableFact]
    public async Task ServiceUpdate_NonPositiveExpectedVersion_ValidationFailed_NoWrite()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var templates = new TemplateRepository(context);
            var service = new TemplateAdministrationService(
                templates,
                new TemplateModuleRepository(context),
                new UserRepository(context),
                ModuleRegistry.Empty());

            foreach (var malformedVersion in new[] { 0, -1 })
            {
                var templateId = await CreateTemplateAsync(templates, $"T-{token}-{malformedVersion}", null, ["job-on-view"]);

                var result = await service.UpdateAsync(
                    new TemplateAdministrationCommands.UpdateTemplateCommand(
                        templateId, "Renomeado", ["controlo-create"], "controlo", malformedVersion),
                    CancellationToken.None);

                Assert.IsType<TemplateAdministrationResult.ValidationFailed>(result);

                var stored = (await templates.GetByIdAsync(templateId, CancellationToken.None))!;
                Assert.Equal($"T-{token}-{malformedVersion}", stored.Name);
                Assert.Equal(1, stored.Version);
                Assert.Equal(["job-on-view"], (await new TemplateModuleRepository(context)
                    .GetByTemplateAsync(templateId, CancellationToken.None))
                    .Select(module => module.ModuleId)
                    .ToArray());
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <remarks>
    /// Test: ServiceDelete_NonPositiveExpectedVersion_ValidationFailed_NoDeleteOrNullOut.<br/>
    /// Purpose: prove delete with <c>ExpectedVersion &lt;= 0</c> fails validation before any
    /// destructive effect, against the real schema.<br/>
    /// Master behavior being verified: accepted plan §17 — delete requires <c>&gt; 0</c>.<br/>
    /// Preconditions: Template with a member USER; delete command with version 0 (−1 in a second
    /// iteration).<br/>
    /// Action: <c>DeleteAsync</c>.<br/>
    /// Assertions: <c>ValidationFailed</c>; Template row survives; USER reference survives.<br/>
    /// Required non-effects: no <c>DeleteWithMembersAsync</c>, no null-out.<br/>
    /// What this proves: malformed destructive carriers cannot delete or null anything.<br/>
    /// What this does NOT prove: atomic transaction semantics (covered by the delete tests).
    /// </remarks>
    [SkippableFact]
    public async Task ServiceDelete_NonPositiveExpectedVersion_ValidationFailed_NoDeleteOrNullOut()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var service = new TemplateAdministrationService(
                templates,
                new TemplateModuleRepository(context),
                users,
                ModuleRegistry.Empty());

            foreach (var malformedVersion in new[] { 0, -1 })
            {
                var templateId = await CreateTemplateAsync(templates, $"T-{token}-{malformedVersion}", null, ["job-on-view"]);
                var account = await CreateAccountAsync(users, $"cn-{token}-{malformedVersion}", templateId: templateId);

                var result = await service.DeleteAsync(
                    new TemplateAdministrationCommands.DeleteTemplateCommand(templateId, malformedVersion),
                    CancellationToken.None);

                Assert.IsType<TemplateAdministrationResult.ValidationFailed>(result);
                Assert.NotNull(await templates.GetByIdAsync(templateId, CancellationToken.None));
                Assert.Equal(templateId, (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!.TemplateId);
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <remarks>
    /// Test: ServiceMembership_NonPositiveUserExpectedVersion_ValidationFailed_NoWrite.<br/>
    /// Purpose: prove membership with <c>UserExpectedVersion &lt;= 0</c> fails validation before
    /// any membership write, against the real schema.<br/>
    /// Master behavior being verified: accepted plan §17 — association operations require a valid
    /// user version <c>&gt; 0</c>.<br/>
    /// Preconditions: USER U associated with Template A, version 1; remove command (the shape that
    /// would null the relation) with version 0 (−1 in a second iteration).<br/>
    /// Action: <c>SetTemplateUserAsync</c>.<br/>
    /// Assertions: <c>ValidationFailed</c>; U's <c>template_id</c> remains A; version unchanged.<br/>
    /// Required non-effects: no <c>SetTemplateAsync</c>, no version bump.<br/>
    /// What this proves: malformed membership carriers cannot null or reassign anything.<br/>
    /// What this does NOT prove: validator unit rule (covered in unit tests).
    /// </remarks>
    [SkippableFact]
    public async Task ServiceMembership_NonPositiveUserExpectedVersion_ValidationFailed_NoWrite()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = UniqueToken();
        try
        {
            var users = new UserRepository(context);
            var templates = new TemplateRepository(context);
            var service = new TemplateAdministrationService(
                templates,
                new TemplateModuleRepository(context),
                users,
                ModuleRegistry.Empty());

            foreach (var malformedVersion in new[] { 0, -1 })
            {
                var templateId = await CreateTemplateAsync(templates, $"T-{token}-{malformedVersion}", null, ["job-on-view"]);
                var account = await CreateAccountAsync(users, $"cn-{token}-{malformedVersion}", templateId: templateId);

                var result = await service.SetTemplateUserAsync(
                    new TemplateAdministrationCommands.SetTemplateUserCommand(
                        templateId, account.AccountId, TargetTemplateId: null, malformedVersion),
                    CancellationToken.None);

                Assert.IsType<TemplateAdministrationResult.ValidationFailed>(result);

                var stored = (await users.GetByIdAsync(account.AccountId, CancellationToken.None))!;
                Assert.Equal(templateId, stored.TemplateId);
                Assert.Equal(account.Version, stored.Version);
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string UniqueToken() =>
        $"{TestTokenPrefix}{Guid.NewGuid():N}";

    private static async Task<Guid> CreateTemplateAsync(
        TemplateRepository templates,
        string name,
        string? landing,
        string[] moduleIds)
    {
        var template = new Template(Guid.Empty, name, landing, Version: 1);
        var modules = moduleIds
            .Select((moduleId, index) => new TemplateModule(Guid.Empty, moduleId, index + 1))
            .ToArray();
        return await templates.CreatedAsync(template, modules, CancellationToken.None);
    }

    private static IReadOnlyList<TemplateModule> TestModules(Guid templateId, string[] moduleIds) =>
        moduleIds
            .Select((moduleId, index) => new TemplateModule(templateId, moduleId, index + 1))
            .ToArray();

    private static async Task<UserAccount> CreateAccountAsync(
        UserRepository users,
        string companyNumber,
        string? name = null,
        Guid? templateId = null,
        bool active = true)
    {
        var account = new UserAccount(
            Guid.NewGuid(),
            CompanyNumber: companyNumber,
            DisplayName: name ?? "João Silva",
            Email: $"user-{companyNumber}@dmo.test",
            RoleLabel: "Reparador",
            IsActive: active,
            TemplateId: templateId,
            Version: 1);

        await users.CreatedAsync(account, $"subject-{Guid.NewGuid():N}", CancellationToken.None);
        return account;
    }

    private static Task CleanupAsync(DmoDbContext context, string token) =>
        context.Database.ExecuteSqlRawAsync(
            "DELETE FROM users WHERE company_number LIKE '%' || @t || '%' "
            + "OR email LIKE '%' || @t || '%'; "
            + "DELETE FROM templates WHERE name LIKE '%' || @t || '%'",
            new NpgsqlParameter("t", token));
}