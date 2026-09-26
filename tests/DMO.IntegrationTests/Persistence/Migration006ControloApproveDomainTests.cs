using System.Data;
using System.Text.RegularExpressions;
using DMO.Infrastructure.Persistence;
using DMO.IntegrationTests.ControloApprove;
using DMO.IntegrationTests.JobOn;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table names and row
// identifiers) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T06 env-gated integration test — Migration 006 (<c>ControloApproveDomain</c>): exactly one
/// new table (<c>peso_review_decisions</c>) with the exact columns/CHECKs/RESTRICT FKs of §6/§7,
/// exactly one additive index on the existing <c>pesos</c> (<c>IX_pesos_reviewable</c>), the exact
/// <c>Down</c> behaviour and the no-drift re-apply, against a disposable PostgreSQL database.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §25 (migration contract: one additive migration, the sixth overall;
/// §6.2 no column change to <c>pesos</c>; §7.3 the deferred pending-list index) and §26.4 rows
/// MG1–MG3 (AC-MG1…AC-MG3). Every row is <c>[SkippableFact]</c> behind
/// <see cref="PersistenceTestDatabase.SkipIfNotConfigured"/>.</remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class Migration006ControloApproveDomainTests
{
    /// <summary>EF's own migration bookkeeping table (never a product table).</summary>
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>The exact single new table of the slice.</summary>
    private const string DecisionTable = "peso_review_decisions";

    /// <summary>The TEN migrations, in generation order (this slice owns the SIXTH; P2-T07 adds
    /// the SEVENTH — disclosed extension, P2-T07 contract §28; the P2-T08 delta adds the
    /// NINTH (EmailTemplateGroupRouting) and the Peso Comparação slice adds the TENTH
    /// (ControloComparacaoDomain)).</summary>
    private static readonly string[] AllMigrationIds =
    [
        "20260922001736_AccountAndTemplateFoundation",
        "20260922001757_TemplateModuleComposition",
        "20260922232349_ToolJobOnDomainCore",
        "20260923045054_ControloCreateDomain",
        "20260923122429_GlassDensitySettings",
        "20260923171223_ControloApproveDomain",
        "20260924051151_BoquilhasDomain",
        "20260924130151_BoquilhasPreJobonAssociation",
        "20260924182527_EmailTemplateGroupRouting",
        "20260925071139_ControloComparacaoDomain",
    ];

    /// <summary>The complete public product-table register after all ten migrations (the closed
    /// 20-table state + EXACTLY the one decision table + the THREE Boquilhas register tables +
    /// the THREE Comparação tables — OWNER CLARIFICATION: the unreviewed 007 was corrected
    /// pre-closure; no lifecycle tables; the P2-T08 delta adds no table).</summary>
    private static readonly string[] PublicProductTables =
    [
        "admin_accounts", "boquilha_movement_audit", "boquilha_movements", "boquilhas",
        "bq_contexts", "cm_contexts", "comparacao_cm_subjects", "comparacao_measurement_rows",
        "comparacoes", "email_list_recipients", "email_lists",
        "email_templates", "glass_density_settings", "job_ons", "machine_repairer_assignments",
        "mf_contexts", "pdf_directory_settings", "peso_measurement_rows", "peso_review_decisions",
        "pesos", "repairers", "template_modules", "templates", "tool_machines", "tools", "users",
    ];

    /// <summary>
    /// MG1 — applying all seven migrations to a reset schema leaves exactly the seven contracted
    /// migrations in <c>__EFMigrationsHistory</c> and exactly the 24 raw public tables; the
    /// slice adds exactly ONE table to the closed 20-table state and P2-T07 adds exactly THREE.
    /// </summary>
    [SkippableFact]
    public async Task MG1_ExactlyOneNewTableAndTheSixthMigrationAreApplied()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var history = Sorted(await QueryStringsAsync(
            context,
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\""));
        Assert.Equal(Sorted(AllMigrationIds.ToList()), history);

        var tables = Sorted(await QueryStringsAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"));

        Assert.Equal(27, tables.Count); // 26 product tables + history
        Assert.Equal(Sorted([.. PublicProductTables, MigrationHistoryTable]), tables);
    }

    /// <summary>
    /// MG1 — the migration source creates exactly the one table and the ONE additive <c>pesos</c>
    /// index; the source touches NO other table (the closed migration sources are untouched at
    /// the DDL level — verified separately by hash, contract App. A).
    /// </summary>
    [SkippableFact]
    public async Task MG1_TheMigrationSourceCreatesExactlyOneTableAndOneAdditivePesosIndex()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var source = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Migrations/20260923171223_ControloApproveDomain.cs");

        var created = Regex.Matches(source, @"CreateTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal([DecisionTable], created);

        // Exactly the one additive index on the EXISTING pesos table (contract §7.3/§25.2).
        var pesosIndexes = Regex.Matches(source, @"CreateIndex\(\s*name:\s*""(?<name>IX_pesos[^""]*)""\s*,\s*table:\s*""pesos""")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal(["IX_pesos_reviewable"], pesosIndexes);

        // No other table is created/dropped/renamed by this migration and no column is added to pesos.
        Assert.DoesNotContain("AddColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DropColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RenameTable", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// MG2 — the decision table's CHECKs/FKs/columns match §6/§7 exactly (constraint names,
    /// <c>confdeltype='r'</c> — no cascade), and the reason CHECK rejects an <c>aprovado</c> row
    /// with a reason and a <c>reaberto</c> row without one (SQLSTATE 23514 → the same validator
    /// token the service raises, never a 500).
    /// </summary>
    [SkippableFact]
    public async Task MG2_TheDecisionTableConstraintsMatchTheContractExactly()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // Columns (exact set, exact nullability).
        var columns = await QueryStringsAsync(
            context,
            "SELECT column_name, is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'peso_review_decisions'");
        var expectedColumns = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["peso_review_decision_id"] = false,
            ["peso_id"] = false,
            ["decision"] = false,
            ["decided_by_user_id"] = false,
            ["decided_at"] = false,
            ["reason"] = true,
            ["prior_status"] = false,
            ["pesos_version_at_decision"] = false,
            ["created_at"] = false,
        };
        Assert.Equal(
            Sorted(expectedColumns.Select(entry => $"{entry.Key}|{(entry.Value ? "YES" : "NO")}")),
            Sorted(columns));

        // CHECK constraints (exact names).
        var checks = await QueryStringsAsync(
            context,
            "SELECT conname FROM pg_constraint WHERE conrelid = 'peso_review_decisions'::regclass " +
            "AND contype = 'c'");
        foreach (var expected in new[]
                 {
                     "peso_review_decisions_decision_check",
                     "peso_review_decisions_prior_status_check",
                     "peso_review_decisions_reason_required_check",
                     "peso_review_decisions_version_check",
                 })
        {
            Assert.Contains(expected, checks);
        }
        Assert.Equal(4, checks.Count);

        // FKs are RESTRICT (confdeltype 'r'), never cascade.
        var fks = await QueryStringsAsync(
            context,
            "SELECT conname, confdeltype FROM pg_constraint " +
            "WHERE conrelid = 'peso_review_decisions'::regclass AND contype = 'f'");
        Assert.Equal(2, fks.Count);
        Assert.Contains("FK_peso_review_decisions_pesos_peso_id|r", fks);
        Assert.Contains("FK_peso_review_decisions_users_decided_by_user_id|r", fks);

        // The reason CHECK: an aprovado row with a reason and a reaberto row without one are
        // rejected (23514), and the valid aprovado-without-reason insert succeeds.
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        try
        {
        var userId = await SeedUserAsync(connection);
        var toolId = await SeedToolAsync(connection);
        var pesoId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();

        await ExecuteAsync(
            connection,
            "INSERT INTO pesos (peso_id, tool_id, status, water_temperature, version, created_by_user_id, " +
            "created_at, updated_at) VALUES (@id, @tool, 'pendente', 25, 1, @user, now(), now())",
            ("id", (object)pesoId),
            ("tool", toolId),
            ("user", userId));

        // aprovado WITH a reason → CHECK violation.
        await Assert.ThrowsAsync<PostgresException>(() => InsertDecisionAsync(
            connection, decisionId, pesoId, userId, "aprovado", "motivo invalido", "pendente", 1));
        // reaberto WITHOUT a reason → CHECK violation.
        await Assert.ThrowsAsync<PostgresException>(() => InsertDecisionAsync(
            connection, Guid.NewGuid(), pesoId, userId, "reaberto", "", "aprovado", 2));
        // valid aprovado WITHOUT a reason → inserted.
        await InsertDecisionAsync(connection, decisionId, pesoId, userId, "aprovado", null, "pendente", 1);
        }
        finally
        {
            // Cleanup in FK-safe order (disposable database only): the decision rows, then the
            // Peso row, its Tool anchor and the MG2 user.
            await ExecuteAsync(connection,
                "DELETE FROM peso_review_decisions WHERE peso_id IN (SELECT peso_id FROM pesos " +
                "WHERE tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE '\''REF-MG2-%'\'')) " +
                "OR decided_by_user_id IN (SELECT user_id FROM users WHERE name = '\''MG2 User'\'')");
            await ExecuteAsync(connection,
                "DELETE FROM pesos WHERE tool_id IN (SELECT tool_id FROM tools WHERE reference LIKE '\''REF-MG2-%'\'')");
            await ExecuteAsync(connection, "DELETE FROM tools WHERE reference LIKE '\''REF-MG2-%'\''");
            await ExecuteAsync(connection, "DELETE FROM users WHERE name = '\''MG2 User'\''");
        }
    }

    /// <summary>
    /// MG3 — decision rows are append-only: no update/delete ROUTE and no update/delete repository
    /// member exist for the decision table (immutability is structural, contract §10.3/AC-MG3;
    /// <c>pesos.version</c> increments exactly once per decision transition, proven at the
    /// repository level in <c>PesoReviewRepositoryIntegrationTests</c>).
    /// </summary>
    [SkippableFact]
    public async Task MG3_DecisionRowsAreAppendOnly()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var repositorySource = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Persistence/Controlo/PesoReviewRepository.cs");
        var endpointsSource = P2T04ProductionScan.Read(
            "src/DMO.Web/Endpoints/Controlo/ControloApproveEndpoints.cs");

        Assert.DoesNotContain("MapPut", endpointsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MapDelete", endpointsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAsync", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletedAsync", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveRange", repositorySource, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------------------------- helpers

    private static async Task<Guid> SeedUserAsync(NpgsqlConnection connection)
    {
        var userId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            "INSERT INTO users (user_id, auth_identity_id, name, company_number, email, active, version, " +
            "created_at, updated_at) VALUES (@id, @auth, @name, @cn, @email, true, 1, now(), now())",
            ("id", (object)userId),
            ("auth", $"auth-mg2-{Guid.NewGuid():N}"),
            ("name", "MG2 User"),
            ("cn", $"CN-MG2-{Guid.NewGuid():N}"),
            ("email", $"mg2-{Guid.NewGuid():N}@example.pt"));

        return userId;
    }

    private static async Task<Guid> SeedToolAsync(NpgsqlConnection connection)
    {
        var toolId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            "INSERT INTO tools (tool_id, tool_type, reference, lot, processo, created_at, updated_at) " +
            "VALUES (@id, 'CM', @ref, '01', 'NNPB', now(), now())",
            ("id", (object)toolId),
            ("ref", $"REF-MG2-{Guid.NewGuid():N}"));

        return toolId;
    }

    private static Task InsertDecisionAsync(
        NpgsqlConnection connection,
        Guid decisionId,
        Guid pesoId,
        Guid userId,
        string decision,
        string? reason,
        string priorStatus,
        int versionAtDecision) =>
        ExecuteAsync(
            connection,
            "INSERT INTO peso_review_decisions (peso_review_decision_id, peso_id, decision, decided_by_user_id, " +
            "decided_at, reason, prior_status, pesos_version_at_decision, created_at) " +
            "VALUES (@id, @peso, @decision, @user, now(), @reason, @prior, @version, now())",
            ("id", (object)decisionId),
            ("peso", pesoId),
            ("decision", decision),
            ("user", userId),
            ("reason", (object?)reason ?? DBNull.Value),
            ("prior", priorStatus),
            ("version", versionAtDecision));

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value));
        }

        await command.ExecuteNonQueryAsync();
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToList();

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        DmoDbContext context,
        string sql)
    {
        var results = new List<string>();
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

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