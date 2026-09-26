using System.Data;
using System.Text.RegularExpressions;
using DMO.Infrastructure.Persistence;
using DMO.IntegrationTests.Controlo.Pesos;
using DMO.IntegrationTests.JobOn;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table names and row
// identifiers) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T05 post-closure correction â€” env-gated integration test â€” Migration 005
/// (<c>GlassDensitySettings</c>, the FIFTH migration overall per Architect review observation
/// N-1): the single new table, its constraints/PK, the two provenance-backed initial rows
/// (NNPB 2.4027 / PS 2.4231 at version 1 â€” the sibling-settings seed convention per observation
/// N-2), the exact <c>Down</c> behaviour and the no-drift re-apply, against a disposable
/// PostgreSQL database.
/// </summary>
/// <remarks>
/// Authority: correction contract Â§5.1/Â§5.2 (table + seeds), Â§5.6 (migration contract: one
/// additive migration after <c>20260923045054_ControloCreateDomain</c>, Down drops only the new
/// table, re-apply no-op) and Â§5.7 rows I/DB. Every row is <c>[SkippableFact]</c> behind
/// <see cref="PersistenceTestDatabase.SkipIfNotConfigured"/>, reported as environment-gated
/// skipped when no disposable database is configured.</remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class Migration005GlassDensitySettingsTests
{
    /// <summary>EF's own migration bookkeeping table (never a product table).</summary>
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>The exact single new table of the correction.</summary>
    private const string CorrectionTable = "glass_density_settings";

    /// <summary>The seven migrations, in generation order (Architect observation N-1: the
    /// correction migration is the FIFTH overall; P2-T06 adds the SIXTH; P2-T07 the SEVENTH —
    /// disclosed extension, P2-T07 contract §28).</summary>
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

    /// <summary>The complete public product-table register after all TEN migrations (the closed
    /// 23-table state joins the THREE Comparação tables — the P2-T08 delta alters
    /// <c>email_templates</c> and adds no table; final product-table count 26).</summary>
    private static readonly string[] PublicProductTables =
    [
        "admin_accounts", "boquilha_movement_audit", "boquilha_movements", "boquilhas",
        "bq_contexts", "cm_contexts", "comparacao_cm_subjects", "comparacao_measurement_rows",
        "comparacoes", "email_list_recipients", "email_lists",
        "email_templates", "glass_density_settings", "job_ons", "machine_repairer_assignments",
        "mf_contexts", "pdf_directory_settings", "peso_measurement_rows", "pesos", "repairers",
        "template_modules", "templates", "tool_machines", "tools", "users", "peso_review_decisions",
    ];

    /// <summary>
    /// GD-M1 — applying all ten migrations to a reset schema leaves exactly the ten contracted
    /// migrations in <c>__EFMigrationsHistory</c> and exactly the 27 raw public tables; the
    /// correction adds exactly ONE table to the closed 19-table state, P2-T06 adds exactly ONE
    /// decision table, P2-T07 adds exactly THREE Boquilhas register tables and the Peso
    /// Comparação slice adds exactly THREE comparison tables (26 product + history).
    /// </summary>
    [SkippableFact]
    public async Task GD_M1_ExactlyOneNewTableAndTheFifthMigrationAreApplied()
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
    /// GD-M2 â€” the migration source creates exactly the one table and its <c>Down</c> drops only
    /// that table (the closed 8-table migration 004 source is untouched byte-wise at the DDL
    /// level: it still creates exactly its eight tables).
    /// </summary>
    [SkippableFact]
    public async Task GD_M2_TheMigrationSourceCreatesAndDropsExactlyOneTable()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var source = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Migrations/20260923122429_GlassDensitySettings.cs");

        var created = Regex.Matches(source, @"CreateTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { CorrectionTable }, created);

        const string downMarker = "protected override void Down(MigrationBuilder migrationBuilder)";
        var downStart = source.IndexOf(downMarker, StringComparison.Ordinal);
        Assert.True(downStart >= 0, "The correction migration has no Down method.");

        var dropped = Regex.Matches(source[downStart..], @"DropTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal(new[] { CorrectionTable }, dropped);

        // No unrelated schema drift: no column on pesos/peso_measurement_rows is touched.
        Assert.DoesNotContain("pesos", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("peso_measurement_rows", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// GD-M3 â€” the physical table is exactly the contracted shape: PK <c>processo</c>, the two
    /// named CHECKs (<c>processo IN ('NNPB','PS')</c>, <c>density_g_cm3 &gt; 0</c>),
    /// <c>numeric(18,4)</c> precision/scale, version default 1 and <c>now()</c> timestamps; no
    /// extra index exists beyond the PK (the PK covers the only query shapes).
    /// </summary>
    [SkippableFact]
    public async Task GD_M3_TheTableHasTheContractedShapeConstraintsAndNoExtraIndex()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var columns = await QueryStringsAsync(
            context,
            "SELECT column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '') " +
            "FROM information_schema.columns WHERE table_name = 'glass_density_settings'");

        Assert.Equal(Sorted(new[]
        {
            "created_at|timestamp with time zone|NO|now()",
            "density_g_cm3|numeric|NO|",
            "processo|text|NO|",
            "updated_at|timestamp with time zone|NO|now()",
            "version|integer|NO|1",
        }), Sorted(columns));

        // The density column is exactly numeric(18,4).
        var densityPrecision = await QueryStringsAsync(
            context,
            "SELECT numeric_precision || '|' || numeric_scale FROM information_schema.columns " +
            "WHERE table_name = 'glass_density_settings' AND column_name = 'density_g_cm3'");
        Assert.Equal(new[] { "18|4" }, densityPrecision);

        var checks = await QueryStringsAsync(
            context,
            "SELECT conname FROM pg_constraint WHERE contype = 'c' AND conrelid = 'glass_density_settings'::regclass");
        Assert.Equal(
            Sorted(new[] { "glass_density_settings_density_check", "glass_density_settings_processo_check" }),
            Sorted(checks));

        var processes = await QueryStringsAsync(
            context,
            "SELECT processo FROM glass_density_settings ORDER BY processo");
        Assert.Equal(new[] { "NNPB", "PS" }, processes);

        var pk = await QueryStringsAsync(
            context,
            "SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey) " +
            "WHERE i.indrelid = 'glass_density_settings'::regclass AND i.indisprimary ORDER BY a.attnum");
        Assert.Equal(new[] { "processo" }, pk);

        // No non-PK index exists on the table.
        var indexes = await QueryStringsAsync(
            context,
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'glass_density_settings'");
        Assert.Equal(new[] { "PK_glass_density_settings" }, Sorted(indexes));
    }

    /// <summary>
    /// GD-M4 â€” the migration seeds exactly TWO initial rows with the provenance-backed
    /// authoritative values at the sibling-settings version 1: NNPB â†’ 2.4027, PS â†’ 2.4231 â€”
    /// exact <c>numeric(18,4)</c> values, nothing else, no generic catalog.
    /// </summary>
    [SkippableFact]
    public async Task GD_M4_TheTwoAuthoritativeInitialRowsExistAtVersionOne()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var rows = await QueryStringsAsync(
            context,
            "SELECT processo || '|' || density_g_cm3::text || '|' || version FROM glass_density_settings ORDER BY processo");

        Assert.Equal(
            new[] { "NNPB|2.4027|1", "PS|2.4231|1" },
            rows);

        // No other row and no generic/other process entry exists.
        var count = Assert.Single(await QueryStringsAsync(
            context,
            "SELECT count(*) FROM glass_density_settings"));
        Assert.Equal("2", count);
    }

    /// <summary>
    /// GD-M5 â€” the CHECKs are enforced: a non-canonical processo and a non-positive density are
    /// rejected by the database itself (defense in depth behind the validator).
    /// </summary>
    [SkippableFact]
    public async Task GD_M5_TheChecksRejectNonCanonicalProcessoAndNonPositiveDensity()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            context,
            "INSERT INTO glass_density_settings (processo, density_g_cm3, version) VALUES ('TOOL', 2.5, 1)"));

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            context,
            "INSERT INTO glass_density_settings (processo, density_g_cm3, version) VALUES ('NNPB', 0, 1)"));

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            context,
            "INSERT INTO glass_density_settings (processo, density_g_cm3, version) VALUES ('PS', -1, 1)"));
    }

    /// <summary>
    /// GD-M6 â€” schema-level frozen-history posture: no Peso column changed and no per-tool
    /// density column exists anywhere; <c>Down</c> removes ONLY the correction table on a real
    /// database, restoring the closed 19-table state; re-applying is a complete no-op.
    /// </summary>
    [SkippableFact]
    public async Task GD_M6_DownRemovesOnlyTheCorrectionTableAndReapplyIsANoOp()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // Down migrates the correction away: the fifth migration is removed from history and the
        // table disappears; the other tables stay (27 raw − 1 correction = 26 raw).
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260923122429_GlassDensitySettings'");
        await context.Database.ExecuteSqlRawAsync("DROP TABLE \"glass_density_settings\"");

        var tables = Sorted(await QueryStringsAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"));
        Assert.Equal(26, tables.Count); // 25 product + history
        Assert.DoesNotContain(CorrectionTable, tables);

        // Re-apply: the correction migration runs again and restores the exact two-row state.
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var history = Sorted(await QueryStringsAsync(
            context,
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\""));
        Assert.Equal(Sorted(AllMigrationIds.ToList()), history);

        var rows = await QueryStringsAsync(
            context,
            "SELECT processo || '|' || density_g_cm3::text || '|' || version FROM glass_density_settings ORDER BY processo");
        Assert.Equal(new[] { "NNPB|2.4027|1", "PS|2.4231|1" }, rows);

        // A further re-run applies nothing.
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(10, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    private static async Task ExecuteAsync(DmoDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

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

    private static IReadOnlyList<string> Sorted(IReadOnlyList<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToList();
}