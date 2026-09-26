using System.Data;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T04 env-gated integration test — the canonical Tool registry over the disposable PostgreSQL
/// database: identity tuple, duplicate prevention, atomic create, deterministic bounded search,
/// machine-compatibility filter and the reverse usage read.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §5 (canonical Tool rules), §8.2/§8.3 (search and reverse read) and
/// §20.1/§20.3 (rows TOL1, TOL5, TOL6, TOL7, TOL9, TOL11, TOL12, TOL19, CTX12, CTX14). Every
/// assertion is scoped to the rows this test creates (fresh Guid and per-test reference token).
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class ToolRepositoryIntegrationTests
{
    /// <summary>
    /// TOL1 (AC-3): <c>(CM, "5447T173", "12")</c> and <c>(CM, "5447T173", "13")</c> are two distinct
    /// Tools with distinct <c>tool_id</c>s, and the second create is accepted.
    /// </summary>
    [SkippableFact]
    public async Task TOL1_DifferentLotYieldsADifferentToolIdAndTheSecondCreateIsAccepted()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var service = ToolService(context);

            var first = Assert.IsType<ToolResult.Created>(await service.CreateAsync(
                new CreateToolCommand("CM", reference, "12", "NNPB", 10, ["B1", "B2"]),
                CancellationToken.None));

            // Same reference, DIFFERENT lot: a different canonical Tool, and the create is accepted.
            var second = Assert.IsType<ToolResult.Created>(await service.CreateAsync(
                new CreateToolCommand("CM", reference, "13", null, null, ["B3"]),
                CancellationToken.None));

            Assert.NotEqual(first.ToolId, second.ToolId);

            var rows = await QueryStringsAsync(
                context,
                "SELECT tool_id || '|' || lot || '|' || tool_type FROM tools WHERE reference = @p",
                new NpgsqlParameter("p", reference));

            Assert.Equal(
                Sorted([$"{first.ToolId}|12|CM", $"{second.ToolId}|13|CM"]),
                Sorted(rows));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// TOL5 (AC-4): a second <c>tools</c> row with the same (<c>tool_type</c>, <c>reference</c>,
    /// <c>lot</c>) is rejected by <c>tools_type_reference_lot_key</c> with <c>23505</c>.
    /// </summary>
    [SkippableFact]
    public async Task TOL5_SecondRowWithTheSameIdentityTupleIsRejectedByTheUniqueIndex()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            await InsertToolAsync(context, Guid.NewGuid(), "CM", reference, "12");

            var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertToolAsync(context, Guid.NewGuid(), "CM", reference, "12"));

            Assert.Equal("23505", duplicate.SqlState);
            Assert.Equal(
                ToolEntityConfiguration.TypeReferenceLotUniqueConstraintName,
                duplicate.ConstraintName);

            // The tuple includes the lot: the same reference with another lot is a different Tool.
            await InsertToolAsync(context, Guid.NewGuid(), "CM", reference, "13");

            Assert.Equal(
                2,
                await CountAsync(context, "tools WHERE reference = @p", new NpgsqlParameter("p", reference)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// TOL6 (AC-4): the duplicate-prevention query finds the existing Tool and the create is refused
    /// with its <c>tool_id</c>; exactly one row exists afterwards.
    /// </summary>
    [SkippableFact]
    public async Task TOL6_DuplicatePreventionNamesTheExistingToolIdAndWritesNoSecondRow()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var service = ToolService(context);

            var created = Assert.IsType<ToolResult.Created>(await service.CreateAsync(
                new CreateToolCommand("CM", reference, "12", null, 5, ["B1"]),
                CancellationToken.None));

            // The same identity tuple, a different compatibility set: still the same Tool identity.
            var refused = Assert.IsType<ToolResult.DuplicateIdentity>(await service.CreateAsync(
                new CreateToolCommand("CM", reference, "12", "PS", 99, ["C3"]),
                CancellationToken.None));

            Assert.Equal(created.ToolId, refused.ExistingToolId);
            Assert.False(string.IsNullOrWhiteSpace(refused.Message));

            Assert.Equal(
                1,
                await CountAsync(
                    context,
                    "tools WHERE tool_type = 'CM' AND reference = @p AND lot = '12'",
                    new NpgsqlParameter("p", reference)));
            Assert.Equal(
                1,
                await CountAsync(
                    context,
                    "tool_machines WHERE tool_id = @p",
                    new NpgsqlParameter("p", created.ToolId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// TOL7 (AC-8): Tool create writes the Tool and its full compatibility set as one unit — a forced
    /// failure at the write leaves no <c>tools</c> row and no orphan <c>tool_machines</c> row.
    /// </summary>
    [SkippableFact]
    public async Task TOL7_ToolCreateIsOneUnitAndAForcedWriteFailureLeavesNoRowAtAll()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var lot = $"lot-{token}";
        var toolId = ToolId.New();
        MachineCode[] machines = [MachineCode.From("B1"), MachineCode.From("B2")];

        try
        {
            // The failure is forced at the single write of ToolRepository.CreatedAsync, i.e. at the
            // save that carries the Tool row together with its compatibility rows.
            var interceptor = new FailOnNthSaveInterceptor(failOn: 1);
            await using var marked = CreateMarkedContext(interceptor);

            var repository = new ToolRepository(marked);
            var tool = new Tool(toolId, ToolType.Cm, reference, lot, Processo.Nnpb, 5, machines);

            await Assert.ThrowsAsync<ForcedSaveFailureException>(
                () => repository.CreatedAsync(tool, machines, CancellationToken.None));

            Assert.Equal(1, interceptor.Calls);
            Assert.Contains("ToolEntity:Added", interceptor.FailedSaveEntries);
            Assert.Contains("ToolMachineEntity:Added", interceptor.FailedSaveEntries);

            // Nothing of the unit survived the failure.
            Assert.Equal(
                0,
                await CountAsync(context, "tools WHERE tool_id = @p", new NpgsqlParameter("p", toolId.Value)));
            Assert.Equal(
                0,
                await CountAsync(context, "tool_machines WHERE tool_id = @p", new NpgsqlParameter("p", toolId.Value)));

            // The identity is still free and a healthy create persists Tool + full compatibility set.
            var created = Assert.IsType<ToolResult.Created>(await ToolService(context).CreateAsync(
                new CreateToolCommand("CM", reference, lot, "NNPB", 5, ["B1", "B2"]),
                CancellationToken.None));

            Assert.Equal(
                2,
                await CountAsync(context, "tool_machines WHERE tool_id = @p", new NpgsqlParameter("p", created.ToolId)));

            var persisted = await new ToolRepository(context).GetByIdAsync(created.ToolId, CancellationToken.None);
            Assert.Equal(new[] { "B1", "B2" }, persisted!.CompatibleMachines.Select(machine => machine.Value).ToArray());
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// TOL9 (AC-11): two identical searches return the identical contracted order
    /// (<c>tool_type, reference, lot, tool_id</c>), and that order does not depend on the order the
    /// rows were inserted in.
    /// </summary>
    [SkippableFact]
    public async Task TOL9_TwoIdenticalSearchesReturnTheIdenticalContractedOrder()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var reverseReference = $"rev-{token}";

        try
        {
            var service = ToolService(context);

            // Inserted in a deliberately scrambled order.
            await CreateToolAsync(service, "MF", reference, "05");
            await CreateToolAsync(service, "CM", reference, "09");
            await CreateToolAsync(service, "BQ", reference, "02");
            await CreateToolAsync(service, "CM", reference, "01");

            // The same four identity tuples, inserted through raw SQL in the REVERSE order.
            await InsertToolAsync(context, Guid.NewGuid(), "CM", reverseReference, "01");
            await InsertToolAsync(context, Guid.NewGuid(), "BQ", reverseReference, "02");
            await InsertToolAsync(context, Guid.NewGuid(), "CM", reverseReference, "09");
            await InsertToolAsync(context, Guid.NewGuid(), "MF", reverseReference, "05");

            string[] expected = ["BQ|02", "CM|01", "CM|09", "MF|05"];

            var first = await SearchOrderAsync(context, reference);
            var second = await SearchOrderAsync(context, reference);

            Assert.Equal(expected, first);
            Assert.Equal(first, second);

            // Insertion order is irrelevant: the same order comes out of the reverse-inserted set.
            Assert.Equal(expected, await SearchOrderAsync(context, reverseReference));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// TOL11 (AC-42): with five matching Tools, <c>Limit = 2</c> returns exactly two items — the
    /// limit is honoured at the database level.
    /// </summary>
    [SkippableFact]
    public async Task TOL11_WithFiveMatchingToolsTheLimitReturnsExactlyTwoItems()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var service = ToolService(context);
            foreach (var lot in new[] { "05", "03", "01", "04", "02" })
            {
                await CreateToolAsync(service, "CM", reference, lot);
            }

            Assert.Equal(
                5,
                await CountAsync(context, "tools WHERE reference = @p", new NpgsqlParameter("p", reference)));

            var result = Assert.IsType<ToolResult.SearchResults>(await service.SearchAsync(
                new ToolSearchQuery(null, ToolType.Cm, reference, null, null, Limit: 2),
                CancellationToken.None));

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(new[] { "01", "02" }, result.Items.Select(item => item.Lot).ToArray());
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// TOL12 (AC-10): a <c>Machine</c> filter returns only Tools having that <c>tool_machines</c>
    /// row, and a Tool compatible with two machines appears in both filtered searches.
    /// </summary>
    [SkippableFact]
    public async Task TOL12_TheMachineFilterReturnsOnlyToolsRegisteredForThatMachine()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var service = ToolService(context);

            var bothMachines = Assert.IsType<ToolResult.Created>(await service.CreateAsync(
                new CreateToolCommand("CM", reference, "01", null, null, ["B1", "B2"]),
                CancellationToken.None));
            var otherMachine = Assert.IsType<ToolResult.Created>(await service.CreateAsync(
                new CreateToolCommand("CM", reference, "02", null, null, ["C1"]),
                CancellationToken.None));

            var forB1 = await SearchAsync(context, reference, "B1");
            var forB2 = await SearchAsync(context, reference, "B2");
            var forC1 = await SearchAsync(context, reference, "C1");

            // Compatible with B1 AND B2: present in both filtered searches.
            Assert.Equal(new[] { bothMachines.ToolId }, forB1.ToArray());
            Assert.Equal(new[] { bothMachines.ToolId }, forB2.ToArray());

            // Registered only for C1: absent from the B1/B2 searches.
            Assert.Equal(new[] { otherMachine.ToolId }, forC1.ToArray());
            Assert.DoesNotContain(otherMachine.ToolId, forB1);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// TOL19 (AC-12): deleting a <c>tools</c> row referenced by a compatibility row or by a context
    /// row is rejected by the <c>RESTRICT</c> foreign key, and nothing is deleted.
    /// </summary>
    [SkippableFact]
    public async Task TOL19_DeletingAReferencedToolIsRejectedByTheRestrictForeignKey()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            // Referenced by a compatibility row only.
            var compatibleOnly = Assert.IsType<ToolResult.Created>(await ToolService(context).CreateAsync(
                new CreateToolCommand("CM", reference, "01", null, null, ["B1"]),
                CancellationToken.None));

            var compatibilityDelete = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM tools WHERE tool_id = @p",
                    new NpgsqlParameter("p", compatibleOnly.ToolId)));

            Assert.Equal("23503", compatibilityDelete.SqlState);
            Assert.Equal("FK_tool_machines_tools_tool_id", compatibilityDelete.ConstraintName);

            // Referenced by a production context only (the Tool row carries no compatibility row).
            var contextToolId = Guid.NewGuid();
            await InsertToolAsync(context, contextToolId, "CM", reference, "02");
            Assert.IsType<DMO.Application.JobOn.JobOnResult.Created>(await JobOnService(context).CreateAsync(
                new DMO.Application.JobOn.CreateJobOnCommand(
                    reference, $"pn-{token}-01", "B1", null, contextToolId, null, null),
                CancellationToken.None));

            var contextDelete = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM tools WHERE tool_id = @p",
                    new NpgsqlParameter("p", contextToolId)));

            Assert.Equal("23503", contextDelete.SqlState);
            Assert.Equal("FK_cm_contexts_tools_tool_id", contextDelete.ConstraintName);

            // Fail closed: both Tool rows are still there.
            Assert.Equal(
                2,
                await CountAsync(context, "tools WHERE reference = @p", new NpgsqlParameter("p", reference)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX12 (AC-30, AC-38): the reverse read by <c>tool_id</c> returns the occurrences using that
    /// Tool, while no <c>tools</c> row stores them (no reverse column, no array column).
    /// </summary>
    [SkippableFact]
    public async Task CTX12_TheReverseReadByToolIdReturnsOccurrencesAndNoToolRowStoresThem()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var toolId = Assert.IsType<ToolResult.Created>(await ToolService(context).CreateAsync(
                new CreateToolCommand("CM", reference, "01", null, null, ["B1"]),
                CancellationToken.None)).ToolId;

            var jobOns = JobOnService(context);

            // Two distinct occurrences using the SAME canonical Tool through their CM context.
            Assert.IsType<DMO.Application.JobOn.JobOnResult.Created>(await jobOns.CreateAsync(
                new DMO.Application.JobOn.CreateJobOnCommand(
                    reference, $"pn-{token}-01", "B1", null, toolId, null, null),
                CancellationToken.None));
            Assert.IsType<DMO.Application.JobOn.JobOnResult.Created>(await jobOns.CreateAsync(
                new DMO.Application.JobOn.CreateJobOnCommand(
                    reference, $"pn-{token}-02", "B2", null, toolId, null, null),
                CancellationToken.None));

            var usages = await new ToolRepository(context)
                .ListUsageOccurrencesAsync(toolId, CancellationToken.None);

            Assert.Equal(2, usages.Count);
            Assert.Equal(
                new[] { $"pn-{token}-01|B1", $"pn-{token}-02|B2" },
                usages.Select(usage => $"{usage.ProductionNumber}|{usage.Machine}").ToArray());
            Assert.All(usages, usage => Assert.Equal(reference, usage.Reference));

            // No reverse storage exists: the tools row carries exactly the eight contracted columns.
            var columns = Sorted(await QueryStringsAsync(
                context,
                "SELECT column_name FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = 'tools'"));

            Assert.Equal(
                Sorted(new[]
                {
                    "created_at", "lot", "processo", "quantity", "reference", "tool_id", "tool_type", "updated_at",
                }),
                columns);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CTX14 (AC-61, superseded wording): a create freezes the triple read from the Tool row at
    /// selection time, and duplication ALSO snapshots the CURRENT canonical Tool row (Owner
    /// clarification, contract §23) — a later Tool metadata change is never reflected in either a
    /// pre-existing context or a context created before that change.
    /// </summary>
    [SkippableFact]
    public async Task CTX14_TheCreateAndTheDuplicationFreezeTheTripleReadAtCreationTime()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var updatedReference = $"upd-{token}";
        var updatedLot = $"lot2-{token}";

        try
        {
            var toolId = Assert.IsType<ToolResult.Created>(await ToolService(context).CreateAsync(
                new CreateToolCommand("CM", reference, "01", "NNPB", 7, ["B1"]),
                CancellationToken.None)).ToolId;

            var jobOns = JobOnService(context);
            var source = Assert.IsType<DMO.Application.JobOn.JobOnResult.Created>(await jobOns.CreateAsync(
                new DMO.Application.JobOn.CreateJobOnCommand(
                    reference, $"pn-{token}-01", "B1", null, toolId, null, null),
                CancellationToken.None));

            Assert.Equal(
                $"{reference}|01",
                await FrozenTripleAsync(context, source.JobOnId, "cm_contexts"));

            // The live canonical Tool metadata changes directly in the database.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE tools SET reference = @r, lot = @l WHERE tool_id = @p",
                new NpgsqlParameter("r", updatedReference),
                new NpgsqlParameter("l", updatedLot),
                new NpgsqlParameter("p", toolId));

            // Duplication snapshots the CURRENT canonical Tool row (the post-change values): the
            // snapshot source is the Tool at duplication time, never the source context's frozen
            // triple. The old "copies the source frozen triple verbatim" reading is superseded.
            var duplicated = Assert.IsType<DMO.Application.JobOn.JobOnResult.Duplicated>(await jobOns.DuplicateAsync(
                new DMO.Application.JobOn.DuplicateJobOnCommand(
                    source.JobOnId, ExpectedSourceVersion: 1, $"pn-{token}-02", "B1", null),
                CancellationToken.None));

            Assert.Equal(
                $"{updatedReference}|{updatedLot}",
                await FrozenTripleAsync(context, duplicated.JobOnId, "cm_contexts"));

            // The source's own historical context is never rewritten by the Tool change.
            Assert.Equal(
                $"{reference}|01",
                await FrozenTripleAsync(context, source.JobOnId, "cm_contexts"));

            // A NEW create freezes the Tool row as it is now: the live values at selection time.
            var later = Assert.IsType<DMO.Application.JobOn.JobOnResult.Created>(await jobOns.CreateAsync(
                new DMO.Application.JobOn.CreateJobOnCommand(
                    reference, $"pn-{token}-03", "B1", null, toolId, null, null),
                CancellationToken.None));

            Assert.Equal(
                $"{updatedReference}|{updatedLot}",
                await FrozenTripleAsync(context, later.JobOnId, "cm_contexts"));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    private static async Task<IReadOnlyList<string>> SearchOrderAsync(DmoDbContext context, string reference)
    {
        var result = Assert.IsType<ToolResult.SearchResults>(await ToolService(context).SearchAsync(
            new ToolSearchQuery(null, null, reference, null, null, Limit: 10),
            CancellationToken.None));

        return result.Items
            .Select(item => $"{ToolTokens.ToToken(item.Type)}|{item.Lot}")
            .ToList();
    }

    private static async Task<IReadOnlyList<Guid>> SearchAsync(
        DmoDbContext context,
        string reference,
        string machine)
    {
        var result = Assert.IsType<ToolResult.SearchResults>(await ToolService(context).SearchAsync(
            new ToolSearchQuery(null, null, reference, null, MachineCode.From(machine), Limit: 10),
            CancellationToken.None));

        return result.Items.Select(item => item.ToolId).ToList();
    }

    private static async Task<ToolResult.Created> CreateToolAsync(
        ToolService service,
        string type,
        string reference,
        string lot) =>
        Assert.IsType<ToolResult.Created>(await service.CreateAsync(
            new CreateToolCommand(type, reference, lot, null, null, ["B1"]),
            CancellationToken.None));

    private static Task<string> FrozenTripleAsync(DmoDbContext context, Guid jobOnId, string table) =>
        ScalarAsync(
            context,
            $"SELECT tool_reference || '|' || tool_lot FROM {table} WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOnId));

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

    private static ToolService ToolService(DmoDbContext context) => new(new ToolRepository(context));

    private static DMO.Application.JobOn.JobOnService JobOnService(DmoDbContext context) => new(
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

    private static async Task<int> CountAsync(
        DmoDbContext context,
        string fromAndWhere,
        params NpgsqlParameter[] parameters) =>
        int.Parse(Assert.Single(await QueryStringsAsync(
            context,
            $"SELECT count(*) FROM {fromAndWhere}",
            parameters)));

    private static IReadOnlyList<string> Sorted(IReadOnlyList<string> values) =>
        values.OrderBy(value => value, StringComparer.Ordinal).ToList();

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

    /// <summary>Fails the Nth <c>SaveChangesAsync</c> of the marked context (test-only).</summary>
    private sealed class FailOnNthSaveInterceptor(int failOn) : SaveChangesInterceptor
    {
        private int _calls;

        /// <summary>The number of <c>SaveChangesAsync</c> calls observed so far.</summary>
        public int Calls => _calls;

        /// <summary>The tracked entries of the failed save (entity type + state), sorted.</summary>
        public IReadOnlyList<string> FailedSaveEntries { get; private set; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);

            if (call == failOn)
            {
                FailedSaveEntries = eventData.Context is { } tracked
                    ? tracked.ChangeTracker.Entries()
                        .Select(entry => $"{entry.Entity.GetType().Name}:{entry.State}")
                        .OrderBy(entry => entry, StringComparer.Ordinal)
                        .ToList()
                    : [];

                throw new ForcedSaveFailureException($"Forced failure on SaveChanges call {call}.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>The test-only failure thrown by <see cref="FailOnNthSaveInterceptor"/>.</summary>
    private sealed class ForcedSaveFailureException(string message) : Exception(message);
}
