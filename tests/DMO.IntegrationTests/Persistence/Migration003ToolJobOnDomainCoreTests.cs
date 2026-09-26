using System.Data;
using System.Text.RegularExpressions;
using DMO.Infrastructure.Persistence;
using DMO.IntegrationTests.Frontend.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T04 env-gated integration test â€” Migration 003 (<c>ToolJobOnDomainCore</c>): the physical
/// schema, the contracted keys/constraints/indexes and the migration behaviour against a
/// disposable PostgreSQL database.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract Â§3 (physical schema), Â§4 (keys, constraints, indexes), Â§16 (migration
/// contract), Â§19 (PostgreSQL/Supabase compatibility) and Â§20.9 (rows MIG1â€“MIG12). Every DB-class
/// row is <c>[SkippableFact]</c> behind <see cref="PersistenceTestDatabase.SkipIfNotConfigured"/>
/// and is reported as environment-gated skipped when no disposable database is configured
/// (Â§20.10).
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class Migration003ToolJobOnDomainCoreTests
{
    /// <summary>The migration the P2-T04 migration is applied on top of (Â§20.9 row MIG8).</summary>
    private const string PreviousMigrationId = "20260922001757_TemplateModuleComposition";

    /// <summary>The EF migration class name of the P2-T04 migration (Â§16.1).</summary>
    private const string ToolJobOnMigrationName = "ToolJobOnDomainCore";

    /// <summary>
    /// The EF migration class name of the P2-T05 migration (P2-T05 contract Â§25.1) â€” disclosed
    /// extension: the P2-T05 migration now applies on top of this one.
    /// </summary>
    private const string ControloCreateMigrationName = "ControloCreateDomain";

    /// <summary>The glass-density correction migration 005 (post-closure correction Â§5.6).</summary>
    private const string CorrectionMigrationName = "GlassDensitySettings";

    /// <summary>The four foundation tables of migrations 001/002.</summary>
    private static readonly string[] FoundationTables =
        ["admin_accounts", "templates", "template_modules", "users"];

    /// <summary>The six contracted domain-core tables of migration 003 (Â§3, Â§16.2).</summary>
    private static readonly string[] DomainCoreTables =
        ["bq_contexts", "cm_contexts", "job_ons", "mf_contexts", "tool_machines", "tools"];

    /// <summary>
    /// The eight contracted Controlo tables of migration 004 (disclosed P2-T05 extension, P2-T05
    /// contract Â§16/Â§25).
    /// </summary>
    private static readonly string[] ControloTables =
    [
        "email_list_recipients", "email_lists", "email_templates",
        "machine_repairer_assignments", "pdf_directory_settings", "peso_measurement_rows",
        "pesos", "repairers",
    ];

    /// <summary>The single approved table of the post-closure glass-density correction (migration 005).</summary>
    private const string GlassDensitySettingsTable = "glass_density_settings";

    /// <summary>The single P2-T06 table of migration 006 (disclosed extension, P2-T06 contract §25).</summary>
    private const string PesoReviewDecisionsTable = "peso_review_decisions";

    /// <summary>
    /// The THREE Boquilhas tables of migration 007 (disclosed extension; the P2-T07 OWNER
    /// CLARIFICATION corrected the unreviewed 007 pre-closure — the lifecycle tables
    /// <c>boquilha_close_snapshots</c>/<c>boquilha_reopenings</c>/<c>boquilha_machines</c> are
    /// gone and the final product-table count is 23).
    /// </summary>
    private static readonly string[] BoquilhasTables =
    [
        "boquilha_movement_audit", "boquilha_movements", "boquilhas",
    ];

    /// <summary>
    /// The THREE Peso Comparação tables of migration 010 (disclosed in the implementation
    /// response): the comparison aggregate lives in its OWN tables and never touches the initial
    /// Peso rows.
    /// </summary>
    private static readonly string[] ComparacaoTables =
    [
        "comparacoes", "comparacao_cm_subjects", "comparacao_measurement_rows",
    ];

    /// <summary>EF's own migration bookkeeping table (never a product table).</summary>
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>Tables that must never appear (contract Â§17, AC-99, AC-104; P2-T05 removed
    /// <c>repairers</c> from the forbidden list because the accepted P2-T05 contract creates the
    /// canonical repairer register â€” disclosed extension).</summary>
    private static readonly string[] ForbiddenTables =
    [
        "machines", "machine_registry", "tool_references", "documents", "settings",
        "permissions", "capabilities", "roles", "admin_audit_events",
    ];

    /// <summary>The 44 contracted columns of the six tables (Â§3.1â€“Â§3.4).</summary>
    private static readonly ColumnContract[] ContractedColumns =
    [
        new("tools", "tool_id", "uuid", "NO", "gen_random_uuid()"),
        new("tools", "tool_type", "text", "NO", null),
        new("tools", "reference", "text", "NO", null),
        new("tools", "lot", "text", "NO", null),
        new("tools", "processo", "text", "YES", null),
        new("tools", "quantity", "integer", "YES", null),
        new("tools", "created_at", "timestamp with time zone", "NO", "now()"),
        new("tools", "updated_at", "timestamp with time zone", "NO", "now()"),

        new("tool_machines", "tool_machine_id", "uuid", "NO", "gen_random_uuid()"),
        new("tool_machines", "tool_id", "uuid", "NO", null),
        new("tool_machines", "machine", "text", "NO", null),

        new("job_ons", "jobon_id", "uuid", "NO", "gen_random_uuid()"),
        new("job_ons", "reference", "text", "NO", null),
        new("job_ons", "production_number", "text", "NO", null),
        new("job_ons", "machine", "text", "NO", null),
        new("job_ons", "production_date", "date", "YES", null),
        new("job_ons", "copied_from_jobon_id", "uuid", "YES", null),
        new("job_ons", "version", "integer", "NO", "1"),
        new("job_ons", "created_at", "timestamp with time zone", "NO", "now()"),
        new("job_ons", "updated_at", "timestamp with time zone", "NO", "now()"),

        new("cm_contexts", "cm_id", "uuid", "NO", "gen_random_uuid()"),
        new("cm_contexts", "jobon_id", "uuid", "NO", null),
        new("cm_contexts", "tool_id", "uuid", "NO", null),
        new("cm_contexts", "tool_type", "text", "NO", null),
        new("cm_contexts", "tool_reference", "text", "NO", null),
        new("cm_contexts", "tool_lot", "text", "NO", null),
        new("cm_contexts", "created_at", "timestamp with time zone", "NO", "now()"),
        new("cm_contexts", "updated_at", "timestamp with time zone", "NO", "now()"),

        new("mf_contexts", "mf_id", "uuid", "NO", "gen_random_uuid()"),
        new("mf_contexts", "jobon_id", "uuid", "NO", null),
        new("mf_contexts", "tool_id", "uuid", "NO", null),
        new("mf_contexts", "tool_type", "text", "NO", null),
        new("mf_contexts", "tool_reference", "text", "NO", null),
        new("mf_contexts", "tool_lot", "text", "NO", null),
        new("mf_contexts", "created_at", "timestamp with time zone", "NO", "now()"),
        new("mf_contexts", "updated_at", "timestamp with time zone", "NO", "now()"),

        new("bq_contexts", "bq_id", "uuid", "NO", "gen_random_uuid()"),
        new("bq_contexts", "jobon_id", "uuid", "NO", null),
        new("bq_contexts", "tool_id", "uuid", "NO", null),
        new("bq_contexts", "tool_type", "text", "NO", null),
        new("bq_contexts", "tool_reference", "text", "NO", null),
        new("bq_contexts", "tool_lot", "text", "NO", null),
        new("bq_contexts", "created_at", "timestamp with time zone", "NO", "now()"),
        new("bq_contexts", "updated_at", "timestamp with time zone", "NO", "now()"),
    ];

    /// <summary>The 18 contracted CHECK constraints (Â§4.4).</summary>
    private static readonly (string Table, string Name)[] ContractedChecks =
    [
        ("tools", "tools_type_check"),
        ("tools", "tools_reference_required_check"),
        ("tools", "tools_lot_required_check"),
        ("tools", "tools_processo_check"),
        ("tools", "tools_quantity_check"),
        ("tool_machines", "tool_machines_machine_check"),
        ("job_ons", "job_ons_reference_required_check"),
        ("job_ons", "job_ons_production_number_required_check"),
        ("job_ons", "job_ons_machine_check"),
        ("cm_contexts", "cm_contexts_tool_type_check"),
        ("cm_contexts", "cm_contexts_tool_reference_required_check"),
        ("cm_contexts", "cm_contexts_tool_lot_required_check"),
        ("mf_contexts", "mf_contexts_tool_type_check"),
        ("mf_contexts", "mf_contexts_tool_reference_required_check"),
        ("mf_contexts", "mf_contexts_tool_lot_required_check"),
        ("bq_contexts", "bq_contexts_tool_type_check"),
        ("bq_contexts", "bq_contexts_tool_reference_required_check"),
        ("bq_contexts", "bq_contexts_tool_lot_required_check"),
    ];

    /// <summary>The six contracted unique keys (Â§4.2).</summary>
    private static readonly IndexContract[] ContractedUniqueKeys =
    [
        new("tools", "tools_type_reference_lot_key", true, "tool_type,reference,lot"),
        new("tool_machines", "tool_machines_tool_machine_key", true, "tool_id,machine"),
        new("job_ons", "job_ons_reference_production_number_key", true, "reference,production_number"),
        new("cm_contexts", "cm_contexts_jobon_key", true, "jobon_id"),
        new("mf_contexts", "mf_contexts_jobon_key", true, "jobon_id"),
        new("bq_contexts", "bq_contexts_jobon_key", true, "jobon_id"),
    ];

    /// <summary>
    /// The complete contracted index register of the six tables (Â§4.5): the twelve contracted
    /// indexes plus the six primary-key indexes. No other index is contracted.
    /// </summary>
    private static readonly IndexContract[] ContractedIndexes =
    [
        .. ContractedUniqueKeys,
        new("tools", "tools_reference_idx", false, "reference"),
        new("tool_machines", "tool_machines_machine_idx", false, "machine"),
        new("job_ons", "IX_job_ons_copied_from_jobon_id", false, "copied_from_jobon_id"),
        new("cm_contexts", "IX_cm_contexts_tool_id", false, "tool_id"),
        new("mf_contexts", "IX_mf_contexts_tool_id", false, "tool_id"),
        new("bq_contexts", "IX_bq_contexts_tool_id", false, "tool_id"),
        new("tools", "PK_tools", true, "tool_id"),
        new("tool_machines", "PK_tool_machines", true, "tool_machine_id"),
        new("job_ons", "PK_job_ons", true, "jobon_id"),
        new("cm_contexts", "PK_cm_contexts", true, "cm_id"),
        new("mf_contexts", "PK_mf_contexts", true, "mf_id"),
        new("bq_contexts", "PK_bq_contexts", true, "bq_id"),
    ];

    /// <summary>The eight contracted foreign keys, every one ON DELETE RESTRICT (Â§4.3).</summary>
    private static readonly ForeignKeyContract[] ContractedForeignKeys =
    [
        new("FK_tool_machines_tools_tool_id", "tool_machines", "tool_id", "tools", "tool_id"),
        new("FK_job_ons_job_ons_copied_from_jobon_id", "job_ons", "copied_from_jobon_id", "job_ons", "jobon_id"),
        new("FK_cm_contexts_job_ons_jobon_id", "cm_contexts", "jobon_id", "job_ons", "jobon_id"),
        new("FK_cm_contexts_tools_tool_id", "cm_contexts", "tool_id", "tools", "tool_id"),
        new("FK_mf_contexts_job_ons_jobon_id", "mf_contexts", "jobon_id", "job_ons", "jobon_id"),
        new("FK_mf_contexts_tools_tool_id", "mf_contexts", "tool_id", "tools", "tool_id"),
        new("FK_bq_contexts_job_ons_jobon_id", "bq_contexts", "jobon_id", "job_ons", "jobon_id"),
        new("FK_bq_contexts_tools_tool_id", "bq_contexts", "tool_id", "tools", "tool_id"),
    ];

    /// <summary>
    /// MIG1 (AC-103, AC-104): after the P2-T04 migration <c>public</c> holds exactly the four
    /// foundation tables plus the six contracted domain-core tables, and no other product table.
    /// </summary>
    [SkippableFact]
    public async Task MIG1_PublicHoldsExactlyTheFourFoundationTablesPlusTheSixContractedTables()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var tables = Sorted(await QueryStringsAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"));

        // Disclosed P2-T05 extension: the shared schema now also holds the eight contracted
        // Controlo tables (migration 004); the post-closure glass-density correction adds
        // exactly the one approved settings table (migration 005); the P2-T06 Controlo Approve
        // migration 006 adds exactly the one review-decision table; the P2-T07 Boquilhas
        // migration 007 adds exactly the THREE Boquilhas register tables (OWNER CLARIFICATION:
        // the unreviewed 007 was corrected pre-closure — the lifecycle tables are gone; the
        // final product-table count is 23); the Peso Comparação migration 010 adds exactly the
        // THREE comparison tables. The P2-T04 rows keep pinning the complete set.
        Assert.Equal(
            Sorted([.. FoundationTables, .. DomainCoreTables, .. ControloTables, GlassDensitySettingsTable, PesoReviewDecisionsTable, .. BoquilhasTables, .. ComparacaoTables, MigrationHistoryTable]),
            tables);

        foreach (var forbidden in ForbiddenTables)
        {
            Assert.DoesNotContain(forbidden, tables);
        }
    }

    /// <summary>
    /// MIG2 (AC-103): every contracted column exists with the contracted type, nullability and
    /// database default (catalog query on <c>information_schema.columns</c>).
    /// </summary>
    [SkippableFact]
    public async Task MIG2_EveryContractedColumnExistsWithTheContractedTypeAndNullability()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var columns = Sorted(await QueryStringsAsync(
            context,
            "SELECT table_name || '|' || column_name || '|' || data_type || '|' || is_nullable || '|' || " +
            "coalesce(column_default, '') FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name IN " +
            "('tools','tool_machines','job_ons','cm_contexts','mf_contexts','bq_contexts')"));

        Assert.Equal(
            Sorted(ContractedColumns
                .Select(column => column.Render())
                .ToList()),
            columns);
    }

    /// <summary>
    /// MIG3 (AC-103, AC-106): every contracted CHECK constraint exists with the contracted name on
    /// the contracted table and actually rejects a violating row with <c>23514</c> naming it.
    /// </summary>
    [SkippableFact]
    public async Task MIG3_EveryContractedCheckConstraintExistsAndRejectsAViolatingRow()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // The catalog half: exactly the eighteen contracted checks on the contracted tables.
        var checks = await QueryStringsAsync(
            context,
            "SELECT conrelid::regclass::text || '|' || conname FROM pg_constraint " +
            "WHERE contype = 'c' AND conrelid IN " +
            "('tools'::regclass,'tool_machines'::regclass,'job_ons'::regclass," +
            "'cm_contexts'::regclass,'mf_contexts'::regclass,'bq_contexts'::regclass)");

        Assert.Equal(
            Sorted(ContractedChecks.Select(check => $"{check.Table}|{check.Name}").ToList()),
            Sorted(checks.Select(check => check.Replace("public.", string.Empty, StringComparison.Ordinal)).ToList()));

        // The behavioural half: each contracted check rejects a row that violates exactly it.
        var token = Guid.NewGuid().ToString("N");
        var cmToolId = Guid.NewGuid();
        var mfToolId = Guid.NewGuid();
        var bqToolId = Guid.NewGuid();
        var cmJobOnId = Guid.NewGuid();
        var mfJobOnId = Guid.NewGuid();
        var bqJobOnId = Guid.NewGuid();

        try
        {
            await InsertToolAsync(context, cmToolId, "CM", $"mig3-cm-{token}", $"mig3-lot-cm-{token}");
            await InsertToolAsync(context, mfToolId, "MF", $"mig3-mf-{token}", $"mig3-lot-mf-{token}");
            await InsertToolAsync(context, bqToolId, "BQ", $"mig3-bq-{token}", $"mig3-lot-bq-{token}");
            await InsertJobOnAsync(context, cmJobOnId, $"mig3-cm-{token}", $"mig3-pn-cm-{token}");
            await InsertJobOnAsync(context, mfJobOnId, $"mig3-mf-{token}", $"mig3-pn-mf-{token}");
            await InsertJobOnAsync(context, bqJobOnId, $"mig3-bq-{token}", $"mig3-pn-bq-{token}");

            foreach (var violation in ViolationCases(
                         token, cmToolId, mfToolId, bqToolId, cmJobOnId, mfJobOnId, bqJobOnId))
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(
                    () => context.Database.ExecuteSqlRawAsync(violation.Sql));

                Assert.Equal("23514", exception.SqlState);
                Assert.Equal(violation.ConstraintName, exception.ConstraintName);
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// MIG4 (AC-103, AC-106): the six contracted unique keys exist and are unique, on the
    /// contracted table and column tuple.
    /// </summary>
    [SkippableFact]
    public async Task MIG4_TheSixContractedUniqueKeysExistAndAreUnique()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var indexes = await ReadIndexesAsync(context);

        Assert.Equal(
            Sorted(ContractedUniqueKeys.Select(key => key.Render()).ToList()),
            Sorted(ContractedUniqueKeys
                .Select(key => Assert.Single(indexes, index => index.Name == key.Name))
                .Select(index => index.Render())
                .ToList()));
    }

    /// <summary>
    /// MIG5 (AC-80, AC-106): all eight foreign keys exist with the contracted names, columns and
    /// referenced targets, and every one is <c>ON DELETE RESTRICT</c> (<c>confdeltype = 'r'</c>).
    /// </summary>
    [SkippableFact]
    public async Task MIG5_AllEightForeignKeysExistAndEveryOneIsRestrict()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var rows = await QueryStringsAsync(
            context,
            "SELECT conrelid::regclass::text || '|' || conname || '|' || confdeltype::text || '|' || " +
            "pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'f' AND conrelid IN " +
            "('tools'::regclass,'tool_machines'::regclass,'job_ons'::regclass," +
            "'cm_contexts'::regclass,'mf_contexts'::regclass,'bq_contexts'::regclass)");

        var parsed = rows.Select(row =>
        {
            var parts = row.Split('|', 4);
            return new ForeignKeyRow(
                parts[0].Replace("public.", string.Empty, StringComparison.Ordinal),
                parts[1],
                parts[2],
                parts[3].Replace("public.", string.Empty, StringComparison.Ordinal));
        }).ToList();

        Assert.Equal(
            Sorted(ContractedForeignKeys.Select(key => key.Name).ToList()),
            Sorted(parsed.Select(row => row.Name).ToList()));

        foreach (var expected in ContractedForeignKeys)
        {
            var actual = Assert.Single(parsed, row => row.Name == expected.Name);

            Assert.Equal(expected.Table, actual.Table);
            Assert.Equal("r", actual.DeleteType); // RESTRICT â€” no foreign key of this schema cascades.
            Assert.Contains(
                $"FOREIGN KEY ({expected.Column}) REFERENCES " +
                $"{expected.PrincipalTable}({expected.PrincipalColumn}) ON DELETE RESTRICT",
                actual.Definition,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// MIG6 (AC-103): every contracted index exists with the contracted name, table, column list
    /// and uniqueness, and the six tables carry no index beyond the contracted register.
    /// </summary>
    [SkippableFact]
    public async Task MIG6_EveryContractedIndexExistsWithItsContractedNameAndColumns()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var indexes = await ReadIndexesAsync(context);

        Assert.Equal(
            Sorted(ContractedIndexes.Select(index => index.Render()).ToList()),
            Sorted(indexes.Select(index => index.Render()).ToList()));
    }

    /// <summary>
    /// MIG7 (AC-103): applying the migration twice is a no-op â€” no error, no pending migration and
    /// no schema change of any of the ten tables.
    /// </summary>
    [SkippableFact]
    public async Task MIG7_ApplyingTheMigrationAgainIsANoOpWithNoSchemaChange()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var before = Sorted(await SchemaCatalogAsync(context));

        // The second application is a complete no-op: it must not throw.
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var after = Sorted(await SchemaCatalogAsync(context));

        Assert.Equal(before, after);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    /// <summary>
    /// MIG8 (AC-103): <c>Down</c> drops exactly the six contracted tables and leaves the foundation
    /// tables and their constraints intact; the database is then returned to the latest version.
    /// </summary>
    [SkippableFact]
    public async Task MIG8_DownDropsExactlyTheSixTablesAndLeavesTheFoundationIntact()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        Assert.Equal(PreviousMigrationId, PreviousMigrationIdOf(context));

        try
        {
            await context.Database
                .GetService<IMigrator>()
                .MigrateAsync(PreviousMigrationId);

            // Exactly the six tables are gone: the remaining product tables are the foundation four.
            var tables = Sorted(await QueryStringsAsync(
                context,
                "SELECT table_name FROM information_schema.tables " +
                "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"));
            Assert.Equal(Sorted([.. FoundationTables, MigrationHistoryTable]), tables);

            foreach (var dropped in DomainCoreTables)
            {
                Assert.DoesNotContain(dropped, tables);
            }

            // The foundation constraints and indexes are untouched by the rollback.
            var foundationConstraints = Sorted(await QueryStringsAsync(
                context,
                "SELECT conrelid::regclass::text || '|' || conname FROM pg_constraint WHERE conrelid IN " +
                "('templates'::regclass,'template_modules'::regclass,'users'::regclass," +
                "'admin_accounts'::regclass)"));
            Assert.Contains("template_modules|PK_template_modules", foundationConstraints);
            Assert.Contains("users|FK_users_templates_template_id", foundationConstraints);
            Assert.Contains("admin_accounts|admin_accounts_singleton_id_check", foundationConstraints);

            var foundationIndexes = Sorted(await QueryStringsAsync(
                context,
                "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND tablename IN " +
                "('templates','template_modules','users','admin_accounts')"));
            Assert.Contains("users_company_number_key", foundationIndexes);
            Assert.Contains("template_modules_template_order_key", foundationIndexes);

            // The migration no longer counts as applied; the foundation migrations still do.
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.DoesNotContain(applied, migration => migration.EndsWith(ToolJobOnMigrationName, StringComparison.Ordinal));
            Assert.Contains(PreviousMigrationId, applied);
            Assert.Contains("20260922001736_AccountAndTemplateFoundation", applied);
        }
        finally
        {
            // The shared disposable database is always left at the latest version.
            await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        }

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    /// <summary>
    /// MIG9 (AC-96): after the P2-T04 migration the four foundation tables are byte-equivalent to
    /// their pre-migration catalog state (columns, defaults, constraints and indexes).
    /// </summary>
    [SkippableFact]
    public async Task MIG9_TheFoundationCatalogStateIsUnchangedByTheMigration()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        Assert.Equal(PreviousMigrationId, PreviousMigrationIdOf(context));

        try
        {
            // Pre-migration state: the foundation schema immediately below the P2-T04 migration.
            await context.Database.GetService<IMigrator>().MigrateAsync(PreviousMigrationId);
            var before = Sorted(await FoundationCatalogAsync(context));
            Assert.NotEmpty(before);

            // Apply the P2-T04 migration and compare the same foundation catalog state.
            await PersistenceTestDatabase.ApplyMigrationsAsync(context);
            var after = Sorted(await FoundationCatalogAsync(context));

            Assert.Equal(before, after);

            // Non-effect: the migration really did add its six tables.
            var tables = await QueryStringsAsync(
                context,
                "SELECT table_name FROM information_schema.tables " +
                "WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name IN " +
                "('tools','tool_machines','job_ons','cm_contexts','mf_contexts','bq_contexts')");
            Assert.Equal(Sorted(DomainCoreTables.ToList()), Sorted(tables));
        }
        finally
        {
            await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        }
    }

    /// <summary>
    /// MIG10 (AC-103): exactly one new migration file pair exists for the P2-T04 migration, and
    /// the model snapshot records exactly the six new tables (plus the correction table).
    /// </summary>
    [SkippableFact]
    public async Task MIG10_ExactlyOneMigrationFilePairExistsAndTheSnapshotRecordsTheSixTables()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var migrationsDirectory = Path.Combine(
            P2T03ProductionScan.RepositoryRoot(),
            "src",
            "DMO.Infrastructure",
            "Migrations");

        var files = Directory.GetFiles(migrationsDirectory, "*.cs")
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        var migrationFiles = files
            .Where(name => !name.EndsWith(".Designer.cs", StringComparison.Ordinal)
                && !name.StartsWith("DmoDbContextModelSnapshot", StringComparison.Ordinal))
            .ToList();

        // Exactly EIGHT migrations existed at the last disclosed baseline; the P2-T08 email-routing
        // migration 009 (a second disclosed delta) and the Peso Comparação migration 010 (this
        // slice) join the set — the file-pair count is now TEN. The P2-T04 pair is still tracked.
        Assert.Equal(10, migrationFiles.Count);
        var toolJobOnMigrations = migrationFiles
            .Where(name => name.EndsWith($"_{ToolJobOnMigrationName}.cs", StringComparison.Ordinal))
            .ToList();
        var migrationFile = Assert.Single(toolJobOnMigrations);
        Assert.Equal("20260922232349_ToolJobOnDomainCore.cs", migrationFile);

        var designerFile = Assert.Single(files, name =>
            name.EndsWith($"_{ToolJobOnMigrationName}.Designer.cs", StringComparison.Ordinal));
        Assert.Equal("20260922232349_ToolJobOnDomainCore.Designer.cs", designerFile);

        // The pair shares one timestamp prefix (one migration, one owner â€” Â§16.1).
        var stamp = migrationFile[..migrationFile.IndexOf('_')];
        Assert.StartsWith(stamp, designerFile, StringComparison.Ordinal);

        // The single P2-T05 migration pair exists on top of this one (P2-T05 contract Â§25.1).
        var controloMigrations = migrationFiles
            .Where(name => name.EndsWith($"_{ControloCreateMigrationName}.cs", StringComparison.Ordinal))
            .ToList();
        var controloMigrationFile = Assert.Single(controloMigrations);
        var controloDesignerFile = Assert.Single(files, name =>
            name.EndsWith($"_{ControloCreateMigrationName}.Designer.cs", StringComparison.Ordinal));
        var controloStamp = controloMigrationFile[..controloMigrationFile.IndexOf('_')];
        Assert.StartsWith(controloStamp, controloDesignerFile, StringComparison.Ordinal);

        // The snapshot and the CONTROL migration designers record exactly the eighteen product tables
        // (disclosed P2-T05 extension: ten prior + eight Controlo); the snapshot ALSO records the
        // one correction table (nineteen product tables after migration 005) and the one review
        // decision table (P2-T06) and the THREE Boquilhas register tables (P2-T07 OWNER
        // CLARIFICATION: the unreviewed 007 was corrected pre-closure — no lifecycle tables; the
        // final product-table count is 23). The P2-T04 migration's
        // OWN designer is a frozen historical artifact of its generation time and still records
        // the ten tables it shipped with.
        var expectedTables = Sorted([.. FoundationTables, .. DomainCoreTables, .. ControloTables]);
        var expectedSnapshotTables = Sorted([.. FoundationTables, .. DomainCoreTables, .. ControloTables, GlassDensitySettingsTable, PesoReviewDecisionsTable, .. BoquilhasTables, .. ComparacaoTables]);
        var expectedP2T04DesignerTables = Sorted([.. FoundationTables, .. DomainCoreTables]);

        var snapshot = await File.ReadAllTextAsync(Path.Combine(migrationsDirectory, "DmoDbContextModelSnapshot.cs"));
        Assert.Equal(expectedSnapshotTables, Sorted(TablesOf(snapshot)));

        var designer = await File.ReadAllTextAsync(Path.Combine(migrationsDirectory, designerFile));
        Assert.Equal(expectedP2T04DesignerTables, Sorted(TablesOf(designer)));

        var controloDesigner = await File.ReadAllTextAsync(Path.Combine(migrationsDirectory, controloDesignerFile));
        Assert.Equal(expectedTables, Sorted(TablesOf(controloDesigner)));
    }

    /// <summary>
    /// MIG11 (AC-106): the migration applies cleanly to the disposable PostgreSQL database and
    /// leaves it at the latest migration with the complete contracted schema.
    /// </summary>
    [SkippableFact]
    public async Task MIG11_TheMigrationAppliesCleanlyAndLeavesTheDatabaseAtTheLatestVersion()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var latest = context.Database.GetMigrations().Last();
        // Disclosed P2-T05 extension: the latest migration is now the Controlo domain migration;
        // the post-closure correction adds the glass-density migration 005 on top (Architect
        // review observation N-1: the correction migration is the FIFTH overall); P2-T06 adds
        // the Controlo Approve migration 006 on top (disclosed extension); P2-T07 adds the
        // Boquilhas migration 007 on top (disclosed extension, P2-T07 contract §28); the §34
        // OWNER-clarification delta adds migration 008 (BoquilhasPreJobonAssociation) on top;
        // the P2-T08 delta adds migration 009 (EmailTemplateGroupRouting); the Peso Comparação
        // slice adds migration 010 (ControloComparacaoDomain) on top (this slice).
        var latestIsControlo = latest.EndsWith(ControloCreateMigrationName, StringComparison.Ordinal);
        var latestIsCorrection = latest.EndsWith(CorrectionMigrationName, StringComparison.Ordinal);
        var latestIsP2T06 = latest.EndsWith("ControloApproveDomain", StringComparison.Ordinal);
        var latestIsP2T07 = latest.EndsWith("BoquilhasDomain", StringComparison.Ordinal);
        var latestIsPreJobon = latest.EndsWith("BoquilhasPreJobonAssociation", StringComparison.Ordinal);
        var latestIsEmailRouting = latest.EndsWith("EmailTemplateGroupRouting", StringComparison.Ordinal);
        var latestIsComparacao = latest.EndsWith("ControloComparacaoDomain", StringComparison.Ordinal);
        Assert.True(
            latestIsControlo || latestIsCorrection || latestIsP2T06 || latestIsP2T07 || latestIsPreJobon
            || latestIsEmailRouting || latestIsComparacao,
            $"The latest migration must be the Controlo domain, the glass-density correction, " +
            $"the P2-T06 Controlo Approve domain, the P2-T07 Boquilhas domain, the §34 delta " +
            $"(BoquilhasPreJobonAssociation), the P2-T08 email-routing delta or the Peso " +
            $"Comparação domain, was {latest}.");

        try
        {
            // Down to the previous migration, then a clean application to the target version.
            await context.Database.GetService<IMigrator>().MigrateAsync(PreviousMigrationId);

            var absent = await QueryStringsAsync(
                context,
                "SELECT table_name FROM information_schema.tables " +
                "WHERE table_schema = 'public' AND table_name IN " +
                "('tools','tool_machines','job_ons','cm_contexts','mf_contexts','bq_contexts')");
            Assert.Empty(absent);

            await PersistenceTestDatabase.ApplyMigrationsAsync(context);

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Contains(latest, applied);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            // The re-applied schema is the contracted one: tables, checks, keys and foreign keys.
            var tables = Sorted(await QueryStringsAsync(
                context,
                "SELECT table_name FROM information_schema.tables " +
                "WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name IN " +
                "('tools','tool_machines','job_ons','cm_contexts','mf_contexts','bq_contexts')"));
            Assert.Equal(Sorted(DomainCoreTables.ToList()), tables);

            Assert.Equal(
                Sorted(ContractedChecks.Select(check => $"{check.Table}|{check.Name}").ToList()),
                Sorted((await QueryStringsAsync(
                        context,
                        "SELECT conrelid::regclass::text || '|' || conname FROM pg_constraint " +
                        "WHERE contype = 'c' AND conrelid IN " +
                        "('tools'::regclass,'tool_machines'::regclass,'job_ons'::regclass," +
                        "'cm_contexts'::regclass,'mf_contexts'::regclass,'bq_contexts'::regclass)"))
                    .Select(check => check.Replace("public.", string.Empty, StringComparison.Ordinal))
                    .ToList()));

            Assert.Equal(
                Sorted(ContractedIndexes.Select(index => index.Render()).ToList()),
                Sorted((await ReadIndexesAsync(context)).Select(index => index.Render()).ToList()));
        }
        finally
        {
            await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        }
    }

    /// <summary>
    /// MIG12 (AC-96, AC-103): the migration contains no destructive statement and no seed/
    /// reference-data insert; <c>Down</c> is exactly the six contracted table drops.
    /// </summary>
    [SkippableFact]
    public async Task MIG12_TheMigrationContainsNoDestructiveStatementAndNoSeedRow()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var migrationsDirectory = Path.Combine(
            P2T03ProductionScan.RepositoryRoot(),
            "src",
            "DMO.Infrastructure",
            "Migrations");
        var migrationFile = Assert.Single(
            Directory.GetFiles(migrationsDirectory, "*_ToolJobOnDomainCore.cs"),
            path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var source = await File.ReadAllTextAsync(migrationFile);

        const string upMarker = "protected override void Up(MigrationBuilder migrationBuilder)";
        const string downMarker = "protected override void Down(MigrationBuilder migrationBuilder)";
        var upStart = source.IndexOf(upMarker, StringComparison.Ordinal);
        var downStart = source.IndexOf(downMarker, StringComparison.Ordinal);
        Assert.True(upStart >= 0, "The migration has no Up method.");
        Assert.True(downStart > upStart, "The migration has no Down method after Up.");

        var up = source[upStart..downStart];

        // Up creates; it never drops, truncates or resets anything.
        Assert.DoesNotContain("drop", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter column", up, StringComparison.OrdinalIgnoreCase);

        // No raw SQL escape hatch and no seed/reference-data row anywhere in the migration.
        Assert.DoesNotContain("migrationBuilder.Sql(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteData", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InsertData", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HasData", source, StringComparison.Ordinal);
        Assert.DoesNotContain("insert", source, StringComparison.OrdinalIgnoreCase);

        // Down is exactly the contracted six table drops â€” no schema reset, no other statement.
        var down = source[downStart..];
        var dropped = Regex.Matches(down, "name: \"(?<name>[a-z_]+)\"")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal(Sorted(DomainCoreTables.ToList()), Sorted(dropped));
        Assert.Equal(6, Regex.Matches(down, "DropTable", RegexOptions.None).Count);
        Assert.DoesNotContain("migrationBuilder.Sql(", down, StringComparison.Ordinal);
    }

    /// <summary>The eighteen violating-row cases, one per contracted CHECK constraint (Â§4.4).</summary>
    private static IEnumerable<(string ConstraintName, string Sql)> ViolationCases(
        string token,
        Guid cmToolId,
        Guid mfToolId,
        Guid bqToolId,
        Guid cmJobOnId,
        Guid mfJobOnId,
        Guid bqJobOnId)
    {
        // tools (5 checks).
        yield return ("tools_type_check",
            $"INSERT INTO tools (tool_id, tool_type, reference, lot) " +
            $"VALUES ('{Guid.NewGuid()}', 'XX', 'mig3-bad-type-{token}', 'mig3-lot-{token}')");
        yield return ("tools_reference_required_check",
            $"INSERT INTO tools (tool_id, tool_type, reference, lot) " +
            $"VALUES ('{Guid.NewGuid()}', 'CM', '   ', 'mig3-lot-{token}')");
        yield return ("tools_lot_required_check",
            $"INSERT INTO tools (tool_id, tool_type, reference, lot) " +
            $"VALUES ('{Guid.NewGuid()}', 'CM', 'mig3-bad-lot-{token}', '')");
        yield return ("tools_processo_check",
            $"INSERT INTO tools (tool_id, tool_type, reference, lot, processo) " +
            $"VALUES ('{Guid.NewGuid()}', 'CM', 'mig3-bad-processo-{token}', 'mig3-lot-{token}', 'XX')");
        yield return ("tools_quantity_check",
            $"INSERT INTO tools (tool_id, tool_type, reference, lot, quantity) " +
            $"VALUES ('{Guid.NewGuid()}', 'CM', 'mig3-bad-quantity-{token}', 'mig3-lot-{token}', -1)");

        // tool_machines (1 check).
        yield return ("tool_machines_machine_check",
            $"INSERT INTO tool_machines (tool_machine_id, tool_id, machine) " +
            $"VALUES ('{Guid.NewGuid()}', '{cmToolId}', 'B4')");

        // job_ons (3 checks).
        yield return ("job_ons_reference_required_check",
            $"INSERT INTO job_ons (jobon_id, reference, production_number, machine) " +
            $"VALUES ('{Guid.NewGuid()}', '  ', 'mig3-pn-ref-{token}', 'B1')");
        yield return ("job_ons_production_number_required_check",
            $"INSERT INTO job_ons (jobon_id, reference, production_number, machine) " +
            $"VALUES ('{Guid.NewGuid()}', 'mig3-pn-req-{token}', '', 'B1')");
        yield return ("job_ons_machine_check",
            $"INSERT INTO job_ons (jobon_id, reference, production_number, machine) " +
            $"VALUES ('{Guid.NewGuid()}', 'mig3-machine-{token}', 'mig3-pn-machine-{token}', 'LINHA B')");

        // cm_contexts (3 checks).
        yield return ("cm_contexts_tool_type_check",
            $"INSERT INTO cm_contexts (cm_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{cmJobOnId}', '{cmToolId}', 'MF', " +
            $"'mig3-ctx-{token}', 'mig3-lot-{token}')");
        yield return ("cm_contexts_tool_reference_required_check",
            $"INSERT INTO cm_contexts (cm_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{cmJobOnId}', '{cmToolId}', 'CM', '  ', 'mig3-lot-{token}')");
        yield return ("cm_contexts_tool_lot_required_check",
            $"INSERT INTO cm_contexts (cm_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{cmJobOnId}', '{cmToolId}', 'CM', 'mig3-ctx-{token}', '')");

        // mf_contexts (3 checks).
        yield return ("mf_contexts_tool_type_check",
            $"INSERT INTO mf_contexts (mf_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{mfJobOnId}', '{mfToolId}', 'CM', " +
            $"'mig3-ctx-{token}', 'mig3-lot-{token}')");
        yield return ("mf_contexts_tool_reference_required_check",
            $"INSERT INTO mf_contexts (mf_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{mfJobOnId}', '{mfToolId}', 'MF', '  ', 'mig3-lot-{token}')");
        yield return ("mf_contexts_tool_lot_required_check",
            $"INSERT INTO mf_contexts (mf_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{mfJobOnId}', '{mfToolId}', 'MF', 'mig3-ctx-{token}', '')");

        // bq_contexts (3 checks).
        yield return ("bq_contexts_tool_type_check",
            $"INSERT INTO bq_contexts (bq_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{bqJobOnId}', '{bqToolId}', 'CM', " +
            $"'mig3-ctx-{token}', 'mig3-lot-{token}')");
        yield return ("bq_contexts_tool_reference_required_check",
            $"INSERT INTO bq_contexts (bq_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{bqJobOnId}', '{bqToolId}', 'BQ', '  ', 'mig3-lot-{token}')");
        yield return ("bq_contexts_tool_lot_required_check",
            $"INSERT INTO bq_contexts (bq_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot) " +
            $"VALUES ('{Guid.NewGuid()}', '{bqJobOnId}', '{bqToolId}', 'BQ', 'mig3-ctx-{token}', '')");
    }

    private static Task InsertToolAsync(
        DmoDbContext context,
        Guid toolId,
        string toolType,
        string reference,
        string lot) =>
        context.Database.ExecuteSqlRawAsync(
            "INSERT INTO tools (tool_id, tool_type, reference, lot) VALUES (@id, @type, @reference, @lot)",
            new NpgsqlParameter("id", toolId),
            new NpgsqlParameter("type", toolType),
            new NpgsqlParameter("reference", reference),
            new NpgsqlParameter("lot", lot));

    private static Task InsertJobOnAsync(
        DmoDbContext context,
        Guid jobOnId,
        string reference,
        string productionNumber) =>
        context.Database.ExecuteSqlRawAsync(
            "INSERT INTO job_ons (jobon_id, reference, production_number, machine) " +
            "VALUES (@id, @reference, @production, 'B1')",
            new NpgsqlParameter("id", jobOnId),
            new NpgsqlParameter("reference", reference),
            new NpgsqlParameter("production", productionNumber));

    /// <summary>Reads and normalizes the index register of the six contracted tables.</summary>
    private static async Task<IReadOnlyList<IndexContract>> ReadIndexesAsync(DmoDbContext context)
    {
        var rows = await QueryStringsAsync(
            context,
            "SELECT tablename || '|' || indexname || '|' || indexdef FROM pg_indexes " +
            "WHERE schemaname = 'public' AND tablename IN " +
            "('tools','tool_machines','job_ons','cm_contexts','mf_contexts','bq_contexts')");

        return rows.Select(row =>
        {
            var parts = row.Split('|', 3);
            var definition = parts[2];
            var columns = Regex.Match(definition, @"\(([^)]*)\)").Groups[1].Value
                .Replace(" ", string.Empty, StringComparison.Ordinal);

            return new IndexContract(
                parts[0],
                parts[1],
                definition.Contains("UNIQUE", StringComparison.Ordinal),
                columns);
        }).ToList();
    }

    /// <summary>The catalog state of the four foundation tables: columns, constraints and indexes.</summary>
    private static async Task<IReadOnlyList<string>> FoundationCatalogAsync(DmoDbContext context)
    {
        var columns = await QueryStringsAsync(
            context,
            "SELECT 'column:' || table_name || '|' || column_name || '|' || data_type || '|' || is_nullable || " +
            "'|' || coalesce(column_default, '') FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name IN " +
            "('templates','template_modules','users','admin_accounts')");

        var constraints = await QueryStringsAsync(
            context,
            "SELECT 'constraint:' || conrelid::regclass::text || '|' || conname || '|' || contype::text || " +
            "'|' || pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid IN " +
            "('templates'::regclass,'template_modules'::regclass,'users'::regclass," +
            "'admin_accounts'::regclass)");

        var indexes = await QueryStringsAsync(
            context,
            "SELECT 'index:' || tablename || '|' || indexname || '|' || indexdef FROM pg_indexes " +
            "WHERE schemaname = 'public' AND tablename IN " +
            "('templates','template_modules','users','admin_accounts')");

        return [.. columns, .. constraints, .. indexes];
    }

    /// <summary>The complete catalog state of the ten product tables (row MIG7).</summary>
    private static async Task<IReadOnlyList<string>> SchemaCatalogAsync(DmoDbContext context)
    {
        var columns = await QueryStringsAsync(
            context,
            "SELECT 'column:' || table_name || '|' || column_name || '|' || data_type || '|' || is_nullable || " +
            "'|' || coalesce(column_default, '') FROM information_schema.columns " +
            "WHERE table_schema = 'public'");

        var constraints = await QueryStringsAsync(
            context,
            "SELECT 'constraint:' || conrelid::regclass::text || '|' || conname || '|' || contype::text || " +
            "'|' || pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace = 'public'::regnamespace");

        var indexes = await QueryStringsAsync(
            context,
            "SELECT 'index:' || tablename || '|' || indexname || '|' || indexdef FROM pg_indexes " +
            "WHERE schemaname = 'public'");

        return [.. columns, .. constraints, .. indexes];
    }

    /// <summary>Resolves the migration immediately below the P2-T04 migration.</summary>
    private static string PreviousMigrationIdOf(DmoDbContext context)
    {
        var migrations = context.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(
            migration => migration.EndsWith(ToolJobOnMigrationName, StringComparison.Ordinal));

        Assert.True(index > 0, "The P2-T04 migration is not the last known migration.");

        return migrations[index - 1];
    }

    /// <summary>Extracts the <c>ToTable("â€¦")</c> table names of a model snapshot or designer file.</summary>
    private static IReadOnlyList<string> TablesOf(string source) =>
        Regex.Matches(source, "ToTable\\(\"(?<name>[a-z_]+)\"")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

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

    /// <summary>One contracted column of Â§3.</summary>
    private sealed record ColumnContract(string Table, string Column, string DataType, string Nullable, string? Default)
    {
        /// <summary>The normalized catalog rendering of this column.</summary>
        public string Render() => $"{Table}|{Column}|{DataType}|{Nullable}|{Default ?? string.Empty}";
    }

    /// <summary>One contracted index of Â§4.5 (its column list normalized without spaces).</summary>
    private sealed record IndexContract(string Table, string Name, bool Unique, string Columns)
    {
        /// <summary>The normalized catalog rendering of this index.</summary>
        public string Render() => $"{Table}|{Name}|{Unique}|{Columns}";
    }

    /// <summary>One contracted foreign key of Â§4.3.</summary>
    private sealed record ForeignKeyContract(
        string Name,
        string Table,
        string Column,
        string PrincipalTable,
        string PrincipalColumn);

    /// <summary>One <c>pg_constraint</c> foreign-key row.</summary>
    private sealed record ForeignKeyRow(string Table, string Name, string DeleteType, string Definition);
}
