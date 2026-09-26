using System.Data;
using System.Text.RegularExpressions;
using DMO.Infrastructure.Persistence;
using DMO.IntegrationTests.JobOn;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table names and row
// identifiers) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// Integration test — Migration 009 (<c>ControloComparacaoDomain</c>): the Peso Comparação
/// aggregate over its OWN three tables, with the exact columns/CHECKs/RESTRICT FKs and the
/// natural composite primary keys, against a disposable PostgreSQL database.
/// </summary>
/// <remarks>
/// Authority: the Peso Comparação functional authority (identity model, persistence, decision
/// model). The compared CM identity is the natural <c>comparacao_id + cm_id</c> (one subject per
/// selected CM inside one comparison event); measurement rows use the natural
/// <c>(comparacao_id, cm_id, row_position)</c> primary key (no reading UUID); FKs are RESTRICT;
/// the initial <c>pesos</c> table is untouched by this migration. Every row is
/// <c>[SkippableFact]</c> behind <see cref="PersistenceTestDatabase.SkipIfNotConfigured"/>.
/// The migration is the TENTH overall (the disclosed P2-T08 email-routing delta 009 already
/// exists, see the implementation response).
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class Migration009ControloComparacaoDomainTests
{
    /// <summary>EF's own migration bookkeeping table (never a product table).</summary>
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>The three NEW tables of this slice, in ordinal order.</summary>
    private static readonly string[] NewTables =
    [
        "comparacoes",
        "comparacao_cm_subjects",
        "comparacao_measurement_rows",
    ];

    /// <summary>The TEN migrations, in generation order (this slice owns the TENTH; the disclosed
    /// P2-T08 email-routing migration 009 exists between it and the P2-T07 Boquilhas pair).</summary>
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

    /// <summary>The complete public product-table register after all TEN migrations (23 closed
    /// product tables on this baseline + the THREE Comparação tables = 26 product tables; the
    /// email-routing migration 009 alters <c>email_templates</c> and adds no table).</summary>
    private static readonly string[] PublicProductTables =
    [
        "admin_accounts", "boquilha_movement_audit", "boquilha_movements", "boquilhas",
        "bq_contexts", "cm_contexts", "comparacao_cm_subjects", "comparacao_measurement_rows",
        "comparacoes", "email_list_recipients", "email_lists", "email_templates",
        "glass_density_settings", "job_ons", "machine_repairer_assignments", "mf_contexts",
        "pdf_directory_settings", "peso_measurement_rows", "peso_review_decisions", "pesos",
        "repairers", "template_modules", "templates", "tool_machines", "tools", "users",
    ];

    /// <summary>
    /// CMP1 — applying all ten migrations to a reset schema leaves exactly the ten migrations in
    /// <c>__EFMigrationsHistory</c> and the 28 raw public tables (27 product + history).
    /// </summary>
    [SkippableFact]
    public async Task CMP1_ExactlyThreeNewTablesAndTheTenthMigrationAreApplied()
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

    /// <summary>CMP1 — the migration source creates EXACTLY the three Comparação tables, carries no
    /// column change to <c>pesos</c>, and its <c>Down</c> drops exactly those three.</summary>
    [SkippableFact]
    public async Task CMP1_TheMigrationSourceCreatesExactlyTheThreeComparisonTables()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var source = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Migrations/20260925071139_ControloComparacaoDomain.cs");

        var created = Regex.Matches(source, @"CreateTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal(Sorted(NewTables.ToList()), Sorted(created));

        // No column/table change to the initial Peso table (zero-effect rule at the schema level).
        Assert.DoesNotContain("pesos", created, StringComparer.Ordinal);
        Assert.DoesNotContain("AddColumn", source, StringComparison.Ordinal);

        const string downMarker = "protected override void Down(MigrationBuilder migrationBuilder)";
        var downStart = source.IndexOf(downMarker, StringComparison.Ordinal);
        Assert.True(downStart >= 0, "The migration has no Down method.");
        var dropped = Regex.Matches(source[downStart..], @"DropTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal(Sorted(NewTables.ToList()), Sorted(dropped));
    }

    /// <summary>CMP2 — the constraints of the three tables match the comparison model exactly:
    /// the natural composite primary keys, the decision/justification/state CHECKs, the row-result
    /// CHECKs and the confirmation-stamp CHECK. Every FK is RESTRICT and no FK references a
    /// duplicated workstream identity.</summary>
    [SkippableFact]
    public async Task CMP2_TheComparisonTableConstraintsMatchTheModelExactly()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // The composite primary keys ARE the (comparacao_id, cm_id) and
        // (comparacao_id, cm_id, row_position) uniqueness invariants.
        var primaryKeys = Sorted(await QueryStringsAsync(
            context,
            "SELECT conrelid::regclass::text || '|' || conname FROM pg_constraint " +
            "WHERE contype = 'p' AND conrelid IN " +
            "('comparacoes'::regclass,'comparacao_cm_subjects'::regclass,'comparacao_measurement_rows'::regclass)"));
        Assert.Contains("comparacoes|PK_comparacoes", primaryKeys);
        Assert.Contains("comparacao_cm_subjects|PK_comparacao_cm_subjects", primaryKeys);
        Assert.Contains("comparacao_measurement_rows|PK_comparacao_measurement_rows", primaryKeys);

        // The CHECK constraints of the three tables.
        var checks = Sorted(await QueryStringsAsync(
            context,
            "SELECT conrelid::regclass::text || '|' || conname FROM pg_constraint " +
            "WHERE contype = 'c' AND conrelid IN " +
            "('comparacoes'::regclass,'comparacao_cm_subjects'::regclass,'comparacao_measurement_rows'::regclass)"));
        Assert.Contains("comparacoes|comparacoes_confirmed_check", checks);
        Assert.Contains("comparacao_cm_subjects|comparacao_cm_subjects_decision_check", checks);
        Assert.Contains("comparacao_cm_subjects|comparacao_cm_subjects_reason_check", checks);
        Assert.Contains("comparacao_cm_subjects|comparacao_cm_subjects_decision_state_check", checks);
        Assert.Contains("comparacao_measurement_rows|comparacao_measurement_rows_position_check", checks);
        Assert.Contains("comparacao_measurement_rows|comparacao_measurement_rows_weight_check", checks);
        Assert.Contains("comparacao_measurement_rows|comparacao_measurement_rows_capacity_check", checks);
        Assert.Contains("comparacao_measurement_rows|comparacao_measurement_rows_glass_check", checks);

        // Every FK is RESTRICT (confdeltype = 'r'), and the FKs reference only the accepted
        // identities (pesos, cm_contexts, users, comparacoes, comparacao_cm_subjects) — no
        // production/previous-Peso/duplicated-Tool target.
        var fks = await QueryStringsAsync(
            context,
            "SELECT conrelid::regclass::text || '|' || conname || '|' || confdeltype::text FROM pg_constraint " +
            "WHERE contype = 'f' AND conrelid IN " +
            "('comparacoes'::regclass,'comparacao_cm_subjects'::regclass,'comparacao_measurement_rows'::regclass)");
        Assert.Equal(8, fks.Count); // 3 (header) + 4 (subject) + 1 (rows composite) = 8
        foreach (var row in fks)
        {
            Assert.EndsWith("|r", row); // RESTRICT — no comparacao FK cascades.
        }
    }

    /// <summary>Physical proof of the natural identity: the <c>comparacao_cm_subjects</c> composite
    /// primary key exposes exactly the (comparacao_id, cm_id) columns, and the measurement-rows key
    /// exposes (comparacao_id, cm_id, row_position) — no surrogate subject id and no reading UUID.</summary>
    [SkippableFact]
    public async Task CMP2_TheSubjectAndRowKeysAreTheNaturalCompositeKeys()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var subjectKey = Sorted(await QueryStringsAsync(
            context,
            "SELECT a.attname FROM pg_constraint c " +
            "JOIN unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord) ON true " +
            "JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum " +
            "WHERE c.conname = 'PK_comparacao_cm_subjects' ORDER BY k.ord"));
        Assert.Equal(Sorted(["comparacao_id", "cm_id"]), subjectKey);

        var rowKey = Sorted(await QueryStringsAsync(
            context,
            "SELECT a.attname FROM pg_constraint c " +
            "JOIN unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord) ON true " +
            "JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum " +
            "WHERE c.conname = 'PK_comparacao_measurement_rows' ORDER BY k.ord"));
        Assert.Equal(Sorted(["comparacao_id", "cm_id", "row_position"]), rowKey);
    }

    /// <summary>
    /// The <c>pesos</c> table and its actors stay byte-identical: the comparison migration adds no
    /// column to <c>pesos</c> — the initial Peso row is never extended by the Comparação slice.
    /// </summary>
    [SkippableFact]
    public async Task CMP2_TheComparisonMigrationAddsNoColumnToPesos()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var source = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Migrations/20260925071139_ControloComparacaoDomain.cs");

        // The only occurrences of "pesos" are the header's FK principal table references — never an
        // AddColumn targeting pesos.
        Assert.DoesNotContain("AddColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("previous_peso", source, StringComparison.Ordinal);
        Assert.DoesNotContain("production", source, StringComparison.Ordinal | StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>CMP3 — the migration seeds no rows: the three comparison tables are empty after a
    /// clean apply (no placeholder comparison rows for the CMs — the zero-effect absence).</summary>
    [SkippableFact]
    public async Task CMP3_TheMigrationSeedsNoComparisonRows()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        foreach (var table in NewTables)
        {
            var count = Assert.Single(await QueryStringsAsync(context, $"SELECT count(*) FROM {table}"));
            Assert.Equal("0", count);
        }
    }

    /// <summary>CMP4 — re-running the migration mechanism over an updated schema applies zero
    /// migrations (idempotent no-op).</summary>
    [SkippableFact]
    public async Task CMP4_ReRunningAppliesZeroMigrations()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        Assert.Equal(10, (await context.Database.GetAppliedMigrationsAsync()).Count());
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(10, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    /// <summary>
    /// CMP1 — the raw schema carries no forbidden identity: no <c>previous_peso_id</c>, no
    /// <c>production_id</c> and no duplicated Tool column/table is declared by the comparison
    /// migration.
    /// </summary>
    [SkippableFact]
    public async Task CMP1_NoForbiddenIdentityAppearsInTheComparisonSchema()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var source = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Migrations/20260925071139_ControloComparacaoDomain.cs");

        Assert.DoesNotContain("previous_peso_id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviousPeso", source, StringComparison.Ordinal);
        Assert.DoesNotContain("production_id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_id = table.Column", source, StringComparison.Ordinal);
        Assert.DoesNotContain("reference", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lot", source, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Sorted(IReadOnlyList<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToList();

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