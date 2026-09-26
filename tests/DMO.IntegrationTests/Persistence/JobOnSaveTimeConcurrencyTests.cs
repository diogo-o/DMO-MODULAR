using System.Data;
using DMO.Application.JobOn;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Web.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table names) against
// a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T04 env-gated regression — the save-time optimistic-concurrency race of contract §15.1:
/// a version bump committed by another transaction <b>between</b> the repository's explicit
/// in-transaction compare and its <c>SaveChangesAsync</c> must surface as the typed domain
/// conflict (stale-version), never as an unhandled <c>DbUpdateConcurrencyException</c> (HTTP 500).
/// </summary>
/// <remarks>
/// This test reproduces the actual race against the disposable PostgreSQL database: the second
/// connection performs a real committed <c>UPDATE</c> of the same row while the first write is
/// in flight, so EF's guarded <c>UPDATE ... WHERE version = N</c> matches zero rows and EF raises
/// <c>DbUpdateConcurrencyException</c> for real — nothing is mocked away. It is the regression
/// test mandated by the Architect implementation review (dmo-work
/// <c>5d490113b8dd1d80759742cc86a99e8f0294b44c</c>, verdict REJECT, contract §15.1 correction).
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class JobOnSaveTimeConcurrencyTests
{
    /// <summary>
    /// The mandatory race reproduction (contract §15.1): load version N → explicit compare passes
    /// → second connection commits N+1 before the first write's save → first write's guarded
    /// save matches zero rows → <c>DbUpdateConcurrencyException</c> → mapped through
    /// <c>ConcurrencyConflictExceptionMapping.ToDomainConflict</c> → typed
    /// <c>Refused(StaleVersion)</c> → transport token <c>stale-version</c> (HTTP 409).
    /// </summary>
    [SkippableFact]
    public async Task S15_SaveTimeConcurrencyRaceIsMappedToAStaleVersionRefusal()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var productionNumber = $"pn-{token}-01";

        try
        {
            // Seed an occurrence at version 1 through the real application path.
            var created = Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
                new CreateJobOnCommand(reference, productionNumber, "B1", null, null, null, null),
                CancellationToken.None));

            Assert.Equal(1, created.Version);

            // The race seam: the interceptor fires inside <see cref="JobOnRepository"/>'s
            // SaveChangesAsync — AFTER the repository's explicit in-transaction version compare
            // has passed (the committed row is still version 1) and BEFORE EF's guarded UPDATE
            // executes. A second, real database connection then commits version 2.
            var racer = new SaveTimeRaceInterceptor(created.JobOnId);
            await using var marked = CreateMarkedContext(racer);

            var result = await JobOns(marked).UpdateAsync(
                new UpdateJobOnCommand(
                    created.JobOnId,
                    ExpectedVersion: 1,
                    reference,
                    productionNumber,
                    "C1", // a real fact change, so the guarded UPDATE is actually issued
                    null,
                    Associations: [],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None);

            // The explicit compare passed (version 1 == 1) and the save then lost the race to the
            // second connection's committed version 2: the result must be the typed conflict, not
            // an exception and not a silent overwrite.
            var refused = Assert.IsType<JobOnResult.Refused>(result);
            Assert.Equal(JobOnRefusalReason.StaleVersion, refused.Reason);
            Assert.Equal("stale-version", JobOnEndpoints.RefusalToken(refused.Reason));

            // The interceptor genuinely committed its bump on a separate connection.
            Assert.Equal(1, racer.Bumps);

            // Nothing of the first write was persisted: the row still carries the seeded machine
            // (B1, not the edit's C1) and the second connection's committed version 2.
            Assert.Equal(
                $"{reference}|{productionNumber}|B1|2",
                await ScalarAsync(
                    context,
                    "SELECT reference || '|' || production_number || '|' || machine || '|' || version " +
                    "FROM job_ons WHERE jobon_id = @p",
                    new NpgsqlParameter("p", created.JobOnId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    private static JobOnService JobOns(DmoDbContext context) => new(
        new JobOnRepository(context),
        new ToolRepository(context),
        [new JobOnLineageDependencyProbe(context)]);

    private static DmoDbContext CreateMarkedContext(SaveTimeRaceInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseNpgsql(PersistenceTestDatabase.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new DmoDbContext(options);
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

    private static async Task<string> ScalarAsync(
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

        return Assert.Single(results);
    }

    /// <summary>
    /// The test-only race seam: on the repository save in flight, a second — real — database
    /// connection commits <c>SET version = version + 1</c> for the target row before the first
    /// write's guarded <c>UPDATE</c> evaluates its concurrency-token predicate. Npgsql pooling
    /// hands the second context a distinct connection while the first save's transaction is open.
    /// </summary>
    private sealed class SaveTimeRaceInterceptor(Guid jobOnId) : SaveChangesInterceptor
    {
        private int _bumps;

        /// <summary>The number of version bumps committed by the second connection.</summary>
        public int Bumps => _bumps;

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _bumps, 1) == 0)
            {
                using var racer = PersistenceTestDatabase.CreateContext();
                racer.Database.ExecuteSqlRaw(
                    "UPDATE job_ons SET version = version + 1 WHERE jobon_id = @p",
                    new NpgsqlParameter("p", jobOnId));
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}