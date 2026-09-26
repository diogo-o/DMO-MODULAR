using System.Data;
using System.Text.RegularExpressions;
using DMO.Infrastructure.Persistence;
using DMO.IntegrationTests.JobOn;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table names and row
// identifiers) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T07 env-gated integration test — Migration 007 (<c>BoquilhasDomain</c>, OWNER
/// CLARIFICATION corrected pair): exactly the THREE final tables with the exact
/// columns/CHECKs/RESTRICT FKs (the register identity, the movement ledger, the movement audit),
/// the plain one-register-per-BQ-context unique key, the exact <c>Down</c> behaviour and the
/// no-drift re-apply, against a disposable PostgreSQL database.
/// </summary>
/// <remarks>
/// Authority: P2-T07 OWNER CLARIFICATION (schema/migration strategy: P2-T07 is NOT closed and
/// migration 007 was never accepted, so the schema was corrected cleanly — the lifecycle tables
/// <c>boquilha_close_snapshots</c>/<c>boquilha_reopenings</c>/<c>boquilha_machines</c>, the
/// <c>status</c> column and the ACTIVE partial unique indexes are GONE, with no compensating
/// legacy migration). Physical facts for the report: 20 product tables before P2-T07; +3 = 23
/// product tables post-migration; +<c>__EFMigrationsHistory</c> = 24 raw.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class Migration007BoquilhasDomainTests
{
    /// <summary>EF's own migration bookkeeping table (never a product table).</summary>
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>The seventh migration (this slice owns the corrected OWNER pair).</summary>
    private const string BoquilhasMigrationId = "20260924051151_BoquilhasDomain";

    /// <summary>The migration the seventh applies ON TOP of (the Down target).</summary>
    private const string ControloApproveMigrationId = "20260923171223_ControloApproveDomain";

    /// <summary>The eighth migration — the §34 OWNER-clarification delta (pré-JobOn anchor).</summary>
    private const string BoquilhasPreJobonMigrationId = "20260924130151_BoquilhasPreJobonAssociation";

    /// <summary>The THREE final Boquilhas tables.</summary>
    private static readonly string[] ThreeTables =
    [
        "boquilha_movement_audit",
        "boquilha_movements",
        "boquilhas",
    ];

    /// <summary>The TEN migrations, in generation order (the P2-T08 delta adds the NINTH and the
    /// Peso Comparação slice adds the TENTH).</summary>
    private static readonly string[] AllMigrationIds =
    [
        "20260922001736_AccountAndTemplateFoundation",
        "20260922001757_TemplateModuleComposition",
        "20260922232349_ToolJobOnDomainCore",
        "20260923045054_ControloCreateDomain",
        "20260923122429_GlassDensitySettings",
        ControloApproveMigrationId,
        BoquilhasMigrationId,
        BoquilhasPreJobonMigrationId,
        "20260924182527_EmailTemplateGroupRouting",
        "20260925071139_ControloComparacaoDomain",
    ];

    /// <summary>
    /// MG1 (AC-MG1, corrected) — applying all ten migrations to a reset schema leaves exactly the
    /// ten contracted migrations in <c>__EFMigrationsHistory</c> and 27 raw tables (26 product
    /// tables: 20 closed + exactly THREE Boquilhas tables — migration 008 alters the register, it
    /// adds no table — plus the THREE Comparação tables — plus EF's history row).
    /// </summary>
    [SkippableFact]
    public async Task MG1_ExactlyThreeNewTablesAndTheSeventhMigrationAreApplied()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var history = Sorted(await QueryStringsAsync(context, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\""));
        Assert.Equal(Sorted(AllMigrationIds.ToList()), history);

        var tables = Sorted(await QueryStringsAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"));

        Assert.Equal(27, tables.Count); // 20 closed + THREE Boquilhas + THREE Comparação + __EFMigrationsHistory
        foreach (var table in ThreeTables)
        {
            Assert.Contains(table, tables);
        }

        // The lifecycle tables are GONE from the final schema (no compensating legacy).
        foreach (var removed in new[] { "boquilha_close_snapshots", "boquilha_reopenings", "boquilha_machines" })
        {
            Assert.DoesNotContain(removed, tables);
        }
    }

    /// <summary>
    /// MG1 (AC-MG1, corrected) — the migration source creates exactly the three contracted tables
    /// and NO other table/column/statement; <c>Down</c> drops exactly those three in a referentially
    /// safe order; no partial-index predicate ("WHERE") exists anywhere in the source.
    /// </summary>
    [SkippableFact]
    public async Task MG1_TheMigrationSourceCreatesExactlyTheThreeContractedTables()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        var source = Framework_Read("src/DMO.Infrastructure/Migrations/20260924051151_BoquilhasDomain.cs");

        var created = Regex.Matches(source, @"CreateTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(Sorted(ThreeTables.ToList()), created);

        var dropped = Regex.Matches(source, @"DropTable\(\s*name:\s*""(?<name>[a-z_]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToList();
        Assert.Equal(ThreeTables.Length, dropped.Count);
        Assert.Equal(Sorted(ThreeTables.ToList()), Sorted(dropped));

        Assert.DoesNotContain("AddColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DropColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RenameTable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InsertData", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateSequence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", source, StringComparison.Ordinal); // no partial index survives
    }

    /// <summary>
    /// MG1 (AC-MG1 superseded, corrected) — NO ACTIVE partial unique index exists: the register
    /// carries the plain <c>boquilhas_bq_id_key</c> UNIQUE (one register per REAL BQ context) and no
    /// <c>IX_boquilhas_active_*</c> index at all.
    /// </summary>
    [SkippableFact]
    public async Task MG1_NoActivePartialUniqueIndexes_OnlyThePlainBqIdUniqueKey()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var indexes = await QueryStringsAsync(
            context,
            "SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'boquilhas'");

        var bqKey = indexes.FirstOrDefault(line => line.StartsWith("boquilhas_bq_id_key|", StringComparison.Ordinal));
        Assert.NotNull(bqKey);
        Assert.Contains("CREATE UNIQUE INDEX", bqKey, StringComparison.Ordinal);
        Assert.Contains("(bq_id)", bqKey, StringComparison.Ordinal);

        Assert.DoesNotContain(indexes, line => line.Contains("_active_", StringComparison.Ordinal));
        Assert.DoesNotContain(indexes, line => line.Contains("(status = ", StringComparison.Ordinal));
    }

    /// <summary>
    /// MG2 (AC-MG2, corrected; §34 delta) — the three tables' columns/CHECKs/FKs match the register
    /// model: the exact column sets (the register identity row with the TRANSITIONAL pré-JobOn
    /// anchor — <c>tool_id</c> nullable + <c>bq_id</c> nullable by the exactly-one-anchor CHECK of
    /// migration 008 — and no <c>status</c>/opening-facts; no expected/excess on movements), the
    /// exact CHECK names (the closed three-type set, the Saída-required rule, the anchor XOR and
    /// the version token) and <c>confdeltype='r'</c> (no cascade).
    /// </summary>
    [SkippableFact]
    public async Task MG2_TheBoquilhasConstraintsMatchTheRegisterModelExactly()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // Columns of boquilhas (exact set, exact nullability) — the register identity row with
        // the §34 transitional anchor (migration 008): bq_id XOR tool_id, NEVER both/neither.
        var boquilhasColumns = await QueryStringsAsync(
            context,
            "SELECT column_name, is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'boquilhas'");
        var expectedBoquilhasColumns = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["boquilhas_id"] = false,
            ["bq_id"] = true, // NULL while the register is in the transitional pré-JobOn state (§34)
            ["tool_id"] = true, // the provisional canonical BQ Tool anchor (§34)
            ["version"] = false, // the sole register-write (the association) optimistic token (§34)
            ["created_by_user_id"] = false,
            ["created_at"] = false,
        };
        var expectedColumns = expectedBoquilhasColumns
            .Select(entry => $"{entry.Key}|{(entry.Value ? "YES" : "NO")}")
            .ToList();
        Assert.Equal(
            Sorted(expectedColumns),
            Sorted(boquilhasColumns));

        // No lifecycle/opening-fact column survives.
        Assert.DoesNotContain(boquilhasColumns, line => line.StartsWith("status|", StringComparison.Ordinal));
        Assert.DoesNotContain(boquilhasColumns, line => line.StartsWith("opening_date|", StringComparison.Ordinal));
        Assert.DoesNotContain(boquilhasColumns, line => line.StartsWith("utilisation_percent|", StringComparison.Ordinal));

        // The §34 anchor CHECK of migration 008: exactly ONE anchor (the DB backstop of the
        // validator's XOR rule; tool_id and bq_id can never be two concurrent authorities).
        var registerChecks = await QueryStringsAsync(
            context,
            "SELECT conname FROM pg_constraint WHERE conrelid = 'boquilhas'::regclass AND contype = 'c'");
        Assert.Contains("boquilhas_anchor_check", registerChecks);
        Assert.Contains("boquilhas_version_check", registerChecks);

        // CHECK constraints of boquilha_movements (the closed three-type set + Saída-required).
        var movementChecks = await QueryStringsAsync(
            context,
            "SELECT conname FROM pg_constraint WHERE conrelid = 'boquilha_movements'::regclass AND contype = 'c'");
        foreach (var expected in new[]
                 {
                     "boquilha_movements_type_check",
                     "boquilha_movements_quantity_check",
                     "boquilha_movements_machine_check",
                     "boquilha_movements_saida_required_check",
                     "boquilha_movements_observations_check",
                     "boquilha_movements_version_check",
                 })
        {
            Assert.Contains(expected, movementChecks);
        }

        // All FKs are RESTRICT (confdeltype 'r'), never cascade (2 on boquilhas +
        // 3 on boquilha_movements + 2 on boquilha_movement_audit + the §34 delta's Tool anchor FK
        // on boquilhas (FK_boquilhas_tools_tool_id) = 8).
        var fks = await QueryStringsAsync(
            context,
            "SELECT conrelid::regclass::text, conname, confdeltype FROM pg_constraint " +
            "WHERE contype = 'f' AND conrelid IN ('boquilhas'::regclass, " +
            "'boquilha_movements'::regclass, 'boquilha_movement_audit'::regclass)");
        Assert.Equal(8, fks.Count);
        Assert.All(fks, line => Assert.EndsWith("|r", line, StringComparison.Ordinal));
        Assert.Contains(fks, line => line.Contains("FK_boquilhas_tools_tool_id", StringComparison.Ordinal));
    }

    /// <summary>
    /// MG2 (AC-MG2, corrected) — direct-violation inserts map 23514 (the accepted backstop, never a
    /// 500): the movement-type CHECK (a superseded type), the quantity CHECK, the Saída-required
    /// CHECK and the machine CHECK.
    /// </summary>
    [SkippableFact]
    public async Task MG2_DirectViolationsAreSqlState23514()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        // The bq_id unique key maps 23505 (one register per production/BQ context): the register
        // must anchor a REAL bq_contexts row first (the production association is mandatory).
        var userId = Guid.NewGuid();
        var jobOnId = Guid.NewGuid();
        var bqId = Guid.NewGuid();
        var toolId = Guid.NewGuid();

        await ExecuteAsync(
            connection,
            "INSERT INTO users (user_id, auth_identity_id, company_number, name, email, active, version) " +
            "VALUES (@user, 'auth-1', 'c1', 'u1', 'u1@example.test', true, 1)",
            ("user", (object)userId));

        await ExecuteAsync(
            connection,
            "INSERT INTO tools (tool_id, tool_type, reference, lot, processo, quantity, created_at, updated_at) " +
            "VALUES (@tool, 'BQ', 'BQ-1', '01', 'NNPB', 10, now(), now())",
            ("tool", (object)toolId));

        await ExecuteAsync(
            connection,
            "INSERT INTO job_ons (jobon_id, reference, production_number, machine, production_date, version, created_at, updated_at) " +
            "VALUES (@jobon, 'REF-1', 'P-1', 'B1', NULL, 1, now(), now())",
            ("jobon", (object)jobOnId));

        await ExecuteAsync(
            connection,
            "INSERT INTO bq_contexts (bq_id, jobon_id, tool_id, tool_type, tool_reference, tool_lot, created_at, updated_at) " +
            "VALUES (@bq, @jobon, @tool, 'BQ', 'BQ-1', '01', now(), now())",
            ("bq", (object)bqId),
            ("jobon", (object)jobOnId),
            ("tool", (object)toolId));

        await ExecuteAsync(
            connection,
            "INSERT INTO boquilhas (boquilhas_id, bq_id, created_by_user_id, created_at) " +
            "VALUES (@id, @bq, @user, now())",
            ("id", (object)Guid.NewGuid()),
            ("bq", (object)bqId),
            ("user", (object)userId));

        await AssertThrowsSqlStateAsync(
            connection,
            "INSERT INTO boquilhas (boquilhas_id, bq_id, created_by_user_id, created_at) " +
            "VALUES (@id, @bq, @user, now())",
            "23505",
            ("id", (object)Guid.NewGuid()),
            ("bq", (object)bqId),
            ("user", (object)userId));

        var registerId = await QueryGuidAsync(
            connection,
            "SELECT boquilhas_id FROM boquilhas ORDER BY created_at LIMIT 1");

        // Movement-type CHECK: a superseded type (23514).
        await AssertThrowsSqlStateAsync(
            connection,
            "INSERT INTO boquilha_movements (movement_id, boquilhas_id, movement_type, quantity, business_date, " +
            "recorded_at, recorded_by_user_id, version) " +
            "VALUES (@id, @bq, 'inicio', 1, '2026-09-10', now(), @user, 1)",
            "23514",
            ("id", (object)Guid.NewGuid()),
            ("bq", (object)registerId),
            ("user", (object)userId));

        // Quantity CHECK (23514).
        await AssertThrowsSqlStateAsync(
            connection,
            "INSERT INTO boquilha_movements (movement_id, boquilhas_id, movement_type, quantity, business_date, " +
            "recorded_at, recorded_by_user_id, version) " +
            "VALUES (@id, @bq, 'entrada', 0, '2026-09-10', now(), @user, 1)",
            "23514",
            ("id", (object)Guid.NewGuid()),
            ("bq", (object)registerId),
            ("user", (object)userId));

        // Saída-required CHECK: a Saída without machine/repairer (23514).
        await AssertThrowsSqlStateAsync(
            connection,
            "INSERT INTO boquilha_movements (movement_id, boquilhas_id, movement_type, quantity, business_date, " +
            "recorded_at, recorded_by_user_id, version) " +
            "VALUES (@id, @bq, 'saida', 1, '2026-09-10', now(), @user, 1)",
            "23514",
            ("id", (object)Guid.NewGuid()),
            ("bq", (object)registerId),
            ("user", (object)userId));
    }

    /// <summary>
    /// MG3 (AC-MG3, corrected) — the REAL <c>Down</c> (EF migrator to the previous migration) removes
    /// ONLY the P2-T07 state — the 21-raw-table state (20 product tables + history) is restored;
    /// re-applying is idempotent (27 raw, incl. the Comparação tables of this slice).
    /// </summary>
    [SkippableFact]
    public async Task MG3_DownRemovesOnlyP2T07StateAndReApplyIsIdempotent()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ResetSchemaAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        // The REAL EF migrator applies migration 007's Down (back to the P2-T06 migration).
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(ControloApproveMigrationId);

        var tables = await QueryStringsAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'");
        // Physical raw count: 20 pre-P2-T07 product tables + __EFMigrationsHistory = 21.
        Assert.Equal(21, tables.Count);
        foreach (var table in ThreeTables)
        {
            Assert.DoesNotContain(table, tables);
        }

        var history = await QueryStringsAsync(context, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"");
        Assert.DoesNotContain(history, line => line == BoquilhasMigrationId);
        Assert.Contains(history, line => line == ControloApproveMigrationId);

        // Re-apply forward and again — idempotent (26 product tables + history = 27 raw).
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var reapplied = await QueryStringsAsync(
            context,
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'");
        Assert.Equal(27, reapplied.Count);
    }

    // ------------------------------------------------------------------ helpers

    private static string Framework_Read(string path) => P2T04ProductionScan.Read(path);

    private static List<string> Sorted(List<string> values)
    {
        values.Sort(StringComparer.Ordinal);
        return values;
    }

    private static async Task<List<string>> QueryStringsAsync(DmoDbContext context, string sql)
    {
        var rows = new List<string>();
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
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.GetValue(index)?.ToString() ?? string.Empty;
            }

            rows.Add(string.Join('|', values));
        }

        return rows;
    }

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

    private static async Task<Guid> QueryGuidAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return (Guid)result!;
    }

    private static async Task AssertThrowsSqlStateAsync(
        NpgsqlConnection connection,
        string sql,
        string sqlState,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, value));
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(sqlState, exception.SqlState);
    }
}