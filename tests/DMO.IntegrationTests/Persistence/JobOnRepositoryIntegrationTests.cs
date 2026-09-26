using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using DMO.IntegrationTests.Frontend.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using DomainJobOn = DMO.Domain.JobOn.JobOn;
using DomainJobOnId = DMO.Domain.JobOn.JobOnId;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T04 env-gated integration test — the Job On occurrence over the disposable PostgreSQL
/// database: the production uniqueness rule, the context set of §7, the frozen snapshot, the edit
/// transaction and the context-scoped guarantees of §11.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §6 (occurrence rules), §7 (context/snapshot rules), §8.1 (ordering),
/// §11 (transactions), §15 (concurrency) and §20.2/§20.3 (rows JOB5, JOB7–JOB10, JOB15, JOB24–JOB27,
/// CTX1–CTX8, CTX13, CTX15, CTX17). Every assertion is scoped to the rows this test creates.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class JobOnRepositoryIntegrationTests
{
    private static readonly string[] ContextTables = ["cm_contexts", "mf_contexts", "bq_contexts"];

    /// <summary>
    /// JOB5 (AC-16): a second <c>job_ons</c> row with the same (<c>reference</c>,
    /// <c>production_number</c>) is rejected by <c>job_ons_reference_production_number_key</c> — the
    /// machine is not part of the tuple.
    /// </summary>
    [SkippableFact]
    public async Task JOB5_SecondJobOnWithTheSameReferenceAndProductionNumberIsRejected()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            await InsertJobOnAsync(context, Guid.NewGuid(), reference, $"pn-{token}-01", "B1");

            // The same pair with a DIFFERENT machine and a different date is still the same pair.
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertJobOnAsync(context, Guid.NewGuid(), reference, $"pn-{token}-01", "C3", new DateOnly(2030, 1, 1)));

            Assert.Equal("23505", duplicate.SqlState);
            Assert.Equal(
                JobOnEntityConfiguration.ReferenceProductionNumberUniqueConstraintName,
                duplicate.ConstraintName);

            Assert.Equal(
                1,
                await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));

            // Another production number for the same reference is a different occurrence.
            await InsertJobOnAsync(context, Guid.NewGuid(), reference, $"pn-{token}-02", "B1");

            Assert.Equal(
                2,
                await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB7 (AC-27): a create with CM and MF selected and BQ empty writes exactly the <c>job_ons</c>
    /// row, one <c>cm_contexts</c> row and one <c>mf_contexts</c> row — and no <c>bq_contexts</c> row.
    /// </summary>
    [SkippableFact]
    public async Task JOB7_CreateWithCmAndMfSelectedWritesExactlyThoseContextRows()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");

            var created = await CreateJobOnAsync(
                context, reference, $"pn-{token}-01", "B1", cmTool, mfTool);

            Assert.Equal(1, await CountAsync(
                context, "job_ons WHERE jobon_id = @p", new NpgsqlParameter("p", created.JobOnId)));
            Assert.Equal(1, await CountContextAsync(context, "cm_contexts", created.JobOnId));
            Assert.Equal(1, await CountContextAsync(context, "mf_contexts", created.JobOnId));

            // The BQ slot was not selected: no symmetry row, no placeholder context.
            Assert.Equal(0, await CountContextAsync(context, "bq_contexts", created.JobOnId));

            // The frozen triples are the tools selected for each slot.
            Assert.Equal(
                $"{cmTool}",
                await ScalarAsync(
                    context,
                    "SELECT tool_id::text FROM cm_contexts WHERE jobon_id = @p",
                    new NpgsqlParameter("p", created.JobOnId)));
            Assert.Equal(
                $"{mfTool}",
                await ScalarAsync(
                    context,
                    "SELECT tool_id::text FROM mf_contexts WHERE jobon_id = @p",
                    new NpgsqlParameter("p", created.JobOnId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB8 (AC-27): a create with no Tool selected creates zero context rows of all three kinds.
    /// </summary>
    [SkippableFact]
    public async Task JOB8_CreateWithNoToolSelectedCreatesZeroContextRows()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");

            Assert.Equal(1, await CountAsync(
                context, "job_ons WHERE jobon_id = @p", new NpgsqlParameter("p", created.JobOnId)));

            foreach (var table in ContextTables)
            {
                Assert.Equal(0, await CountContextAsync(context, table, created.JobOnId));
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB9 (AC-19, AC-66, AC-67): a forced failure while inserting a context rolls the whole create
    /// back — no <c>job_ons</c> row and no context rows remain.
    /// </summary>
    [SkippableFact]
    public async Task JOB9_AContextInsertFailureLeavesNoJobOnAndNoContextRow()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");

            // ---- Phase A: the write itself is forced to fail (Job On + context are one unit). ----
            var jobOnId = DomainJobOnId.New();
            var contextId = Guid.NewGuid();
            var jobOn = new DomainJobOn(
                jobOnId,
                reference,
                $"pn-{token}-a",
                MachineCode.From("B1"),
                null,
                null,
                Version: 1,
                Contexts: [CmContext(contextId, jobOnId, cmTool, reference, "01")]);

            var interceptor = new FailOnNthSaveInterceptor(failOn: 1);
            await using var marked = CreateMarkedContext(interceptor);
            var markedRepository = new JobOnRepository(marked);

            await Assert.ThrowsAsync<ForcedSaveFailureException>(
                () => markedRepository.CreatedAsync(jobOn, jobOn.Contexts, CancellationToken.None));

            Assert.Equal(1, interceptor.Calls);
            Assert.Contains("JobOnEntity:Added", interceptor.FailedSaveEntries);
            Assert.Contains("CmContextEntity:Added", interceptor.FailedSaveEntries);

            Assert.Equal(0, await CountAsync(
                context, "job_ons WHERE jobon_id = @p", new NpgsqlParameter("p", jobOnId.Value)));
            foreach (var table in ContextTables)
            {
                Assert.Equal(0, await CountContextAsync(context, table, jobOnId.Value));
            }

            // ---- Phase B: a real context-insert failure (taken context identity) rolls back too. ----
            var parent = await CreateJobOnAsync(context, reference, $"pn-{token}-parent", "B1", cmTool);
            var takenContextId = Guid.Parse(await ScalarAsync(
                context,
                "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", parent.JobOnId)));

            var failingJobOnId = DomainJobOnId.New();
            var failingContext = CmContext(takenContextId, failingJobOnId, cmTool, reference, "01");
            var failingJobOn = new DomainJobOn(
                failingJobOnId,
                reference,
                $"pn-{token}-b",
                MachineCode.From("B1"),
                null,
                null,
                Version: 1,
                Contexts: [failingContext]);

            await using var fresh = PersistenceTestDatabase.CreateContext();

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                new JobOnRepository(fresh).CreatedAsync(failingJobOn, [failingContext], CancellationToken.None));

            var postgres = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal("23505", postgres.SqlState);
            Assert.Equal("PK_cm_contexts", postgres.ConstraintName);

            Assert.Equal(0, await CountAsync(
                context, "job_ons WHERE jobon_id = @p", new NpgsqlParameter("p", failingJobOnId.Value)));
            foreach (var table in ContextTables)
            {
                Assert.Equal(0, await CountContextAsync(context, table, failingJobOnId.Value));
            }

            // The pre-existing context row (and its Job On) is untouched.
            Assert.Equal(1, await CountContextAsync(context, "cm_contexts", parent.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB10 (AC-19): after commit the <c>jobon_id</c> is visible to an independent context and
    /// matches the value the create returned.
    /// </summary>
    [SkippableFact]
    public async Task JOB10_TheCommittedJobOnIdIsVisibleToAnIndependentContext()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var productionNumber = $"pn-{token}-01";

        try
        {
            var created = await CreateJobOnAsync(context, reference, productionNumber, "B1");
            Assert.Equal(1, created.Version);

            // An independent context (its own connection) observes the committed identity.
            await using var independent = PersistenceTestDatabase.CreateContext();

            var row = await ScalarAsync(
                independent,
                "SELECT jobon_id::text || '|' || reference || '|' || production_number || '|' || machine || " +
                "'|' || version FROM job_ons WHERE jobon_id = @p",
                new NpgsqlParameter("p", created.JobOnId));

            Assert.Equal($"{created.JobOnId}|{reference}|{productionNumber}|B1|1", row);

            var reloaded = await new JobOnRepository(independent)
                .GetByIdAsync(created.JobOnId, CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.Equal(created.JobOnId, reloaded!.JobOnId.Value);
            Assert.Equal(productionNumber, reloaded.ProductionNumber);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB15 (AC-25): the productions result is ordered <c>production_number</c>,
    /// <c>production_date</c> NULLS LAST, <c>jobon_id</c>, deterministically across repeated calls.
    /// </summary>
    [SkippableFact]
    public async Task JOB15_TheProductionsResultIsDeterministicallyOrdered()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        string[] productionNumbers =
            [$"pn-{token}-01", $"pn-{token}-02", $"pn-{token}-03", $"pn-{token}-04"];

        try
        {
            // Inserted out of order, with NULL dates interleaved between dated rows.
            await InsertJobOnAsync(context, Guid.NewGuid(), reference, productionNumbers[2], "B1");
            await InsertJobOnAsync(context, Guid.NewGuid(), reference, productionNumbers[0], "B1", new DateOnly(2030, 1, 1));
            await InsertJobOnAsync(context, Guid.NewGuid(), reference, productionNumbers[3], "C1");
            await InsertJobOnAsync(context, Guid.NewGuid(), reference, productionNumbers[1], "B2", new DateOnly(2029, 6, 1));

            var repository = new JobOnRepository(context);

            var first = await repository.ListByReferenceAsync(reference, CancellationToken.None);
            var second = await repository.ListByReferenceAsync(reference, CancellationToken.None);

            Assert.Equal(productionNumbers, first.Select(item => item.ProductionNumber).ToArray());
            Assert.Equal(first, second);

            // The dates are returned exactly as persisted, NULL included (never substituted).
            Assert.Equal(
                new DateOnly?[] { new(2030, 1, 1), new(2029, 6, 1), null, null },
                first.Select(item => item.ProductionDate).ToArray());

            // The same order comes out of the contracted ORDER BY evaluated by the database itself.
            var raw = await QueryStringsAsync(
                context,
                "SELECT production_number FROM job_ons WHERE reference = @p " +
                "ORDER BY production_number, production_date, jobon_id",
                new NpgsqlParameter("p", reference));

            Assert.Equal(productionNumbers, raw.ToArray());
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB24 (AC-68, AC-73): an edit changes reference, production number, machine and production
    /// date, persists them and increments <c>version</c> exactly once.
    /// </summary>
    [SkippableFact]
    public async Task JOB24_AnEditPersistsTheFourFactsAndIncrementsTheVersionExactlyOnce()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var editedReference = $"ref-{token}-edited";
        var editedProductionNumber = $"pn-{token}-edited";
        var editedDate = new DateOnly(2030, 3, 15);

        try
        {
            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");

            var updated = Assert.IsType<JobOnResult.Updated>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId,
                    ExpectedVersion: 1,
                    editedReference,
                    editedProductionNumber,
                    "C2",
                    editedDate,
                    Associations: [],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None));

            Assert.Equal(created.JobOnId, updated.JobOnId);
            Assert.Equal(2, updated.Version); // exactly one increment for one committed mutation

            var row = await ScalarAsync(
                context,
                "SELECT reference || '|' || production_number || '|' || machine || '|' || " +
                "production_date::text || '|' || version FROM job_ons WHERE jobon_id = @p",
                new NpgsqlParameter("p", created.JobOnId));

            Assert.Equal(
                $"{editedReference}|{editedProductionNumber}|C2|" +
                $"{editedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|2",
                row);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB25 (AC-72): an edit with a stale <c>ExpectedVersion</c> raises the domain conflict, returns
    /// <c>stale-version</c> and leaves every column unchanged.
    /// </summary>
    [SkippableFact]
    public async Task JOB25_AStaleExpectedVersionRefusesTheEditAndWritesNothing()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");

            // A legitimate edit takes the row to version 2.
            Assert.IsType<JobOnResult.Updated>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId, 1, reference, $"pn-{token}-01", "B2", null, [], false),
                CancellationToken.None));

            var current = await RowFingerprintAsync(context, created.JobOnId);

            // Service level: the stale edit is refused with the contracted reason.
            var refused = Assert.IsType<JobOnResult.Refused>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId, 1, $"{reference}-stale", $"pn-{token}-stale", "C3", null, [], false),
                CancellationToken.None));

            Assert.Equal(JobOnRefusalReason.StaleVersion, refused.Reason);
            Assert.False(string.IsNullOrWhiteSpace(refused.Message));
            Assert.Equal(current, await RowFingerprintAsync(context, created.JobOnId));

            // Repository level: the domain conflict itself, still writing nothing.
            var repository = new JobOnRepository(context);
            var persisted = await repository.GetByIdAsync(created.JobOnId, CancellationToken.None);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdatedAsync(
                persisted! with { Version = 1, Reference = $"{reference}-stale" },
                [],
                CancellationToken.None));

            Assert.Equal(current, await RowFingerprintAsync(context, created.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// JOB26 (AC-74): the update path writes only the four facts plus the context changes,
    /// <c>version</c> and <c>updated_at</c>; no status column is written anywhere.
    /// </summary>
    [SkippableFact]
    public async Task JOB26_TheUpdatePathWritesOnlyTheContractedColumnsAndNoStatus()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var repositorySource = await File.ReadAllTextAsync(
            P2T04SourcePath("Persistence/ToolJobOn/JobOnRepository.cs"));
        var updateBody = MethodBody(repositorySource, "public async Task<DomainJobOn> UpdatedAsync(");

        var assignedMembers = Regex.Matches(updateBody, @"entity\.(?<name>[A-Za-z]+)\s*(?:\+=|=)[^=]")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "Machine", "ProductionDate", "ProductionNumber", "Reference", "UpdatedAt", "Version" },
            assignedMembers);

        AssertNoLifecycleVocabulary(WithoutComments(updateBody));

        // There is no status column to write: job_ons carries exactly the nine contracted columns.
        var columns = Sorted(await QueryStringsAsync(
            context,
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'job_ons'"));

        Assert.Equal(
            Sorted(new[]
            {
                "copied_from_jobon_id", "created_at", "jobon_id", "machine", "production_date",
                "production_number", "reference", "updated_at", "version",
            }),
            columns);
    }

    /// <summary>
    /// JOB27 (AC-68): an edit setting a Tool whose type does not match the slot fails with
    /// <c>TOOL_TYPE_MISMATCH</c> and writes nothing.
    /// </summary>
    [SkippableFact]
    public async Task JOB27_SetWithAMismatchedToolTypeFailsAndWritesNothing()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");

            var created = await CreateJobOnAsync(
                context, reference, $"pn-{token}-01", "B1", cmTool);
            var fingerprint = await RowFingerprintAsync(context, created.JobOnId);
            var contextFingerprint = await ContextFingerprintAsync(context, created.JobOnId);

            // Service level: the contracted validation code, and nothing written.
            var refused = Assert.IsType<JobOnResult.ValidationFailed>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId,
                    ExpectedVersion: 1,
                    reference,
                    $"pn-{token}-01",
                    "B2",
                    null,
                    Associations: [new ToolAssociationChange(ToolContextType.Cm, ToolAssociationAction.Set, mfTool)],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None));

            Assert.Contains(JobOnValidationErrors.ToolTypeMismatch, refused.Errors);
            Assert.Equal(fingerprint, await RowFingerprintAsync(context, created.JobOnId));
            Assert.Equal(contextFingerprint, await ContextFingerprintAsync(context, created.JobOnId));

            // Repository level: the typed persistence failure, still writing nothing (no version bump).
            var repository = new JobOnRepository(context);
            var persisted = await repository.GetByIdAsync(created.JobOnId, CancellationToken.None);

            var failure = await Assert.ThrowsAsync<JobOnPersistenceException>(() => repository.UpdatedAsync(
                persisted!,
                [new ToolContextChange(ToolContextType.Cm, ToolAssociationAction.Set, mfTool)],
                CancellationToken.None));

            Assert.Equal(JobOnPersistenceFailureReason.ToolTypeMismatch, failure.Reason);
            Assert.Equal(fingerprint, await RowFingerprintAsync(context, created.JobOnId));
            Assert.Equal(contextFingerprint, await ContextFingerprintAsync(context, created.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX1 (AC-28, AC-29): selecting the same Tool twice for one slot leaves one row with the
    /// identical context id (idempotent reuse).
    /// </summary>
    [SkippableFact]
    public async Task CTX1_ReselectingTheSameToolReusesTheSameContextRow()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool);

            var contextId = await ScalarAsync(
                context,
                "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", created.JobOnId));

            var reused = Assert.IsType<JobOnResult.Updated>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId,
                    ExpectedVersion: 1,
                    reference,
                    $"pn-{token}-01",
                    "B1",
                    null,
                    Associations: [new ToolAssociationChange(ToolContextType.Cm, ToolAssociationAction.Set, cmTool)],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None));

            // No second row, no id change, and only the enclosing transaction's single bump.
            Assert.Equal(2, reused.Version);
            Assert.Equal(1, await CountContextAsync(context, "cm_contexts", created.JobOnId));
            Assert.Equal(0, await CountContextAsync(context, "mf_contexts", created.JobOnId));
            Assert.Equal(0, await CountContextAsync(context, "bq_contexts", created.JobOnId));
            Assert.Equal(
                contextId,
                await ScalarAsync(
                    context,
                    "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
                    new NpgsqlParameter("p", created.JobOnId)));
            Assert.Equal(
                $"{reference}|01",
                await ScalarAsync(
                    context,
                    "SELECT tool_reference || '|' || tool_lot FROM cm_contexts WHERE jobon_id = @p",
                    new NpgsqlParameter("p", created.JobOnId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX2 (AC-29): a direct second insert of the same context type for one Job On is rejected by
    /// <c>cm_contexts_jobon_key</c> (and the MF/BQ equivalents) with <c>23505</c>.
    /// </summary>
    [SkippableFact]
    public async Task CTX2_ASecondContextOfTheSameTypeForOneJobOnIsRejected()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");

            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);

            var cases = new (string Table, Guid ToolId, string ToolType, string ConstraintName, string Lot)[]
            {
                ("cm_contexts", cmTool, "CM", CmContextEntityConfiguration.JobOnUniqueConstraintName, "01"),
                ("mf_contexts", mfTool, "MF", MfContextEntityConfiguration.JobOnUniqueConstraintName, "02"),
                ("bq_contexts", bqTool, "BQ", BqContextEntityConfiguration.JobOnUniqueConstraintName, "03"),
            };

            foreach (var testCase in cases)
            {
                var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertContextAsync(
                    context,
                    testCase.Table,
                    Guid.NewGuid(),
                    created.JobOnId,
                    testCase.ToolId,
                    testCase.ToolType,
                    reference,
                    testCase.Lot));

                Assert.Equal("23505", duplicate.SqlState);
                Assert.Equal(testCase.ConstraintName, duplicate.ConstraintName);
                Assert.Equal(1, await CountContextAsync(context, testCase.Table, created.JobOnId));
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX3 (AC-34): a context row whose frozen <c>tool_type</c> disagrees with its context type is
    /// rejected by the per-context type CHECK (<c>23514</c>) — for all three context tables.
    /// </summary>
    [SkippableFact]
    public async Task CTX3_AMismatchedFrozenToolTypeIsRejectedByTheContextCheck()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");
            var jobOn = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");

            var cases = new (string Table, Guid ToolId, string ToolType, string ConstraintName)[]
            {
                ("cm_contexts", cmTool, "MF", "cm_contexts_tool_type_check"),
                ("mf_contexts", mfTool, "CM", "mf_contexts_tool_type_check"),
                ("bq_contexts", bqTool, "CM", "bq_contexts_tool_type_check"),
            };

            foreach (var testCase in cases)
            {
                var rejected = await Assert.ThrowsAsync<PostgresException>(() => InsertContextAsync(
                    context,
                    testCase.Table,
                    Guid.NewGuid(),
                    jobOn.JobOnId,
                    testCase.ToolId,
                    testCase.ToolType,
                    reference,
                    "01"));

                Assert.Equal("23514", rejected.SqlState);
                Assert.Equal(testCase.ConstraintName, rejected.ConstraintName);
            }

            // No context row of any kind was created by the rejected inserts.
            foreach (var table in ContextTables)
            {
                Assert.Equal(0, await CountContextAsync(context, table, jobOn.JobOnId));
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX4 (AC-30): a context row cannot be inserted with a null <c>jobon_id</c>/<c>tool_id</c>
    /// (<c>23502</c>) or with an unknown id (<c>23503</c>, the RESTRICT foreign key).
    /// </summary>
    [SkippableFact]
    public async Task CTX4_AContextCannotBeInsertedWithoutAValidJobOnAndTool()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var jobOn = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");

            // NOT NULL: jobon_id.
            var nullJobOn = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO cm_contexts (cm_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
                    $"VALUES ('{Guid.NewGuid()}', NULL, '{cmTool}', 'CM', '{reference}', '01')"));
            Assert.Equal("23502", nullJobOn.SqlState);
            Assert.Equal("jobon_id", nullJobOn.ColumnName);

            // NOT NULL: tool_id.
            var nullTool = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO cm_contexts (cm_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
                    $"VALUES ('{Guid.NewGuid()}', '{jobOn.JobOnId}', NULL, 'CM', '{reference}', '01')"));
            Assert.Equal("23502", nullTool.SqlState);
            Assert.Equal("tool_id", nullTool.ColumnName);

            // Unknown ids: the RESTRICT foreign keys refuse the row.
            var unknownJobOn = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO cm_contexts (cm_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
                    $"VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{cmTool}', 'CM', '{reference}', '01')"));
            Assert.Equal("23503", unknownJobOn.SqlState);
            Assert.Equal(CmContextEntityConfiguration.JobOnForeignKeyConstraintName, unknownJobOn.ConstraintName);

            var unknownTool = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO cm_contexts (cm_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
                    $"VALUES ('{Guid.NewGuid()}', '{jobOn.JobOnId}', '{Guid.NewGuid()}', 'CM', '{reference}', '01')"));
            Assert.Equal("23503", unknownTool.SqlState);
            Assert.Equal(CmContextEntityConfiguration.ToolForeignKeyConstraintName, unknownTool.ConstraintName);

            // The same holds for the MF and BQ context tables.
            var unknownMfJobOn = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO mf_contexts (mf_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
                    $"VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{cmTool}', 'MF', '{reference}', '01')"));
            Assert.Equal("23503", unknownMfJobOn.SqlState);
            Assert.Equal(MfContextEntityConfiguration.JobOnForeignKeyConstraintName, unknownMfJobOn.ConstraintName);

            var unknownBqTool = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO bq_contexts (bq_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
                    $"VALUES ('{Guid.NewGuid()}', '{jobOn.JobOnId}', '{Guid.NewGuid()}', 'BQ', '{reference}', '01')"));
            Assert.Equal("23503", unknownBqTool.SqlState);
            Assert.Equal(BqContextEntityConfiguration.ToolForeignKeyConstraintName, unknownBqTool.ConstraintName);

            foreach (var table in ContextTables)
            {
                Assert.Equal(0, await CountContextAsync(context, table, jobOn.JobOnId));
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX5 (AC-31, AC-32): after a direct update of the canonical Tool's <c>reference</c>/<c>lot</c>,
    /// every existing context row still returns its ORIGINAL frozen triple.
    /// </summary>
    [SkippableFact]
    public async Task CTX5_ALiveToolMetadataChangeNeverRewritesAFrozenTriple()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");

            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);

            // The live Tool metadata changes directly in the database, for every selected Tool.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE tools SET reference = @r, lot = @l WHERE tool_id = ANY (@ids)",
                new NpgsqlParameter("r", $"{reference}-live"),
                new NpgsqlParameter("l", $"lot-live-{token}"),
                new NpgsqlParameter("ids", new[] { cmTool, mfTool, bqTool }));

            // Every context row still returns its ORIGINAL frozen triple.
            var frozen = await QueryStringsAsync(
                context,
                "SELECT tool_type || '|' || tool_reference || '|' || tool_lot FROM cm_contexts WHERE jobon_id = @p " +
                "UNION ALL SELECT tool_type || '|' || tool_reference || '|' || tool_lot FROM mf_contexts " +
                "WHERE jobon_id = @p UNION ALL SELECT tool_type || '|' || tool_reference || '|' || tool_lot " +
                "FROM bq_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", created.JobOnId));

            Assert.Equal(
                Sorted([$"CM|{reference}|01", $"MF|{reference}|02", $"BQ|{reference}|03"]),
                Sorted(frozen));

            // The read model keeps the frozen triple and the live projection strictly separate.
            var ficha = await FichaAsync(context, created.JobOnId);

            Assert.Equal(3, ficha.Contexts.Count);
            foreach (var toolContext in ficha.Contexts)
            {
                var expectedLot = toolContext.ContextType switch
                {
                    ToolContextType.Cm => "01",
                    ToolContextType.Mf => "02",
                    _ => "03",
                };

                Assert.Equal(reference, toolContext.ToolReference);
                Assert.Equal(expectedLot, toolContext.ToolLot);

                // …while the live Tool projection shows the changed canonical values.
                Assert.Equal($"{reference}-live", toolContext.Tool.Reference);
                Assert.Equal($"lot-live-{token}", toolContext.Tool.Lot);
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX6 (AC-32): no code path updates a context's frozen columns except the explicit
    /// re-selection step of the edit transaction, and no trigger/worker/event propagates a Tool
    /// change.
    /// </summary>
    [SkippableFact]
    public async Task CTX6_NoCodePathRefreshesAFrozenTripleOutsideTheExplicitReselection()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var repositorySource = await File.ReadAllTextAsync(P2T04SourcePath("Persistence/ToolJobOn/JobOnRepository.cs"));

        var applySet = repositorySource.IndexOf("private async Task ApplySetAsync(", StringComparison.Ordinal);
        var applyRemove = repositorySource.IndexOf("private async Task ApplyRemoveAsync(", StringComparison.Ordinal);
        Assert.True(applySet > 0 && applyRemove > applySet, "The edit steps were not found in the repository.");

        // The in-place refresh of a context's frozen columns exists ONLY inside the explicit
        // re-selection step (the Set branch of the edit transaction).
        foreach (var member in new[]
                 {
                     "cm.ToolType", "cm.ToolReference", "cm.ToolLot",
                     "mf.ToolType", "mf.ToolReference", "mf.ToolLot",
                     "bq.ToolType", "bq.ToolReference", "bq.ToolLot",
                 })
        {
            var assignment = $"{member} =";
            var index = repositorySource.IndexOf(assignment, StringComparison.Ordinal);

            Assert.True(index > applySet && index < applyRemove,
                $"{member} is refreshed outside the explicit human re-selection step.");
            Assert.Equal(index, repositorySource.LastIndexOf(assignment, StringComparison.Ordinal));
        }

        // No context table carries an update path of its own, and no worker/event exists.
        var p2t04Persistence = Sorted(new[]
        {
            "Persistence/ToolJobOn/JobOnRepository.cs", "Persistence/ToolJobOn/ToolRepository.cs",
            "Persistence/ToolJobOn/JobOnLineageDependencyProbe.cs",
        }.Select(P2T04SourcePath).ToList());

        foreach (var path in p2t04Persistence)
        {
            var code = WithoutComments(await File.ReadAllTextAsync(path));

            Assert.DoesNotContain("ExecuteUpdate", code, StringComparison.Ordinal);
            Assert.DoesNotContain("BackgroundService", code, StringComparison.Ordinal);
            Assert.DoesNotContain("IHostedService", code, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Timers", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Channel<", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Task.Run", code, StringComparison.Ordinal);
        }

        // The database declares no trigger on any of the six tables.
        Assert.Equal(
            0,
            await CountAsync(
                context,
                "pg_trigger WHERE NOT tgisinternal AND tgrelid IN " +
                "('tools'::regclass,'tool_machines'::regclass,'job_ons'::regclass," +
                "'cm_contexts'::regclass,'mf_contexts'::regclass,'bq_contexts'::regclass)"));
    }

    /// <summary>
    /// CTX7 (AC-35): re-selecting a different Tool for a slot updates the same context row (identical
    /// <c>cm_id</c>) and refreshes <c>tool_id</c> plus the frozen triple.
    /// </summary>
    [SkippableFact]
    public async Task CTX7_ReselectingADifferentToolUpdatesTheSameContextRowInPlace()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var firstTool = await CreateToolAsync(context, "CM", reference, "01");
            var secondTool = await CreateToolAsync(context, "CM", reference, "02");
            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", firstTool);

            var contextId = await ScalarAsync(
                context,
                "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", created.JobOnId));

            var updated = Assert.IsType<JobOnResult.Updated>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId,
                    ExpectedVersion: 1,
                    reference,
                    $"pn-{token}-01",
                    "B1",
                    null,
                    Associations: [new ToolAssociationChange(ToolContextType.Cm, ToolAssociationAction.Set, secondTool)],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None));

            Assert.Equal(2, updated.Version);
            Assert.Equal(1, await CountContextAsync(context, "cm_contexts", created.JobOnId));

            var row = await ScalarAsync(
                context,
                "SELECT cm_id::text || '|' || tool_id::text || '|' || tool_type || '|' || tool_reference || " +
                "'|' || tool_lot FROM cm_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", created.JobOnId));

            Assert.Equal($"{contextId}|{secondTool}|CM|{reference}|02", row);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX8 (AC-36): <c>Remove</c> deletes exactly that context row in the same transaction as the
    /// fact change, and the other contexts are untouched.
    /// </summary>
    [SkippableFact]
    public async Task CTX8_RemoveDeletesOnlyThatContextRowInTheSameTransaction()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var newDate = new DateOnly(2030, 5, 20);

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");

            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);
            var mfFingerprint = await ContextRowFingerprintAsync(context, "mf_contexts", created.JobOnId);
            var bqFingerprint = await ContextRowFingerprintAsync(context, "bq_contexts", created.JobOnId);

            var updated = Assert.IsType<JobOnResult.Updated>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId,
                    ExpectedVersion: 1,
                    reference,
                    $"pn-{token}-01",
                    "C3",
                    newDate,
                    Associations: [new ToolAssociationChange(ToolContextType.Cm, ToolAssociationAction.Remove, null)],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None));

            Assert.Equal(2, updated.Version);

            // Exactly the CM context row is gone; the other two are byte-identical.
            Assert.Equal(0, await CountContextAsync(context, "cm_contexts", created.JobOnId));
            Assert.Equal(mfFingerprint, await ContextRowFingerprintAsync(context, "mf_contexts", created.JobOnId));
            Assert.Equal(bqFingerprint, await ContextRowFingerprintAsync(context, "bq_contexts", created.JobOnId));

            // The fact change rode the same transaction.
            Assert.Equal(
                $"C3|{newDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|2",
                await ScalarAsync(
                    context,
                    "SELECT machine || '|' || production_date::text || '|' || version FROM job_ons WHERE jobon_id = @p",
                    new NpgsqlParameter("p", created.JobOnId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX15 (AC-37): removing a context referenced by a dependent row fails closed (the dependent
    /// table's <c>ON DELETE RESTRICT</c> foreign key) and nothing is deleted.
    /// </summary>
    [SkippableFact]
    public async Task CTX15_ARemovalReferencedByADependentRowFailsClosedAndDeletesNothing()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var guardTable = $"p2t04_ctx15_guard_{token}";
        var foreignKeyName = $"fk_{guardTable}_cm_contexts";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool);

            var contextId = await ScalarAsync(
                context,
                "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", created.JobOnId));

            // EF1002 is not applicable: the interpolated table/constraint identifiers below are
            // random test-generated tokens, never user input, and they must be identifiers.
#pragma warning disable EF1002
            // A test-owned scratch table (never a seventh product table) whose row depends on the
            // context and declares the contracted RESTRICT behavior.
            await context.Database.ExecuteSqlRawAsync(
                $"CREATE TABLE {guardTable} (guard_id uuid PRIMARY KEY, cm_id uuid NOT NULL, " +
                $"CONSTRAINT {foreignKeyName} FOREIGN KEY (cm_id) REFERENCES cm_contexts(cm_id) ON DELETE RESTRICT)");
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO {guardTable} (guard_id, cm_id) VALUES (@g, @c)",
                new NpgsqlParameter("g", Guid.NewGuid()),
                new NpgsqlParameter("c", Guid.Parse(contextId)));
#pragma warning restore EF1002

            var fingerprint = await ContextRowFingerprintAsync(context, "cm_contexts", created.JobOnId);

            // The application path: the refusal is a typed failure and nothing is removed.
            var failure = await Assert.ThrowsAsync<JobOnPersistenceException>(async () =>
                await new JobOnRepository(context).UpdatedAsync(
                (await new JobOnRepository(context).GetByIdAsync(created.JobOnId, CancellationToken.None))!,
                [new ToolContextChange(ToolContextType.Cm, ToolAssociationAction.Remove, null)],
                CancellationToken.None));

            Assert.Equal(JobOnPersistenceFailureReason.DependencyExists, failure.Reason);
            Assert.Equal(fingerprint, await ContextRowFingerprintAsync(context, "cm_contexts", created.JobOnId));

            // The database path: the RESTRICT foreign key refuses the delete itself.
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM cm_contexts WHERE cm_id = @c",
                    new NpgsqlParameter("c", Guid.Parse(contextId))));

            Assert.Equal("23503", rejected.SqlState);
            Assert.StartsWith($"fk_{guardTable}", rejected.ConstraintName, StringComparison.Ordinal);

            Assert.Equal(1, await CountContextAsync(context, "cm_contexts", created.JobOnId));
            Assert.Equal(
                1,
                await CountAsync(context, "job_ons WHERE jobon_id = @p", new NpgsqlParameter("p", created.JobOnId)));
            Assert.Equal(
                1,
                await CountAsync(context, $"{guardTable} WHERE cm_id = @c", new NpgsqlParameter("c", Guid.Parse(contextId))));
        }
        finally
        {
#pragma warning disable EF1002 // test-generated token identifier; see above
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS {guardTable}");
#pragma warning restore EF1002
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX17 (AC-104): each context entity/configuration declares exactly the contracted columns, no
    /// <c>version</c> column and no extra navigation.
    /// </summary>
    [SkippableFact]
    public async Task CTX17_EachContextEntityAndConfigurationDeclaresExactlyTheContractedColumns()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var cases = new (string EntityFile, string ConfigurationFile, string KeyColumn)[]
        {
            ("BqContextEntity.cs", "BqContextEntityConfiguration.cs", "bq_id"),
            ("CmContextEntity.cs", "CmContextEntityConfiguration.cs", "cm_id"),
            ("MfContextEntity.cs", "MfContextEntityConfiguration.cs", "mf_id"),
        };

        var expectedColumns = Sorted(new[]
        {
            "jobon_id", "tool_id", "tool_type", "tool_reference", "tool_lot", "created_at", "updated_at",
        });

        foreach (var testCase in cases)
        {
            var entity = WithoutComments(await File.ReadAllTextAsync(
                P2T04SourcePath($"Persistence/ToolJobOn/Entities/{testCase.EntityFile}")));

            // Exactly eight properties: the key, the two FKs, the frozen triple and the timestamps.
            var properties = Regex.Matches(entity, @"public\s+(?<type>[\w?<>\.]+)\s+(?<name>\w+)\s+\{ get; set; \}")
                .Select(match => (Type: match.Groups["type"].Value, Name: match.Groups["name"].Value))
                .ToList();

            Assert.Equal(8, properties.Count);
            Assert.Equal(
                new[] { "DateTimeOffset", "Guid", "string" },
                properties.Select(property => property.Type).Distinct(StringComparer.Ordinal)
                    .OrderBy(type => type, StringComparer.Ordinal).ToArray());
            Assert.Contains(properties, property => property.Name.EndsWith("Id", StringComparison.Ordinal));

            // No navigation property and no version column on the entity.
            Assert.DoesNotContain("Entity?", entity, StringComparison.Ordinal);
            Assert.DoesNotContain("version", entity, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Version", entity, StringComparison.Ordinal);

            var configuration = WithoutComments(await File.ReadAllTextAsync(
                P2T04SourcePath($"Persistence/ToolJobOn/EntityConfigurations/{testCase.ConfigurationFile}")));

            var declaredColumns = Regex.Matches(configuration, "HasColumnName\\(\"(?<name>[a-z_]+)\"\\)")
                .Select(match => match.Groups["name"].Value)
                .ToList();

            Assert.Equal(
                Sorted([testCase.KeyColumn, .. expectedColumns]),
                Sorted(declaredColumns));

            Assert.DoesNotContain("version", configuration, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ToolContext CmContext(
        Guid contextId,
        DomainJobOnId jobOnId,
        Guid toolId,
        string reference,
        string lot) =>
        new(
            ToolContextType.Cm,
            contextId,
            jobOnId,
            ToolId.From(toolId),
            new ToolContextSnapshot(ToolType.Cm, reference, lot));

    private static async Task<Guid> CreateToolAsync(
        DmoDbContext context,
        string type,
        string reference,
        string lot) =>
        Assert.IsType<ToolResult.Created>(await Tools(context).CreateAsync(
            new CreateToolCommand(type, reference, lot, null, null, ["B1"]),
            CancellationToken.None)).ToolId;

    private static async Task<JobOnResult.Created> CreateJobOnAsync(
        DmoDbContext context,
        string reference,
        string productionNumber,
        string machine,
        Guid? cmToolId = null,
        Guid? mfToolId = null,
        Guid? bqToolId = null,
        DateOnly? productionDate = null) =>
        Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
            new CreateJobOnCommand(reference, productionNumber, machine, productionDate, cmToolId, mfToolId, bqToolId),
            CancellationToken.None));

    private static async Task<JobOnFicha> FichaAsync(DmoDbContext context, Guid jobOnId) =>
        Assert.IsType<JobOnResult.Ficha>(await JobOns(context).GetAsync(jobOnId, CancellationToken.None)).Value;

    private static ToolService Tools(DmoDbContext context) => new(new ToolRepository(context));

    private static JobOnService JobOns(DmoDbContext context) => new(
        new JobOnRepository(context),
        new ToolRepository(context),
        [new JobOnLineageDependencyProbe(context)]);

    private static DmoDbContext CreateMarkedContext(FailOnNthSaveInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseNpgsql(PersistenceTestDatabase.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new DmoDbContext(options);
    }

    private static Task InsertJobOnAsync(
        DmoDbContext context,
        Guid jobOnId,
        string reference,
        string productionNumber,
        string machine,
        DateOnly? productionDate = null) =>
        context.Database.ExecuteSqlRawAsync(
            "INSERT INTO job_ons (jobon_id, reference, production_number, machine, production_date) " +
            "VALUES (@id, @reference, @production, @machine, @date)",
            new NpgsqlParameter("id", jobOnId),
            new NpgsqlParameter("reference", reference),
            new NpgsqlParameter("production", productionNumber),
            new NpgsqlParameter("machine", machine),
            new NpgsqlParameter("date", (object?)productionDate ?? DBNull.Value));

    private static Task InsertContextAsync(
        DmoDbContext context,
        string table,
        Guid contextId,
        Guid jobOnId,
        Guid toolId,
        string toolType,
        string toolReference,
        string toolLot)
    {
        var keyColumn = table[..table.IndexOf('_')] + "_id";

        return context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {table} ({keyColumn}, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            "VALUES (@id, @jobon, @tool, @type, @reference, @lot)",
            new NpgsqlParameter("id", contextId),
            new NpgsqlParameter("jobon", jobOnId),
            new NpgsqlParameter("tool", toolId),
            new NpgsqlParameter("type", toolType),
            new NpgsqlParameter("reference", toolReference),
            new NpgsqlParameter("lot", toolLot));
    }

    /// <summary>Every column of the occurrence row, so "unchanged" means every column.</summary>
    private static Task<string> RowFingerprintAsync(DmoDbContext context, Guid jobOnId) =>
        ScalarAsync(
            context,
            "SELECT jobon_id::text || '|' || reference || '|' || production_number || '|' || machine || '|' || " +
            "coalesce(production_date::text, '-') || '|' || coalesce(copied_from_jobon_id::text, '-') || '|' || " +
            "version || '|' || created_at::text || '|' || updated_at::text FROM job_ons WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOnId));

    /// <summary>Every context row of the Job On across the three tables.</summary>
    private static async Task<string> ContextFingerprintAsync(DmoDbContext context, Guid jobOnId)
    {
        var rows = await QueryStringsAsync(
            context,
            "SELECT 'cm|' || cm_id::text || '|' || tool_id::text || '|' || tool_reference || '|' || tool_lot || " +
            "'|' || created_at::text || '|' || updated_at::text FROM cm_contexts WHERE jobon_id = @p " +
            "UNION ALL SELECT 'mf|' || mf_id::text || '|' || tool_id::text || '|' || tool_reference || '|' || " +
            "tool_lot || '|' || created_at::text || '|' || updated_at::text FROM mf_contexts WHERE jobon_id = @p " +
            "UNION ALL SELECT 'bq|' || bq_id::text || '|' || tool_id::text || '|' || tool_reference || '|' || " +
            "tool_lot || '|' || created_at::text || '|' || updated_at::text FROM bq_contexts WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOnId));

        return string.Join(';', Sorted(rows));
    }

    private static Task<string> ContextRowFingerprintAsync(DmoDbContext context, string table, Guid jobOnId) =>
        ScalarAsync(
            context,
            $"SELECT {table}::text FROM {table} WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOnId));

    /// <summary>Reads one production source file relative to the repository root.</summary>
    private static string P2T04SourcePath(string relativePath) =>
        Path.Combine(
            P2T03ProductionScan.RepositoryRoot(),
            "src",
            "DMO.Infrastructure",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Extracts one method body up to the next member's documentation comment.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' was not found in the source.");

        var end = source.IndexOf("/// <inheritdoc />", start, StringComparison.Ordinal);
        Assert.True(end > start, $"The end of '{signature}' was not found in the source.");

        return source[start..end];
    }

    /// <summary>Removes C# comments so a documentation remark is never mistaken for code.</summary>
    private static string WithoutComments(string source) =>
        Regex.Replace(
                Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
                "//[^\r\n]*",
                string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal);

    private static void AssertNoLifecycleVocabulary(string code)
    {
        foreach (var token in new[]
                 {
                     "rascunho", "planeado", "em fabrico", "fechado", "cancelado", "active", "locked",
                     "approved", "lifecycle", "status", "state",
                 })
        {
            Assert.DoesNotContain(token, code, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Deletes the rows this test created, in FK-safe order (disposable database only).</summary>
    private static async Task CleanupAsync(DmoDbContext context, string token)
    {
        var pattern = $"%{token}%";

        foreach (var table in new[] { "bq_contexts", "mf_contexts", "cm_contexts" })
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {table} WHERE jobon_id IN " +
                "(SELECT jobon_id FROM job_ons WHERE reference LIKE @p) OR tool_id IN " +
                "(SELECT tool_id FROM tools WHERE reference LIKE @p)",
                new NpgsqlParameter("p", pattern));
        }

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM tool_machines WHERE tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE @p)",
            new NpgsqlParameter("p", pattern));
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM job_ons WHERE reference LIKE @p",
            new NpgsqlParameter("p", pattern));
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM tools WHERE reference LIKE @p",
            new NpgsqlParameter("p", pattern));
    }

    private static Task<int> CountContextAsync(DmoDbContext context, string table, Guid jobOnId) =>
        CountAsync(context, $"{table} WHERE jobon_id = @p", new NpgsqlParameter("p", jobOnId));

    private static async Task<int> CountAsync(
        DmoDbContext context,
        string fromAndWhere,
        params NpgsqlParameter[] parameters) =>
        int.Parse(Assert.Single(await QueryStringsAsync(
            context,
            $"SELECT count(*) FROM {fromAndWhere}",
            parameters)));

    private static IReadOnlyList<string> Sorted(IReadOnlyList<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToList();

    private static async Task<string> ScalarAsync(
        DmoDbContext context,
        string sql,
        params NpgsqlParameter[] parameters) =>
        Assert.Single(await QueryStringsAsync(context, sql, parameters));

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        DmoDbContext context,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        var results = new List<string>();
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.GetValue(i)?.ToString() ?? string.Empty;
            }

            results.Add(string.Join("|", values));
        }

        return results;
    }

    /// <summary>Fails the Nth <c>SaveChangesAsync</c> of the marked context (test-only).</summary>
    private sealed class FailOnNthSaveInterceptor(int failOn) : SaveChangesInterceptor
    {
        private int _calls;

        /// <summary>The number of <c>SaveChangesAsync</c> calls observed so far.</summary>
        public int Calls => _calls;

        /// <summary>The tracked entries of the failed save (entity type + state), sorted.</summary>
        public IReadOnlyList<string> FailedSaveEntries { get; private set; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);

            if (call == failOn)
            {
                FailedSaveEntries = eventData.Context is { } tracked
                    ? tracked.ChangeTracker.Entries()
                        .Select(entry => $"{entry.Entity.GetType().Name}:{entry.State}")
                        .OrderBy(entry => entry, StringComparer.Ordinal)
                        .ToList()
                    : [];

                throw new ForcedSaveFailureException($"Forced failure on SaveChanges call {call}.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>The test-only failure thrown by <see cref="FailOnNthSaveInterceptor"/>.</summary>
    private sealed class ForcedSaveFailureException(string message) : Exception(message);
}
