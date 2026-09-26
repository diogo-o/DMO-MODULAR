using System.Data;
using System.Threading;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using DMO.IntegrationTests.Frontend.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table identifiers or
// row identifiers derived from a fresh Guid) against a disposable database. Analyzer EF1003
// suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T04 env-gated integration test — the Job On delete over the disposable PostgreSQL database:
/// the <c>RESTRICT</c> foreign-key mesh, the permitted-delete scoping, the lineage self-FK, the
/// stale-version concurrency refusal and the mid-delete rollback of §11.5.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §11.5 (delete and dependency rule), §15 (concurrency), AC-79/AC-80/
/// AC-81/AC-106 and §20.5 (rows DEP2, DEP3, DEP4, DEP9, DEP10). Every assertion is scoped to the
/// rows this test creates.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class JobOnDeleteDependencyIntegrationTests
{
    private static readonly string[] ContextTables = ["cm_contexts", "mf_contexts", "bq_contexts"];

    /// <summary>
    /// DEP2 (AC-80, AC-106): <c>pg_constraint.confdeltype = 'r'</c> for all eight P2-T04 foreign keys
    /// — every FK of the six P2-T04 tables is <c>RESTRICT</c>, and no other FK exists there.
    /// </summary>
    [SkippableFact]
    public async Task DEP2_AllEightP2T04ForeignKeysAreRestrict()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var rows = await QueryStringsAsync(
                context,
                "SELECT conname || '|' || confdeltype::text FROM pg_constraint " +
                "WHERE contype = 'f' AND conrelid IN " +
                "(SELECT oid FROM pg_class WHERE relname IN " +
                "('job_ons', 'tools', 'tool_machines', 'cm_contexts', 'mf_contexts', 'bq_contexts')) " +
                "ORDER BY conname");

            Assert.Equal(
                new[]
                {
                    "FK_bq_contexts_job_ons_jobon_id",
                    "FK_bq_contexts_tools_tool_id",
                    "FK_cm_contexts_job_ons_jobon_id",
                    "FK_cm_contexts_tools_tool_id",
                    "FK_job_ons_job_ons_copied_from_jobon_id",
                    "FK_mf_contexts_job_ons_jobon_id",
                    "FK_mf_contexts_tools_tool_id",
                    "FK_tool_machines_tools_tool_id",
                },
                rows.Select(row => row.Split('|')[0]).ToArray());

            Assert.All(rows, row => Assert.Equal("r", row.Split('|')[1]));
    }

    /// <summary>
    /// DEP3 (AC-79): a permitted delete removes this Job On's context rows and its own row, and
    /// <c>tools</c>/<c>tool_machines</c>/other Job Ons are unchanged.
    /// </summary>
    [SkippableFact]
    public async Task DEP3_APermittedDeleteRemovesThisJobOnsContextsAndRowAndLeavesEverythingElseUnchanged()
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
            var target = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);
            var other = await CreateJobOnAsync(context, reference, $"pn-{token}-02", "C1");

            var toolsBefore = await TableFingerprintAsync(context, "tools", reference);
            var machinesBefore = await MachineFingerprintAsync(context, reference);
            var otherBefore = await RowFingerprintAsync(context, other.JobOnId);

            var deleted = Assert.IsType<JobOnResult.Deleted>(await JobOns(context).DeleteAsync(
                new DeleteJobOnCommand(
                    target.JobOnId,
                    ExpectedVersion: 1,
                    DeleteConfirmed: true,
                    DateThresholdWarningAcknowledged: true),
                CancellationToken.None));

            Assert.Equal(target.JobOnId, deleted.JobOnId);

            // This Job On's contexts are gone, and its own row is gone.
            foreach (var table in ContextTables)
            {
                Assert.Equal(0, await CountContextAsync(context, table, target.JobOnId));
            }

            Assert.Equal(
                0,
                await CountAsync(context, "job_ons WHERE jobon_id = @p", new NpgsqlParameter("p", target.JobOnId)));

            // tools, tool_machines and the other Job On are untouched.
            Assert.Equal(toolsBefore, await TableFingerprintAsync(context, "tools", reference));
            Assert.Equal(machinesBefore, await MachineFingerprintAsync(context, reference));
            Assert.Equal(otherBefore, await RowFingerprintAsync(context, other.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DEP4 (AC-78): deleting a Job On that is the recorded duplication source is refused both by the
    /// lineage probe and by the self-FK.
    /// </summary>
    [SkippableFact]
    public async Task DEP4_DeletingARecordedDuplicationSourceIsRefusedByTheProbeAndByTheSelfForeignKey()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");
            var dependent = Assert.IsType<JobOnResult.Duplicated>(await JobOns(context).DuplicateAsync(
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B1", null),
                CancellationToken.None));

            // Seam 1: the lineage probe refuses the delete and names the dependency kind.
            var refusal = Assert.IsType<JobOnResult.Refused>(await JobOns(context).DeleteAsync(
                new DeleteJobOnCommand(
                    source.JobOnId,
                    ExpectedVersion: 1,
                    DeleteConfirmed: true,
                    DateThresholdWarningAcknowledged: true),
                CancellationToken.None));

            Assert.Equal(JobOnRefusalReason.DependencyExists, refusal.Reason);
            Assert.NotNull(refusal.Dependencies);
            Assert.Contains(
                refusal.Dependencies!,
                dependency => dependency.Kind == JobOnLineageDependencyProbe.DuplicationLineageKind
                    && !string.IsNullOrWhiteSpace(dependency.Description));

            // Seam 2: the raw delete is refused by the self-referencing RESTRICT FK.
            var violation = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM job_ons WHERE jobon_id = @p",
                    new NpgsqlParameter("p", source.JobOnId)));

            Assert.Equal("23503", violation.SqlState);
            Assert.Equal(
                JobOnEntityConfiguration.CopiedFromForeignKeyConstraintName,
                violation.ConstraintName);

            // Both rows still exist and the lineage still points at the source.
            Assert.Equal(2, await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));
            Assert.Equal(
                $"{source.JobOnId}",
                Assert.Single(await QueryStringsAsync(
                    context,
                    "SELECT copied_from_jobon_id::text FROM job_ons WHERE jobon_id = @p",
                    new NpgsqlParameter("p", dependent.JobOnId))));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DEP9 (AC-81): a stale <c>ExpectedVersion</c> refuses the delete and leaves the Job On and its
    /// contexts intact — at the service seam and at the repository seam.
    /// </summary>
    [SkippableFact]
    public async Task DEP9_AStaleExpectedVersionRefusesTheDeleteAndLeavesTheJobOnAndItsContextsIntact()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var target = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool);

            var rowBefore = await RowFingerprintAsync(context, target.JobOnId);

            // Service seam: the stale version is refused before any write.
            var refusal = Assert.IsType<JobOnResult.Refused>(await JobOns(context).DeleteAsync(
                new DeleteJobOnCommand(
                    target.JobOnId,
                    ExpectedVersion: 2,
                    DeleteConfirmed: true,
                    DateThresholdWarningAcknowledged: true),
                CancellationToken.None));

            Assert.Equal(JobOnRefusalReason.StaleVersion, refusal.Reason);

            // Repository seam: the same stale version is a hard concurrency conflict.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                new JobOnRepository(context).DeletedAsync(
                    target.JobOnId,
                    expectedVersion: 2,
                    CancellationToken.None));

            Assert.Equal(rowBefore, await RowFingerprintAsync(context, target.JobOnId));
            Assert.Equal(1, await CountContextAsync(context, "cm_contexts", target.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DEP10 (AC-79): a forced failure between the context delete and the Job On delete rolls back
    /// completely — contexts and row are still present.
    /// </summary>
    [SkippableFact]
    public async Task DEP10_AForcedFailureBetweenTheContextDeleteAndTheJobOnDeleteRollsBackCompletely()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var target = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool);

            var rowBefore = await RowFingerprintAsync(context, target.JobOnId);

            // A marked context fails on its FIRST SaveChanges: inside DeletedAsync that is the save
            // that commits the Job On row delete — after the three ExecuteDeleteAsync context
            // deletes already ran. The transaction must roll everything back.
            var interceptor = new FailOnNthSaveInterceptor(1);
            await using var marked = CreateMarkedContext(interceptor);

            await Assert.ThrowsAsync<ForcedSaveFailureException>(() => JobOns(marked).DeleteAsync(
                new DeleteJobOnCommand(
                    target.JobOnId,
                    ExpectedVersion: 1,
                    DeleteConfirmed: true,
                    DateThresholdWarningAcknowledged: true),
                CancellationToken.None));

            Assert.Equal(1, interceptor.Calls);

            // Total rollback: the Job On row and its context rows are still present, unchanged.
            Assert.Equal(rowBefore, await RowFingerprintAsync(context, target.JobOnId));
            foreach (var table in ContextTables)
            {
                Assert.Equal(
                    table == "cm_contexts" ? 1 : 0,
                    await CountContextAsync(context, table, target.JobOnId));
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================= service helpers ====================================================

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
        Guid? bqToolId = null) =>
        Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
            new CreateJobOnCommand(reference, productionNumber, machine, null, cmToolId, mfToolId, bqToolId),
            CancellationToken.None));

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

    // ================= database helpers ===================================================

    /// <summary>Every column of the occurrence row, so "unchanged" means every column.</summary>
    private static Task<string> RowFingerprintAsync(DmoDbContext context, Guid jobOnId) =>
        ScalarAsync(
            context,
            "SELECT jobon_id::text || '|' || reference || '|' || production_number || '|' || machine || '|' || " +
            "coalesce(production_date::text, '-') || '|' || coalesce(copied_from_jobon_id::text, '-') || '|' || " +
            "version || '|' || created_at::text || '|' || updated_at::text FROM job_ons WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOnId));

    /// <summary>Every scoped <c>tools</c> row, so "unchanged" means every column.</summary>
    private static Task<IReadOnlyList<string>> TableFingerprintAsync(
        DmoDbContext context,
        string table,
        string reference) =>
        QueryStringsAsync(
            context,
            "SELECT tool_id::text || '|' || tool_type || '|' || reference || '|' || lot || '|' || " +
            "coalesce(processo, '-') || '|' || coalesce(quantity::text, '-') || '|' || created_at::text || '|' || " +
            "updated_at::text FROM " + table + " WHERE reference = @p ORDER BY 1",
            new NpgsqlParameter("p", reference));

    /// <summary>Every scoped <c>tool_machines</c> row (machines resolved through the Tool).</summary>
    private static Task<IReadOnlyList<string>> MachineFingerprintAsync(DmoDbContext context, string reference) =>
        QueryStringsAsync(
            context,
            "SELECT tool_id::text || '|' || machine FROM tool_machines WHERE tool_id IN " +
            "(SELECT tool_id FROM tools WHERE reference = @p) ORDER BY 1",
            new NpgsqlParameter("p", reference));

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

    /// <summary>Deletes the rows this test created, in FK-safe order (disposable database only).</summary>
    private static async Task CleanupAsync(DmoDbContext context, string token)
    {
        var pattern = $"%{token}%";

        foreach (var table in ContextTables)
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

    /// <summary>Fails the Nth <c>SaveChangesAsync</c> of the marked context (test-only).</summary>
    private sealed class FailOnNthSaveInterceptor(int failOn) : SaveChangesInterceptor
    {
        private int _calls;

        /// <summary>The number of <c>SaveChangesAsync</c> calls observed so far.</summary>
        public int Calls => _calls;

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);

            if (call == failOn)
            {
                throw new ForcedSaveFailureException($"Forced failure on SaveChanges call {call}.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>The test-only failure thrown by <see cref="FailOnNthSaveInterceptor"/>.</summary>
    private sealed class ForcedSaveFailureException(string message) : Exception(message);
}