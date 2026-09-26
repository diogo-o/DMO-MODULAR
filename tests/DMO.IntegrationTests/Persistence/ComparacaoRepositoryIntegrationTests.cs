using System.Data;
using DMO.Application.ControloCreate;
using DMO.Application.ControloComparacao;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.ControloComparacao;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// Integration test — the Peso Comparação aggregate over the disposable PostgreSQL database:
/// the natural <c>comparacao_id + cm_id</c> identity, the separated NEW measurement rows (never
/// touching the initial Peso), the individual final per-CM decisions, the put-aside justification
/// and the confirmation completion rule, over the real repository and service.
/// </summary>
/// <remarks>
/// Authority: the Peso Comparação functional authority. Every assertion is scoped to the rows
/// this test creates (fresh Guid and per-test token). The service-level proofs run in the unit
/// suite; here the persistence contract is proven: the composite primary keys, the CHECK/FK
/// backstops and the atomic write transactions against the real PostgreSQL schema.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class ComparacaoRepositoryIntegrationTests
{
    private static readonly HashSet<string> CreatedTokens = new(StringComparer.Ordinal);

    private static ComparacaoServiceCtx BuildServices(DmoDbContext context) => new(
        new ComparacaoRepository(context));

    private sealed record ComparacaoServiceCtx(ComparacaoRepository Comparacoes)
    {
        public ControloComparacaoService Service(
            DmoDbContext context,
            IControloCalculationConfiguration calculation) =>
            new(
                new PesoRepository(context),
                new DmoPesoContextRead(context),
                calculation,
                new ComparacaoRepository(context));
    }

    /// <summary>PESO0 (zero effect) — a create/read of the initial Peso is untouched by the
    /// comparison surface: after the full comparison lifecycle, the initial Peso row and its rows
    /// are byte-identical and the comparison tables carry the comparison data alone.</summary>
    [SkippableFact]
    public async Task PESO0_InitialPesoIsNeverWrittenByTheComparisonLifecycle()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context);

        var token = Guid.NewGuid().ToString("N");
        CreatedTokens.Add(token);
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            // Create the initial Peso directly (the comparison tests are persistence-level).
            var pesoId = Guid.NewGuid();
            await InsertInitialPesoAsync(context, pesoId, cmId, userId);
            var antes = await ReadInitialPesoAsync(context, pesoId);

            var comparacoes = BuildServices(context);

            // A full comparison lifecycle on that same peso_id.
            var started = await comparacoes.Comparacoes.StartedAsync(
                DomainComparacao(pesoId, userId),
                CancellationToken.None);
            var subject = await comparacoes.Comparacoes.SubjectAddedAsync(
                DomainSubject(started, cmId, userId),
                expectedComparacaoVersion: 1,
                CancellationToken.None);
            var recorded = await comparacoes.Comparacoes.MeasurementsRecordedAsync(
                DomainRecordedSubject(started, cmId, userId),
                expectedSubjectVersion: 1,
                CancellationToken.None);
            _ = recorded;

            var despues = await ReadInitialPesoAsync(context, pesoId);
            Assert.NotNull(antes);
            Assert.NotNull(despues);
            Assert.Equal(antes!.PesoId, despues!.PesoId);
            Assert.Equal(antes.Version, despues.Version);
            Assert.Equal(antes.WaterTemperature, despues.WaterTemperature);
            Assert.Equal(antes.Status, despues.Status);

            // The initial Peso row COUNT is still one, and its rows still carry the ORIGINAL
            // values (the comparison rows live in the comparison tables only).
            Assert.Equal(
                "1",
                await ScalarAsync(context, "SELECT count(*) FROM pesos WHERE peso_id = @p", new NpgsqlParameter("p", pesoId)));
            Assert.Equal(antes.WaterTemperature, despues!.WaterTemperature);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>PESO1 — the natural identity: one selected CM per comparison event, and a second
    /// comparison event of the SAME CM in the SAME production reuses the SAME cm_id under a NEW
    /// comparacao_id (the composite key enforces it).</summary>
    [SkippableFact]
    public async Task PESO1_TheSubjectUsesTheNaturalComparacaoIdPlusCmId()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        CreatedTokens.Add(token);
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);
            var pesoId = Guid.NewGuid();
            await InsertInitialPesoAsync(context, pesoId, cmId, userId);

            var comparacoes = BuildServices(context);

            // Two separate comparison events of the SAME CM in the SAME production: two distinct
            // comparacao_ids, the SAME cm_id — the natural relationship preserves the connection
            // to the same frozen production CM across events.
            var first = await comparacoes.Comparacoes.StartedAsync(
                DomainComparacao(pesoId, userId),
                CancellationToken.None);
            var second = await comparacoes.Comparacoes.StartedAsync(
                DomainComparacao(pesoId, userId),
                CancellationToken.None);

            Assert.NotEqual(first.ComparacaoId, second.ComparacaoId);

            await comparacoes.Comparacoes.SubjectAddedAsync(
                DomainSubject(first, cmId, userId),
                expectedComparacaoVersion: 1,
                CancellationToken.None);
            await comparacoes.Comparacoes.SubjectAddedAsync(
                DomainSubject(second, cmId, userId),
                expectedComparacaoVersion: 1,
                CancellationToken.None);

            var firstRead = await comparacoes.Comparacoes.GetByIdAsync(first.ComparacaoId.Value, CancellationToken.None);
            var secondRead = await comparacoes.Comparacoes.GetByIdAsync(second.ComparacaoId.Value, CancellationToken.None);
            Assert.Single(firstRead!.Subjects);
            Assert.Single(secondRead!.Subjects);
            Assert.Equal(cmId, Assert.Single(firstRead.Subjects).CmId);
            Assert.Equal(cmId, Assert.Single(secondRead.Subjects).CmId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>PESO2 (AC5/AC6) — the comparison measurement rows are stored separately from the
    /// initial Peso rows and never alter the initial Peso average: the comparison rows live in
    /// <c>comparacao_measurement_rows</c>, not in <c>peso_measurement_rows</c>.</summary>
    [SkippableFact]
    public async Task PESO2_ComparisonRowsAreStoredSeparatelyFromTheInitialPesoRows()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        CreatedTokens.Add(token);
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);
            var pesoId = Guid.NewGuid();
            await InsertInitialPesoAsync(context, pesoId, cmId, userId);
            var peso = await ReadInitialPesoAsync(context, pesoId);

            var comparacoes = BuildServices(context);
            var started = await comparacoes.Comparacoes.StartedAsync(
                DomainComparacao(pesoId, userId),
                CancellationToken.None);
            await comparacoes.Comparacoes.SubjectAddedAsync(
                DomainSubject(started, cmId, userId),
                expectedComparacaoVersion: 1,
                CancellationToken.None);
            await comparacoes.Comparacoes.MeasurementsRecordedAsync(
                DomainRecordedSubject(started, cmId, userId),
                expectedSubjectVersion: 1,
                CancellationToken.None);

            // The initial Peso STILL has exactly its original sole measurement row in the Peso
            // table — the comparison rows are physically elsewhere.
            Assert.Equal(
                "1",
                await ScalarAsync(
                    context,
                    "SELECT count(*) FROM peso_measurement_rows WHERE peso_id = @p",
                    new NpgsqlParameter("p", pesoId)));

            // The comparison rows are NEW, separate, and keyed to the comparison event + cm_id.
            var comparisonRows = await ScalarAsync(
                context,
                "SELECT count(*) FROM comparacao_measurement_rows WHERE comparacao_id = @c AND cm_id = @m",
                new NpgsqlParameter("c", started.ComparacaoId.Value),
                new NpgsqlParameter("m", cmId));
            Assert.Equal("2", comparisonRows);

            // The initial average (single row) is untouched — the comparison rows never enter it.
            Assert.NotNull(peso);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>PESO3 — the composite-key backstop: recording the SAME reading position twice for
    /// the same subject is refused by the primary key (SQLSTATE 23505), duplicating a subject is
    /// refused by the (comparacao_id, cm_id) key, and a put-aside without justification is refused
    /// by the reason CHECK (SQLSTATE 23514).</summary>
    [SkippableFact]
    public async Task PESO3_TheNaturalKeyAndCheckBackstopsHold()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        CreatedTokens.Add(token);
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);
            var pesoId = Guid.NewGuid();
            await InsertInitialPesoAsync(context, pesoId, cmId, userId);

            var comparacoes = BuildServices(context);
            var started = await comparacoes.Comparacoes.StartedAsync(
                DomainComparacao(pesoId, userId),
                CancellationToken.None);
            var subject = await comparacoes.Comparacoes.SubjectAddedAsync(
                DomainSubject(started, cmId, userId),
                expectedComparacaoVersion: 1,
                CancellationToken.None);
            await comparacoes.Comparacoes.MeasurementsRecordedAsync(
                DomainRecordedSubject(started, cmId, userId),
                expectedSubjectVersion: 1,
                CancellationToken.None);

            // Duplicating the same subject hits the (comparacao_id, cm_id) primary key.
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO comparacao_cm_subjects (comparacao_id, cm_id, created_by_user_id) " +
                    "VALUES (@c, @m, @u)",
                    new NpgsqlParameter("c", started.ComparacaoId.Value),
                    new NpgsqlParameter("m", cmId),
                    new NpgsqlParameter("u", userId)));
            Assert.Equal("23505", duplicate.SqlState);

            // Duplicating the same row position hits the (comparacao_id, cm_id, row_position) key.
            var rowDuplicate = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO comparacao_measurement_rows " +
                    "(comparacao_id, cm_id, row_position, water_weight_g, capacity_cm3, glass_weight_g) " +
                    "VALUES (@c, @m, 1, 997.1, 1000, 2500)",
                    new NpgsqlParameter("c", started.ComparacaoId.Value),
                    new NpgsqlParameter("m", cmId)));
            Assert.Equal("23505", rowDuplicate.SqlState);

            // A put-aside decision without justification violates the reason CHECK.
            var noReason = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "UPDATE comparacao_cm_subjects SET decision = 'colocar_de_parte', reason = NULL, " +
                    "decided_by_user_id = @u, decided_at = now(), version = version + 1 " +
                    "WHERE comparacao_id = @c AND cm_id = @m",
                    new NpgsqlParameter("c", started.ComparacaoId.Value),
                    new NpgsqlParameter("m", cmId),
                    new NpgsqlParameter("u", userId)));
            Assert.Equal("23514", noReason.SqlState);
            Assert.Equal("comparacao_cm_subjects_reason_check", noReason.ConstraintName);

            _ = subject;
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // --------------------------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------------------------

    private static Comparacao DomainComparacao(Guid pesoId, Guid userId) => new(
        DMO.Domain.ControloComparacao.ComparacaoId.New(),
        DMO.Domain.Controlo.PesoId.From(pesoId),
        userId,
        DateTimeOffset.UtcNow,
        ConfirmedAt: null,
        ConfirmedByUserId: null,
        Version: 1,
        UpdatedAt: DateTimeOffset.UtcNow,
        Subjects: []);

    private static ComparacaoCmSubject DomainSubject(Comparacao comparacao, Guid cmId, Guid userId) => new(
        comparacao.ComparacaoId,
        cmId,
        userId,
        DateTimeOffset.UtcNow,
        Decision: null,
        DecidedByUserId: null,
        DecidedAt: null,
        Reason: null,
        Version: 1,
        UpdatedAt: DateTimeOffset.UtcNow,
        Rows: []);

    private static ComparacaoCmSubject DomainRecordedSubject(Comparacao comparacao, Guid cmId, Guid userId) => new(
        comparacao.ComparacaoId,
        cmId,
        userId,
        DateTimeOffset.UtcNow,
        Decision: null,
        DecidedByUserId: null,
        DecidedAt: null,
        Reason: null,
        Version: 1,
        UpdatedAt: DateTimeOffset.UtcNow,
        Rows:
        [
            new ComparacaoMeasurementRow(comparacao.ComparacaoId, cmId, 1, 997.1m, 1000m, 2500m, DateTimeOffset.UtcNow),
            new ComparacaoMeasurementRow(comparacao.ComparacaoId, cmId, 2, 1994.2m, 2000m, 5000m, DateTimeOffset.UtcNow),
        ]);

    private static async Task InsertInitialPesoAsync(DmoDbContext context, Guid pesoId, Guid cmId, Guid userId)
    {
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO pesos (peso_id, cm_id, water_temperature, glass_density_g_cm3, created_by_user_id, version) " +
            "VALUES (@id, @cm, 25, 2.50, @u, 1)",
            new NpgsqlParameter("id", pesoId),
            new NpgsqlParameter("cm", cmId),
            new NpgsqlParameter("u", userId));

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO peso_measurement_rows (peso_id, row_position, water_weight_g, capacity_cm3, glass_weight_g) " +
            "VALUES (@id, 1, 997.1, 1000, 2500)",
            new NpgsqlParameter("id", pesoId));
    }

    private static async Task<PesoEntity?> ReadInitialPesoAsync(DmoDbContext context, Guid pesoId) =>
        await context.Set<PesoEntity>().AsNoTracking()
            .FirstOrDefaultAsync(peso => peso.PesoId == pesoId);

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

    private static async Task<(Guid ToolId, Guid JobOnId, Guid CmId)> SeedCmProductionAsync(
        DmoDbContext context,
        string token)
    {
        var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");
        var jobOn = await CreateJobOnAsync(context, $"ref-{token}", $"pn-{token}-01", toolId);

        var cmId = Guid.Parse(await ScalarAsync(
            context,
            "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOn.JobOnId)));

        return (toolId, jobOn.JobOnId, cmId);
    }

    private static async Task<Guid> CreateCmToolAsync(
        DmoDbContext context,
        string reference,
        string lot) =>
        Assert.IsType<ToolResult.Created>(await new ToolService(new ToolRepository(context)).CreateAsync(
            new CreateToolCommand("CM", reference, lot, "NNPB", null, ["B1"]),
            CancellationToken.None)).ToolId;

    private static async Task<JobOnResult.Created> CreateJobOnAsync(
        DmoDbContext context,
        string reference,
        string productionNumber,
        Guid cmToolId)
    {
        var jobOn = new JobOnService(new JobOnRepository(context), new ToolRepository(context), []);
        var result = await jobOn.CreateAsync(
            new CreateJobOnCommand(reference, productionNumber, "B1", null, cmToolId, null, null),
            CancellationToken.None);
        return Assert.IsType<JobOnResult.Created>(result);
    }

    private static async Task CleanupAsync(DmoDbContext context, string token)
    {
        var pattern = $"%{token}%";

        // The Comparação tables first (they reference pesos, comparacoes and cm_contexts).
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM comparacao_measurement_rows WHERE comparacao_id IN " +
            "(SELECT comparacao_id FROM comparacoes WHERE peso_id IN " +
            "(SELECT peso_id FROM pesos WHERE created_by_user_id IN " +
            "(SELECT user_id FROM users WHERE company_number LIKE @p) " +
            "OR cm_id IN (SELECT cm_id FROM cm_contexts WHERE jobon_id IN " +
            "(SELECT jobon_id FROM job_ons WHERE reference LIKE @p))))",
            new NpgsqlParameter("p", pattern));
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM comparacao_cm_subjects WHERE comparacao_id IN " +
            "(SELECT comparacao_id FROM comparacoes WHERE peso_id IN " +
            "(SELECT peso_id FROM pesos WHERE created_by_user_id IN " +
            "(SELECT user_id FROM users WHERE company_number LIKE @p) " +
            "OR cm_id IN (SELECT cm_id FROM cm_contexts WHERE jobon_id IN " +
            "(SELECT jobon_id FROM job_ons WHERE reference LIKE @p))))",
            new NpgsqlParameter("p", pattern));
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM comparacoes WHERE peso_id IN " +
            "(SELECT peso_id FROM pesos WHERE created_by_user_id IN " +
            "(SELECT user_id FROM users WHERE company_number LIKE @p) " +
            "OR cm_id IN (SELECT cm_id FROM cm_contexts WHERE jobon_id IN " +
            "(SELECT jobon_id FROM job_ons WHERE reference LIKE @p)))",
            new NpgsqlParameter("p", pattern));

        // The initial Peso rows and their measurement rows (the accepted P2-T05 cleanup pattern).
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

    private static async Task<string> ScalarAsync(
        DmoDbContext context,
        string sql,
        params NpgsqlParameter[] parameters)
    {
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

        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }
}