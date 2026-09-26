using System.Data;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.Entities;
using DMO.IntegrationTests.ControloCreate;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T05 env-gated integration test — the <see cref="PesoJobOnDependencyProbe"/> over the
/// disposable PostgreSQL database: production-bound and pending-peso dependencies and the composed
/// Job On delete/edit refusals (contract §20.4.1).
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §20.4.1 (probe), P2-T04 §11.5/§12.4 (probe composition) and §30 rows
/// Probe-DEP1–Probe-DEP5. Every assertion is scoped to the rows this test creates (fresh Guid and
/// per-test token).
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class PesoJobOnDependencyProbeIntegrationTests
{
    /// <summary>
    /// Probe-DEP1: a production-bound Peso anchored to the target occurrence's CM context is a
    /// reported <c>peso</c> dependency of that occurrence.
    /// </summary>
    [SkippableFact]
    public async Task Probe_DEP1_AProductionBoundPesoIsReportedAsADependency()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, jobOnId, cmId) = await SeedCmProductionAsync(context, token);

            Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            var probe = new PesoJobOnDependencyProbe(context);
            var report = await probe.InspectAsync(
                new JobOnDependencyTarget(jobOnId, cmId, null, null),
                CancellationToken.None);

            Assert.True(report.HasDependencies);
            Assert.Equal(PesoJobOnDependencyProbe.SourceName, report.Source);
            var dependency = Assert.Single(report.Dependencies);
            Assert.Equal(PesoJobOnDependencyProbe.PesoDependencyKind, dependency.Kind);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// Probe-DEP2: a PENDING Peso anchored to the same Tool as the occurrence's CM context is a
    /// reported <c>peso</c> dependency of that occurrence.
    /// </summary>
    [SkippableFact]
    public async Task Probe_DEP2_APendingPesoOnTheSameCmToolIsReportedAsADependency()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (toolId, jobOnId, cmId) = await SeedCmProductionAsync(context, token);

            Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            var probe = new PesoJobOnDependencyProbe(context);
            var report = await probe.InspectAsync(
                new JobOnDependencyTarget(jobOnId, cmId, null, null),
                CancellationToken.None);

            Assert.True(report.HasDependencies);
            var dependency = Assert.Single(report.Dependencies);
            Assert.Equal(PesoJobOnDependencyProbe.PesoDependencyKind, dependency.Kind);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// Probe-DEP3: an occurrence with no Peso anywhere reports no dependencies.
    /// </summary>
    [SkippableFact]
    public async Task Probe_DEP3_AnOccurrenceWithoutPesosReportsNoDependencies()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");

        try
        {
            var (_, jobOnId, cmId) = await SeedCmProductionAsync(context, token);

            var probe = new PesoJobOnDependencyProbe(context);
            var report = await probe.InspectAsync(
                new JobOnDependencyTarget(jobOnId, cmId, null, null),
                CancellationToken.None);

            Assert.False(report.HasDependencies);
            Assert.Empty(report.Dependencies);
            Assert.Equal(PesoJobOnDependencyProbe.SourceName, report.Source);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// Probe-DEP4: a Job On delete composed with the real probe refuses with
    /// <c>DependencyExists</c> naming <c>peso</c> and deletes nothing.
    /// </summary>
    [SkippableFact]
    public async Task Probe_DEP4_DeleteAsyncRefusesWhenAPesoDependsOnTheOccurrence()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, jobOnId, cmId) = await SeedCmProductionAsync(context, token);
            var pesoId = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None)).PesoId;

            var jobOns = new JobOnService(
                new JobOnRepository(context),
                new ToolRepository(context),
                [new PesoJobOnDependencyProbe(context)]);

            var refused = Assert.IsType<JobOnResult.Refused>(await jobOns.DeleteAsync(
                new DeleteJobOnCommand(
                    jobOnId,
                    ExpectedVersion: 1,
                    DeleteConfirmed: true,
                    DateThresholdWarningAcknowledged: true),
                CancellationToken.None));

            Assert.Equal(JobOnRefusalReason.DependencyExists, refused.Reason);
            Assert.NotNull(refused.Dependencies);
            Assert.Contains(refused.Dependencies!, dependency =>
                dependency.Kind == PesoJobOnDependencyProbe.PesoDependencyKind);

            // Nothing was deleted: the occurrence and the Peso are still there.
            Assert.Equal(
                1,
                await CountAsync(context, "job_ons WHERE jobon_id = @p", new NpgsqlParameter("p", jobOnId)));
            Assert.Equal(
                1,
                await CountAsync(context, "pesos WHERE peso_id = @p", new NpgsqlParameter("p", pesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// Probe-DEP5: a CM removal inside a Job On edit fails closed with <c>DependencyExists</c> — the
    /// Peso's RESTRICT foreign key refuses the context deletion and the context row survives.
    /// </summary>
    [SkippableFact]
    public async Task Probe_DEP5_ACmRemovalInsideAnEditFailsClosedWhenAPesoDependsOnIt()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, jobOnId, cmId) = await SeedCmProductionAsync(context, token);
            var pesoId = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None)).PesoId;

            // The Job On edit runs on a SEPARATE request-scoped context (the real request
            // boundary): the Peso row is deliberately NOT tracked there, so EF performs no
            // relationship fixup and the removal of the referenced cm_contexts row fails closed
            // through the RESTRICT foreign key (23503 → DependencyExists), exactly the production
            // behavior (Probe-DEP4 proves the same for the delete path).
            await using var editContext = PersistenceTestDatabase.CreateContext();

            var jobOns = new JobOnService(
                new JobOnRepository(editContext),
                new ToolRepository(editContext),
                [new PesoJobOnDependencyProbe(editContext)]);

            var refused = Assert.IsType<JobOnResult.Refused>(await jobOns.UpdateAsync(
                new UpdateJobOnCommand(
                    jobOnId,
                    ExpectedVersion: 1,
                    $"ref-{token}",
                    $"pn-{token}-01",
                    "B1",
                    null,
                    Associations: [new ToolAssociationChange(ToolContextType.Cm, ToolAssociationAction.Remove, null)],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None));

            Assert.Equal(JobOnRefusalReason.DependencyExists, refused.Reason);

            // Fail closed: the CM context row and the Peso survive.
            Assert.Equal(
                1,
                await CountAsync(
                    context,
                    "cm_contexts WHERE cm_id = @p",
                    new NpgsqlParameter("p", cmId)));
            Assert.Equal(
                1,
                await CountAsync(context, "pesos WHERE peso_id = @p", new NpgsqlParameter("p", pesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // --------------------------------------------------------------------------------------------
    // Composition helpers
    // --------------------------------------------------------------------------------------------

    private static ControloCreateService Pesos(DmoDbContext context) => new(
        new PesoRepository(context),
        new DmoPesoContextRead(context),
        new JobOnService(new JobOnRepository(context), new ToolRepository(context), []),
        new ToolService(new ToolRepository(context)),
        new FixedCalculationConfiguration(),
        new GlassDensitySettingsRepository(context));

    private static ToolService Tools(DmoDbContext context) => new(new ToolRepository(context));

    private static JobOnService JobOns(DmoDbContext context) => new(
        new JobOnRepository(context),
        new ToolRepository(context),
        []);

    private static async Task<Guid> SeedUserAsync(DmoDbContext context, string token)
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        context.Set<UserEntity>().Add(new UserEntity
        {
            UserId = userId,
            AuthIdentityId = $"auth-{token}",
            Name = $"Test User {token}",
            CompanyNumber = $"CN{token}",
            Email = $"user-{token}@example.pt",
            Active = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();

        return userId;
    }

    /// <summary>
    /// Seeds a canonical CM Tool and a production occurrence with its CM context; returns the
    /// (tool_id, jobon_id, cm_id) triple.
    /// </summary>
    private static async Task<(Guid ToolId, Guid JobOnId, Guid CmId)> SeedCmProductionAsync(
        DmoDbContext context,
        string token)
    {
        var toolId = Assert.IsType<ToolResult.Created>(await Tools(context).CreateAsync(
            new CreateToolCommand("CM", $"ref-{token}", "01", "NNPB", null, ["B1"]),
            CancellationToken.None)).ToolId;

        var jobOn = Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
            new CreateJobOnCommand($"ref-{token}", $"pn-{token}-01", "B1", null, toolId, null, null),
            CancellationToken.None));

        var cmId = Guid.Parse(await ScalarAsync(
            context,
            "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOn.JobOnId)));

        return (toolId, jobOn.JobOnId, cmId);
    }

    /// <summary>Deletes the rows this test created, in FK-safe order (disposable database only).</summary>
    private static async Task CleanupAsync(DmoDbContext context, string token)
    {
        var pattern = $"%{token}%";

        foreach (var pesoSuffix in new[]
                 {
                     "created_by_user_id IN (SELECT user_id FROM users WHERE company_number LIKE @p)",
                     "tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE @p)",
                     "cm_id IN (SELECT cm_id FROM cm_contexts WHERE jobon_id IN (SELECT jobon_id FROM job_ons WHERE reference LIKE @p))",
                 })
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM peso_measurement_rows WHERE peso_id IN " +
                $"(SELECT peso_id FROM pesos WHERE {pesoSuffix})",
                new NpgsqlParameter("p", pattern));
        }

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM pesos WHERE created_by_user_id IN (SELECT user_id FROM users WHERE company_number LIKE @p) " +
            "OR tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE @p) " +
            "OR cm_id IN (SELECT cm_id FROM cm_contexts WHERE jobon_id IN " +
            "(SELECT jobon_id FROM job_ons WHERE reference LIKE @p))",
            new NpgsqlParameter("p", pattern));

        foreach (var table in new[] { "cm_contexts", "mf_contexts", "bq_contexts" })
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
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM users WHERE company_number LIKE @p",
            new NpgsqlParameter("p", pattern));
    }

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
}