using System.Data;
using System.Text.RegularExpressions;
using DMO.Infrastructure.Persistence;
using DMO.IntegrationTests.JobOn;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table names and row
// identifiers derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T05 env-gated integration test â€” Migration 004 (<c>ControloCreateDomain</c>): the physical
/// schema, the contracted keys/constraints/indexes and the migration behaviour against a
/// disposable PostgreSQL database.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract Â§16 (physical schema), Â§17 (keys, constraints, indexes), Â§25
/// (migration contract) and Â§30 rows MIG-X1â€“MIG-X10 / AC-Y7 / AC-P1 / AC-F8. Every DB-class row is
/// <c>[SkippableFact]</c> behind <see cref="PersistenceTestDatabase.SkipIfNotConfigured"/> and is
/// reported as environment-gated skipped when no disposable database is configured.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class Migration004ControloCreateDomainTests
{
    /// <summary>EF's own migration bookkeeping table (never a product table).</summary>
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>The eight contracted Controlo tables of migration 004 (Â§16, Â§25.2).</summary>
    private static readonly string[] ControlTables =
    [
        "email_list_recipients", "email_lists", "email_templates", "machine_repairer_assignments",
        "pdf_directory_settings", "peso_measurement_rows", "pesos", "repairers",
    ];

    /// <summary>The complete public product-table register after all SEVEN migrations (order-insensitive;
    /// the post-closure correction adds exactly the one approved table; P2-T06 adds exactly the one
    /// review-decision table; P2-T07 adds exactly the THREE Boquilhas register tables — OWNER
    /// CLARIFICATION: the unreviewed 007 was corrected pre-closure, no lifecycle tables; disclosed
    /// extension).</summary>
    private static readonly string[] PublicProductTables =
    [
        "admin_accounts", "boquilha_movement_audit", "boquilha_movements", "boquilhas",
        "bq_contexts", "cm_contexts", "comparacao_cm_subjects", "comparacao_measurement_rows",
        "comparacoes", "email_list_recipients", "email_lists",
        "email_templates", "glass_density_settings", "job_ons", "machine_repairer_assignments",
        "mf_contexts", "pdf_directory_settings", "peso_measurement_rows", "pesos", "repairers",
        "template_modules", "templates", "tool_machines", "tools", "users", "peso_review_decisions",
    ];

    /// <summary>The TEN migrations, in generation order (§25.1; the correction migration 005 is
    /// the FIFTH overall — Architect review observation N-1; P2-T07 migration 007 is the SEVENTH;
    /// the P2-T08 delta migration 009 (EmailTemplateGroupRouting) and the Peso Comparação
    /// migration 010 (ControloComparacaoDomain — this slice) complete the set).</summary>
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

    /// <summary>The 22 contracted CHECK constraints of the eight tables (Â§17.1).</summary>
    private static readonly (string Table, string Name)[] ContractedChecks =
    [
        ("pesos", "pesos_anchor_check"),
        ("pesos", "pesos_status_check"),
        ("pesos", "pesos_temperature_check"),
        ("pesos", "pesos_marisa_volume_check"),
        ("pesos", "pesos_puncao_volume_check"),
        ("pesos", "pesos_density_check"),
        ("pesos", "pesos_sap_reference_check"),
        ("pesos", "pesos_sap_weight_check"),
        ("peso_measurement_rows", "peso_measurement_rows_position_check"),
        ("peso_measurement_rows", "peso_measurement_rows_weight_check"),
        ("peso_measurement_rows", "peso_measurement_rows_capacity_check"),
        ("peso_measurement_rows", "peso_measurement_rows_glass_check"),
        ("repairers", "repairers_name_required_check"),
        ("machine_repairer_assignments", "machine_repairer_assignments_machine_check"),
        ("pdf_directory_settings", "pdf_directory_settings_singleton_check"),
        ("pdf_directory_settings", "pdf_directory_settings_directory_required_check"),
        ("email_lists", "email_lists_name_required_check"),
        ("email_list_recipients", "email_list_recipients_address_required_check"),
        ("email_templates", "email_templates_name_required_check"),
        ("email_templates", "email_templates_subject_required_check"),
        ("email_templates", "email_templates_body_required_check"),
        ("email_templates", "email_templates_document_type_check"),
        // P2-T08 delta (migration 009): the disclosed email-routing CHECK on the SAME
        // email_templates table.
        ("email_templates", "email_templates_machine_group_check"),
    ];

    /// <summary>
    /// The seven contracted foreign keys of the eight tables, every one <c>ON DELETE RESTRICT</c>
    /// (Â§17.2, AC-P1). The P2-T08 email-routing delta (migration 009) adds the one disclosed
    /// eighth FK <c>email_templates_email_list_id_fkey</c> on the same <c>email_templates</c>
    /// table — a same-table additive disclosure.
    /// </summary>
    private static readonly string[] ContractedForeignKeys =
    [
        "FK_pesos_cm_contexts_cm_id",
        "FK_pesos_tools_tool_id",
        "FK_pesos_users_submitted_by_user_id",
        "FK_pesos_users_created_by_user_id",
        "FK_peso_measurement_rows_pesos_peso_id",
        "FK_machine_repairer_assignments_repairers_repairer_id",
        "FK_email_list_recipients_email_lists_email_list_id",
        "email_templates_email_list_id_fkey",
    ];

    /// <summary>The six contracted unique keys of the eight tables (Â§4.2-equivalent register).</summary>
    private static readonly string[] ContractedUniqueKeys =
    [
        "email_list_recipients_list_address_key",
        "email_lists_name_key",
        "email_templates_name_key",
        "machine_repairer_assignments_machine_key",
        "pdf_directory_settings_singleton_key",
        "peso_measurement_rows_peso_position_key",
    ];

    /// <summary>The three contracted declared (non-key) indexes of the eight tables.</summary>
    private static readonly string[] ContractedIndexes =
    [
        "IX_pesos_cm_id",
        "IX_pesos_tool_id",
        "machine_repairer_assignments_repairer_idx",
    ];

    /// <summary>Table-name fragments that must never name a product table (AC-Y7 scope boundary).</summary>
    private static readonly string[] ForbiddenTableFragments =
    [
        "compara", "pegamento", "resumo", "sheet", "approval", "decision", "document",
        "machine_registry", "machines", "history_assignment",
    ];

    /// <summary>
    /// MIG-X1: applying all migrations to a reset schema leaves exactly the seven contracted
    /// migrations in <c>__EFMigrationsHistory</c> and exactly the 24 raw public tables (the
    /// post-closure correction adds exactly the one approved table to the closed 19-table state;
    /// P2-T06 adds exactly the one review-decision table; P2-T07 adds exactly the THREE Boquilhas
    /// register tables — the raw count 24 = 23 product tables + <c>__EFMigrationsHistory</c>, see
    /// the P2-T07 implementation response).
    /// </summary>
    [SkippableFact]
    public async Task MIG_X1_ApplyingAllMigrationsLeavesSevenMigrationsAndTheTwentyFourRawTables()
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
    /// MIG-X2 (AC-Y7): the Controlo migration creates exactly the eight contracted tables, and no
    /// public table carries the forbidden identity vocabulary.
    /// </summary>
    [SkippableFact]
    public async Task MIG_X2_TheMigrationCreatesExactlyTheEightContractedTablesAndNothingElse()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // The migration source itself creates exactly the eight tables, in its own CreateTable calls.
        var source = P2T04ProductionScan.Read("src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain.cs");
        var created = Regex.Matches(source, @"CreateTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(Sorted(ControlTables.ToList()), Sorted(created));

        // The catalog agrees: no public table carries a forbidden identity fragment. The "machines"
        // fragment is matched at word boundaries so the accepted P2-T04 tool_machines table
        // (a legitimate compatibility table of a DIFFERENT migration) is never flagged, while a
        // standalone "machines" table would be.
        var tables = await QueryStringsAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'");

        foreach (var fragment in ForbiddenTableFragments)
        {
            Func<string, bool> matches = fragment == "machines"
                ? table => Regex.IsMatch(table, @"\bmachines\b", RegexOptions.IgnoreCase)
                : table => table.Contains(fragment, StringComparison.OrdinalIgnoreCase);

            // Disclosed P2-T06 extension: the single AUTHORIZED decision table
            // (peso_review_decisions, P2-T06 contract §6) is the one explicit exception to the
            // P2-T05-time "no decision table" boundary — everything else stays forbidden.
            // Disclosed Comparação extension: the THREE Comparação tables carry a "compara"-matching
            // identity (comparacoes/comparacao_cm_subjects/comparacao_measurement_rows) that was
            // not forbidden by this migration-004-specific row — the comparison aggregate lives in
            // its OWN tables and never touches the initial Peso rows.
            Assert.DoesNotContain(tables, table =>
                (table != "peso_review_decisions"
                 && !table.StartsWith("compara", StringComparison.Ordinal))
                && matches(table));
        }
    }

    /// <summary>
    /// MIG-X3: the 22 contracted CHECK constraints exist with their exact names on the contracted
    /// tables (<c>pg_constraint</c>, contype 'c').
    /// </summary>
    [SkippableFact]
    public async Task MIG_X3_AllTwentyTwoContractedCheckConstraintsExistWithTheirExactNames()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var checks = await QueryStringsAsync(
            context,
            "SELECT conrelid::regclass::text || '|' || conname FROM pg_constraint " +
            "WHERE contype = 'c' AND conrelid IN " +
            "('pesos'::regclass,'peso_measurement_rows'::regclass,'repairers'::regclass," +
            "'machine_repairer_assignments'::regclass,'pdf_directory_settings'::regclass," +
            "'email_lists'::regclass,'email_list_recipients'::regclass,'email_templates'::regclass)");

        Assert.Equal(
            Sorted(ContractedChecks.Select(check => $"{check.Table}|{check.Name}").ToList()),
            Sorted(checks.Select(check => check.Replace("public.", string.Empty, StringComparison.Ordinal)).ToList()));
    }

    /// <summary>
    /// MIG-X4 (AC-P1, Â§17.2): the seven foreign keys of the eight Controlo tables exist with the
    /// contracted names and every one is <c>ON DELETE RESTRICT</c> (<c>confdeltype = 'r'</c>).
    /// </summary>
    [SkippableFact]
    public async Task MIG_X4_AllSevenForeignKeysExistAndEveryOneIsRestrict()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var rows = await QueryStringsAsync(
            context,
            "SELECT conname || '|' || confdeltype::text FROM pg_constraint " +
            "WHERE contype = 'f' AND conrelid IN " +
            "('pesos'::regclass,'peso_measurement_rows'::regclass,'repairers'::regclass," +
            "'machine_repairer_assignments'::regclass,'pdf_directory_settings'::regclass," +
            "'email_lists'::regclass,'email_list_recipients'::regclass,'email_templates'::regclass)");

        var names = rows.Select(row => row.Split('|', 2)[0]).ToList();
        Assert.Equal(Sorted(ContractedForeignKeys.ToList()), Sorted(names));
        Assert.Equal(8, rows.Count);

        foreach (var row in rows)
        {
            var parts = row.Split('|', 2);
            Assert.Equal("r", parts[1]); // RESTRICT â€” no foreign key of this schema cascades.
        }
    }

    /// <summary>
    /// MIG-X5: the six contracted unique keys exist (<c>pg_indexes</c>), each with its exact name.
    /// </summary>
    [SkippableFact]
    public async Task MIG_X5_TheSixContractedUniqueKeysExist()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var indexes = await QueryStringsAsync(
            context,
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");

        foreach (var key in ContractedUniqueKeys)
        {
            Assert.Contains(key, indexes);
        }
    }

    /// <summary>
    /// MIG-X6: the three contracted declared indexes exist (<c>pg_indexes</c>).
    /// </summary>
    [SkippableFact]
    public async Task MIG_X6_TheContractedDeclaredIndexesExist()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var indexes = await QueryStringsAsync(
            context,
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");

        foreach (var index in ContractedIndexes)
        {
            Assert.Contains(index, indexes);
        }
    }

    /// <summary>
    /// MIG-X7 (SET10/AC-F8): the eight Controlo tables carry no per-user dimension column â€” the
    /// only columns mentioning <c>user</c> are the two <c>pesos</c> actor columns.
    /// </summary>
    [SkippableFact]
    public async Task MIG_X7_TheEightControloTablesCarryNoPerUserColumn()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var userColumns = await QueryStringsAsync(
            context,
            "SELECT table_name || '|' || column_name FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name IN " +
            "('pesos','peso_measurement_rows','repairers','machine_repairer_assignments'," +
            "'pdf_directory_settings','email_lists','email_list_recipients','email_templates') " +
            "AND column_name ILIKE '%user%'");

        // Only the two Peso actor columns may carry users; no settings table has any user column.
        Assert.Equal(
            Sorted(["pesos|submitted_by_user_id", "pesos|created_by_user_id"]),
            Sorted(userColumns));
    }

    /// <summary>
    /// MIG-X8: <c>Down</c> drops exactly the eight contracted Controlo tables (the migration source
    /// carries exactly eight <c>DropTable</c> statements over exactly those tables).
    /// </summary>
    [SkippableFact]
    public async Task MIG_X8_DownDropsExactlyTheEightContractedTables()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var source = P2T04ProductionScan.Read(
            "src/DMO.Infrastructure/Migrations/20260923045054_ControloCreateDomain.cs");

        const string downMarker = "protected override void Down(MigrationBuilder migrationBuilder)";
        var downStart = source.IndexOf(downMarker, StringComparison.Ordinal);
        Assert.True(downStart >= 0, "The migration has no Down method.");

        var down = source[downStart..];
        var dropped = Regex.Matches(down, @"DropTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToList();

        Assert.Equal(8, dropped.Count);
        Assert.Equal(Sorted(ControlTables.ToList()), Sorted(dropped));
    }

    /// <summary>
    /// MIG-X9: after applying all migrations to a reset schema, every CONTROL migration table
    /// (migration 004's eight) is empty â€” migration 004 seeds no rows. The post-closure
    /// correction's exactly two provenance-backed initial rows are the documented exception,
    /// proven by <c>Migration005GlassDensitySettingsTests</c>.
    /// </summary>
    [SkippableFact]
    public async Task MIG_X9_TheMigrationSeedsNoRowsIntoAnyOfTheEightTables()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        foreach (var table in ControlTables)
        {
            var count = Assert.Single(await QueryStringsAsync(context, $"SELECT count(*) FROM {table}"));
            Assert.Equal("0", count);
        }
    }

    /// <summary>
    /// MIG-X10: re-running the migration mechanism over an already-updated schema applies zero
    /// migrations â€” the application is a complete no-op.
    /// </summary>
    [SkippableFact]
    public async Task MIG_X10_ReRunningApplyPendingAppliesZeroMigrations()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // P2-T07 (disclosed extension): SEVEN migrations are applied with the database up to date;
        // the P2-T08 delta and the Peso Comparação slice bring the applied count to TEN.
        Assert.Equal(10, (await context.Database.GetAppliedMigrationsAsync()).Count());

        // The second application is a complete no-op: it must not throw and leaves nothing pending.
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(10, (await context.Database.GetAppliedMigrationsAsync()).Count());
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