using System.Data;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using DMO.IntegrationTests.Frontend.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DomainJobOn = DMO.Domain.JobOn.JobOn;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (table identifiers or
// row identifiers derived from a fresh Guid) against a disposable database. Analyzer EF1003
// suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T04 env-gated integration test — the Job On duplication over the disposable PostgreSQL
/// database: the explicit-source preview, the new-identity rules of §10 as corrected by the Owner
/// clarification (§23: every duplicated context re-snapshots the CURRENT canonical Tool row) and
/// the write-transaction guarantees of §10.5/§11.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §10 (duplication rules) as superseded by §23 (OWNER CLARIFICATION —
/// Job On context snapshot invariant), §10.5 (atomic write), §11 (transactions), §15 (concurrency)
/// and §20.4 (rows DUP2–DUP10, DUP12, DUP14, DUP17, DUP18). Every assertion is scoped to the
/// rows this test creates. Duplication must never modify the source, must never reuse a context
/// identity and must never touch <c>tools</c>/<c>tool_machines</c>.
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class JobOnDuplicationIntegrationTests
{
    private static readonly string[] ContextTables = ["cm_contexts", "mf_contexts", "bq_contexts"];

    /// <summary>
    /// DUP2 (AC-55): the read-only preview returns the source ficha plus <c>sourceVersion</c>, and
    /// the source <c>version</c>/<c>updated_at</c> are unchanged afterwards — previewing writes
    /// nothing.
    /// </summary>
    [SkippableFact]
    public async Task DUP2_PreviewReturnsTheSourceFichaAndSourceVersionAndLeavesTheSourceUnchanged()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var created = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool);

            var rowBefore = await RowFingerprintAsync(context, created.JobOnId);
            var contextsBefore = await ContextFingerprintAsync(context, created.JobOnId);
            var expectedFicha = await FichaAsync(context, created.JobOnId);

            var preview = Assert.IsType<JobOnResult.DuplicationPreview>(await JobOns(context)
                .PreviewDuplicateAsync(created.JobOnId, CancellationToken.None));

            Assert.Equal(created.JobOnId, preview.SourceJobOnId);
            Assert.Equal(expectedFicha.Version, preview.SourceVersion);
            // The ficha is a record over list-typed contexts, so value equality must be asserted
            // field by field (a list compares by reference, never by content).
            AssertFichaEqual(expectedFicha, preview.Source);

            // The preview is read-only: the source row, its contexts and the counts are untouched.
            Assert.Equal(rowBefore, await RowFingerprintAsync(context, created.JobOnId));
            Assert.Equal(contextsBefore, await ContextFingerprintAsync(context, created.JobOnId));
            Assert.Equal(1, await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP3 (AC-57): duplication produces a <c>jobon_id</c> different from the source and from every
    /// other Job On.
    /// </summary>
    [SkippableFact]
    public async Task DUP3_DuplicationProducesAJobOnIdDifferentFromTheSourceAndEveryOtherJobOn()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");
            var other = await CreateJobOnAsync(context, reference, $"pn-{token}-02", "B2");

            var duplicated = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-03", "C1", null));

            Assert.NotEqual(source.JobOnId, duplicated.JobOnId);
            Assert.NotEqual(other.JobOnId, duplicated.JobOnId);
            Assert.Equal(source.JobOnId, duplicated.SourceJobOnId);

            var ids = await QueryStringsAsync(
                context,
                "SELECT jobon_id::text FROM job_ons WHERE reference = @p ORDER BY jobon_id::text",
                new NpgsqlParameter("p", reference));

            Assert.Equal(3, ids.Count);
            Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(duplicated.JobOnId.ToString(), ids);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP4 (AC-58): every duplicated context has a new id, none equals a source context id, and the
    /// count matches the source's context count.
    /// </summary>
    [SkippableFact]
    public async Task DUP4_EveryDuplicatedContextHasANewIdAndTheCountMatchesTheSource()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);

            var sourceContextsBefore = await ContextFingerprintAsync(context, source.JobOnId);

            var duplicated = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B1", null));

            var sourceIds = await ContextIdsByTableAsync(context, source.JobOnId);
            var duplicateIds = await ContextIdsByTableAsync(context, duplicated.JobOnId);

            // Per table: the same number of contexts, all with NEW ids, none shared with the source.
            Assert.Equal(ContextTables.Length, duplicateIds.Count);
            Assert.Equal(ContextTables.Length, sourceIds.Count);

            foreach (var table in ContextTables)
            {
                Assert.Equal(sourceIds[table].Count, duplicateIds[table].Count);
                Assert.Empty(sourceIds[table].Intersect(duplicateIds[table], StringComparer.Ordinal));

                var lens = await CountContextAsync(context, table, source.JobOnId);
                Assert.Equal(lens, await CountContextAsync(context, table, duplicated.JobOnId));
            }

            Assert.Equal(3, await CountContextAsync(context, "cm_contexts", duplicated.JobOnId)
                + await CountContextAsync(context, "mf_contexts", duplicated.JobOnId)
                + await CountContextAsync(context, "bq_contexts", duplicated.JobOnId));

            // The source's own context rows carry their original ids (nothing was reused or renamed).
            Assert.Equal(sourceContextsBefore, await ContextFingerprintAsync(context, source.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP5 (AC-59): after duplication the source row and all source context rows are byte-identical
    /// — same <c>version</c>, same frozen triples, same timestamps.
    /// </summary>
    [SkippableFact]
    public async Task DUP5_AfterDuplicationTheSourceRowAndItsContextRowsAreByteIdentical()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool);

            var rowBefore = await RowFingerprintAsync(context, source.JobOnId);
            var contextsBefore = await ContextFingerprintAsync(context, source.JobOnId);

            await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B2", null));

            Assert.Equal(rowBefore, await RowFingerprintAsync(context, source.JobOnId));
            Assert.Equal(contextsBefore, await ContextFingerprintAsync(context, source.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP6 (AC-60): each duplicated context references the same canonical <c>tool_id</c> as its
    /// source context.
    /// </summary>
    [SkippableFact]
    public async Task DUP6_EachDuplicatedContextReferencesTheSameCanonicalToolIdAsItsSource()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);

            var duplicated = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B1", null));

            foreach (var table in ContextTables)
            {
                Assert.Equal(
                    Sorted(await ToolIdsAsync(context, table, source.JobOnId)),
                    Sorted(await ToolIdsAsync(context, table, duplicated.JobOnId)));
            }
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP7 (AC-61, superseded wording): after changing the live Tool's <c>reference</c>/<c>lot</c>,
    /// duplication snapshots the <b>current canonical Tool state</b> — the duplicate carries the NEW
    /// values while the source context keeps its historical frozen triple (Owner clarification §23;
    /// the old "copies the source frozen triple verbatim" reading is superseded).
    /// </summary>
    [SkippableFact]
    public async Task DUP7_AfterTheLiveToolChangesDuplicationSnapshotsTheCurrentToolState()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);

            var sourceTriples = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var table in ContextTables)
            {
                sourceTriples[table] = await ContextTriplesAsync(context, table, source.JobOnId);
            }

            // The live Tools move to a NEW reference/lot AFTER the source recorded its frozen triple.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE tools SET reference = @r, lot = @l WHERE reference LIKE @p",
                new NpgsqlParameter("r", $"{reference}-live"),
                new NpgsqlParameter("l", "99-live"),
                new NpgsqlParameter("p", $"%{token}%"));

            var duplicated = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B1", null));

            foreach (var table in ContextTables)
            {
                // The duplicate carries the CURRENT Tool state (the post-change values): the snapshot
                // source is the canonical Tool row at duplication time, never the source context.
                Assert.Equal(
                    new[] { $"{reference}-live|99-live" },
                    await ContextTriplesAsync(context, table, duplicated.JobOnId));

                // The source context's historical frozen triple is untouched.
                Assert.Equal(sourceTriples[table], await ContextTriplesAsync(context, table, source.JobOnId));

                // And the live Tool row itself now holds the NEW values.
                var live = Assert.Single(await QueryStringsAsync(
                    context,
                    "SELECT reference || '|' || lot FROM tools WHERE tool_id IN " +
                    "(SELECT tool_id FROM " + table + " WHERE jobon_id = @p)",
                    new NpgsqlParameter("p", source.JobOnId)));

                Assert.Equal($"{reference}-live|99-live", live);
            }

            // The new occurrence itself records the lineage and nothing about the source changed.
            Assert.Equal(source.JobOnId, duplicated.SourceJobOnId);
            Assert.Equal(sourceTriples[ContextTables[0]], await ContextTriplesAsync(context, ContextTables[0], source.JobOnId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP17 (Owner clarification §23, invariant 5): duplicating the SAME source twice produces two
    /// independent new occurrences (B and C) whose context identities are all distinct — from each
    /// other, from the source and from the other duplicate — while every context still references
    /// the same canonical <c>tool_id</c> as its source context.
    /// </summary>
    [SkippableFact]
    public async Task DUP17_DuplicatingTheSameSourceTwiceProducesDistinctContextIdsPerDuplicate()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var mfTool = await CreateToolAsync(context, "MF", reference, "02");
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool, mfTool, bqTool);

            var duplicateB = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B1", null));
            var duplicateC = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-03", "B1", null));

            Assert.NotEqual(source.JobOnId, duplicateB.JobOnId);
            Assert.NotEqual(source.JobOnId, duplicateC.JobOnId);
            Assert.NotEqual(duplicateB.JobOnId, duplicateC.JobOnId);

            // Every context identity is distinct across the three occurrences, per table.
            var sourceIds = await ContextIdsByTableAsync(context, source.JobOnId);
            var bIds = await ContextIdsByTableAsync(context, duplicateB.JobOnId);
            var cIds = await ContextIdsByTableAsync(context, duplicateC.JobOnId);

            foreach (var table in ContextTables)
            {
                var all = sourceIds[table].Concat(bIds[table]).Concat(cIds[table]).ToList();
                Assert.Equal(3, all.Count);
                Assert.Equal(3, all.Distinct(StringComparer.Ordinal).Count());
            }

            // Every duplicate context references the same canonical tool_id as the source context.
            foreach (var table in ContextTables)
            {
                var canonical = Sorted(await ToolIdsAsync(context, table, source.JobOnId));
                Assert.Equal(canonical, Sorted(await ToolIdsAsync(context, table, duplicateB.JobOnId)));
                Assert.Equal(canonical, Sorted(await ToolIdsAsync(context, table, duplicateC.JobOnId)));
            }

            // The source row and its contexts are untouched by both duplications.
            Assert.Equal(1, await CountAsync(context, "job_ons WHERE reference = @p AND copied_from_jobon_id IS NULL", new NpgsqlParameter("p", reference)));
            Assert.Equal(3, await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP18 (Owner clarification §23, invariant 6 — P2-T07 identity regression): the source
    /// <c>bq_id</c> and the duplicated <c>bq_id</c> are distinct context identities while both
    /// reference the same canonical <c>tool_id</c> — every production keeps its own BQ context, so
    /// P2-T07 movements keyed by <c>bq_id</c> stay with their own production and no migration to a
    /// new <c>bq_id</c> is ever needed.
    /// </summary>
    [SkippableFact]
    public async Task DUP18_BqIdentitiesStayDistinctWhileBothReferenceTheSameCanonicalTool()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var bqTool = await CreateToolAsync(context, "BQ", reference, "03");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", bqToolId: bqTool);

            var sourceBqIds = await ContextIdsByTableAsync(context, source.JobOnId);

            var duplicated = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B1", null));

            var duplicateBqIds = await ContextIdsByTableAsync(context, duplicated.JobOnId);

            // bq_id_A != bq_id_B: the context identity belongs to exactly one production.
            Assert.Single(sourceBqIds["bq_contexts"]);
            Assert.Single(duplicateBqIds["bq_contexts"]);
            Assert.NotEqual(sourceBqIds["bq_contexts"][0], duplicateBqIds["bq_contexts"][0]);

            // Both point at the SAME canonical BQ tool_id: the tool_id is the continuing identity.
            Assert.Equal(
                Sorted(await ToolIdsAsync(context, "bq_contexts", source.JobOnId)),
                Sorted(await ToolIdsAsync(context, "bq_contexts", duplicated.JobOnId)));

            // Each production's BQ context row is exclusively its own.
            Assert.Equal(2, await CountAsync(context, "bq_contexts WHERE tool_id = @p", new NpgsqlParameter("p", bqTool)));
            Assert.Equal(1, await CountAsync(context, "bq_contexts WHERE jobon_id = @p", new NpgsqlParameter("p", source.JobOnId)));
            Assert.Equal(1, await CountAsync(context, "bq_contexts WHERE jobon_id = @p", new NpgsqlParameter("p", duplicated.JobOnId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP8 (AC-62): the duplicated row's <c>copied_from_jobon_id</c> equals the source id; a created
    /// (non-duplicated) Job On has <c>NULL</c>.
    /// </summary>
    [SkippableFact]
    public async Task DUP8_TheDuplicatedRowRecordsTheSourceAndACreatedRowHasNull()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");

            var duplicated = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B2", null));

            Assert.Equal(
                $"{source.JobOnId}",
                Assert.Single(await QueryStringsAsync(
                    context,
                    "SELECT copied_from_jobon_id::text FROM job_ons WHERE jobon_id = @p",
                    new NpgsqlParameter("p", duplicated.JobOnId))));

            Assert.Equal(
                "-",
                Assert.Single(await QueryStringsAsync(
                    context,
                    "SELECT coalesce(copied_from_jobon_id::text, '-') FROM job_ons WHERE jobon_id = @p",
                    new NpgsqlParameter("p", source.JobOnId))));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP9 (AC-16, AC-63): duplicating onto an existing (<c>reference</c>, <c>production_number</c>)
    /// is refused naming the owner and creates no row.
    /// </summary>
    [SkippableFact]
    public async Task DUP9_DuplicatingOntoAnExistingPairIsRefusedAndCreatesNoRow()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";
        var occupiedNumber = $"pn-{token}-occupied";

        try
        {
            var owner = await CreateJobOnAsync(context, reference, occupiedNumber, "B1");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");

            var refusal = Assert.IsType<JobOnResult.Refused>(await JobOns(context).DuplicateAsync(
                new DuplicateJobOnCommand(source.JobOnId, source.Version, occupiedNumber, "B1", null),
                CancellationToken.None));

            Assert.Equal(JobOnRefusalReason.DuplicateProduction, refusal.Reason);
            Assert.Equal(owner.JobOnId, refusal.ExistingJobOnId);
            Assert.False(string.IsNullOrWhiteSpace(refusal.Message));

            Assert.Equal(2, await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP10 (AC-64): a stale <c>ExpectedSourceVersion</c> refuses the duplication — no new
    /// <c>job_ons</c> row, no new context rows — at the service seam and at the repository seam.
    /// </summary>
    [SkippableFact]
    public async Task DUP10_AStaleExpectedSourceVersionRefusesTheDuplicationAndCreatesNoRows()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool);

            // The source legitimately advances to version 2 (an edit after the operator previewed).
            var updated = Assert.IsType<JobOnResult.Updated>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    source.JobOnId,
                    ExpectedVersion: 1,
                    reference,
                    $"pn-{token}-01",
                    "B1",
                    null,
                    [],
                    DateThresholdWarningAcknowledged: true),
                CancellationToken.None));

            Assert.Equal(2, updated.Version);

            var jobOnsBefore = await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference));
            var cmBefore = await CountContextAsync(context, "cm_contexts", source.JobOnId);

            // Service seam: the stale preview version is refused before any write.
            var refusal = Assert.IsType<JobOnResult.Refused>(await JobOns(context).DuplicateAsync(
                new DuplicateJobOnCommand(source.JobOnId, ExpectedSourceVersion: 1, $"pn-{token}-02", "B1", null),
                CancellationToken.None));

            Assert.Equal(JobOnRefusalReason.StaleVersion, refusal.Reason);

            // Repository seam: the same stale version is a hard concurrency conflict.
            var duplicate = new DomainJobOn(
                DMO.Domain.JobOn.JobOnId.New(),
                reference,
                $"pn-{token}-02",
                MachineCode.From("B1"),
                null,
                source.JobOnId,
                Version: 1,
                Contexts: []);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                new JobOnRepository(context).DuplicatedAsync(
                    duplicate,
                    [],
                    expectedSourceVersion: 1,
                    CancellationToken.None));

            Assert.Equal(jobOnsBefore, await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));
            Assert.Equal(cmBefore, await CountContextAsync(context, "cm_contexts", source.JobOnId));
            Assert.Equal(0, await CountContextAsync(context, "cm_contexts", duplicate.JobOnId.Value));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP12 (AC-56): duplicating from the <b>oldest</b> available source of a reference behaves the
    /// same as any other explicit source — no newer production is selected implicitly.
    /// </summary>
    [SkippableFact]
    public async Task DUP12_DuplicatingFromTheOldestAvailableSourceOfAReferenceSucceeds()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var oldest = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1");
            var newest = await CreateJobOnAsync(context, reference, $"pn-{token}-02", "B1");

            // Premise: the productions query orders the oldest occurrence first — the duplication
            // preview can be opened for ANY listed occurrence, including this one.
            var productions = Assert.IsType<JobOnResult.ProductionsFound>(await JobOns(context)
                .FindProductionsAsync(new FindProductionsQuery(reference), CancellationToken.None));

            Assert.Equal(2, productions.Productions.Count);
            Assert.Equal(oldest.JobOnId, productions.Productions[0].JobOnId);
            Assert.Equal(newest.JobOnId, productions.Productions[1].JobOnId);

            var duplicated = await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(oldest.JobOnId, oldest.Version, $"pn-{token}-03", "C3", null));

            Assert.Equal(oldest.JobOnId, duplicated.SourceJobOnId);
            Assert.Equal(1, duplicated.Version);
            Assert.Equal(3, await CountAsync(context, "job_ons WHERE reference = @p", new NpgsqlParameter("p", reference)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// DUP14 (AC-53): duplication does not modify <c>tools</c> or <c>tool_machines</c> — the row
    /// hashes are unchanged.
    /// </summary>
    [SkippableFact]
    public async Task DUP14_DuplicationDoesNotModifyToolsOrToolMachines()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var reference = $"ref-{token}";

        try
        {
            var cmTool = await CreateToolAsync(context, "CM", reference, "01");
            var source = await CreateJobOnAsync(context, reference, $"pn-{token}-01", "B1", cmTool);

            var toolsBefore = await TableFingerprintAsync(context, "tools", reference);
            var machinesBefore = await MachineFingerprintAsync(context, reference);

            await DuplicateAsync(
                context,
                new DuplicateJobOnCommand(source.JobOnId, source.Version, $"pn-{token}-02", "B1", null));

            Assert.Equal(toolsBefore, await TableFingerprintAsync(context, "tools", reference));
            Assert.Equal(machinesBefore, await MachineFingerprintAsync(context, reference));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ================= service helpers ====================================================

    private static async Task<JobOnResult.Duplicated> DuplicateAsync(
        DmoDbContext context,
        DuplicateJobOnCommand command) =>
        Assert.IsType<JobOnResult.Duplicated>(await JobOns(context).DuplicateAsync(command, CancellationToken.None));

    private static async Task<Guid> CreateToolAsync(
        DmoDbContext context,
        string type,
        string reference,
        string lot) =>
        Assert.IsType<ToolResult.Created>(await Tools(context).CreateAsync(
            new CreateToolCommand(type, reference, lot, null, null, ["B1"]),
            CancellationToken.None)).ToolId;

    private static async Task<JobOnResult.Created> CreateJobOnAsync(
        DmoDbContext context,
        string reference,
        string productionNumber,
        string machine,
        Guid? cmToolId = null,
        Guid? mfToolId = null,
        Guid? bqToolId = null,
        DateOnly? productionDate = null) =>
        Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
            new CreateJobOnCommand(reference, productionNumber, machine, productionDate, cmToolId, mfToolId, bqToolId),
            CancellationToken.None));

    private static async Task<JobOnFicha> FichaAsync(DmoDbContext context, Guid jobOnId) =>
        Assert.IsType<JobOnResult.Ficha>(await JobOns(context).GetAsync(jobOnId, CancellationToken.None)).Value;

    private static ToolService Tools(DmoDbContext context) => new(new ToolRepository(context));

    private static JobOnService JobOns(DmoDbContext context) => new(
        new JobOnRepository(context),
        new ToolRepository(context),
        [new JobOnLineageDependencyProbe(context)]);

    // ================= database helpers ===================================================

    /// <summary>Every column of the occurrence row, so "unchanged" means every column.</summary>
    private static Task<string> RowFingerprintAsync(DmoDbContext context, Guid jobOnId) =>
        ScalarAsync(
            context,
            "SELECT jobon_id::text || '|' || reference || '|' || production_number || '|' || machine || '|' || " +
            "coalesce(production_date::text, '-') || '|' || coalesce(copied_from_jobon_id::text, '-') || '|' || " +
            "version || '|' || created_at::text || '|' || updated_at::text FROM job_ons WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOnId));

    /// <summary>Every context row of the Job On across the three tables.</summary>
    private static async Task<string> ContextFingerprintAsync(DmoDbContext context, Guid jobOnId)
    {
        var rows = await QueryStringsAsync(
            context,
            "SELECT 'cm|' || cm_id::text || '|' || tool_id::text || '|' || tool_reference || '|' || tool_lot || " +
            "'|' || created_at::text || '|' || updated_at::text FROM cm_contexts WHERE jobon_id = @p " +
            "UNION ALL SELECT 'mf|' || mf_id::text || '|' || tool_id::text || '|' || tool_reference || '|' || " +
            "tool_lot || '|' || created_at::text || '|' || updated_at::text FROM mf_contexts WHERE jobon_id = @p " +
            "UNION ALL SELECT 'bq|' || bq_id::text || '|' || tool_id::text || '|' || tool_reference || '|' || " +
            "tool_lot || '|' || created_at::text || '|' || updated_at::text FROM bq_contexts WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOnId));

        return string.Join(';', Sorted(rows));
    }

    /// <summary>The frozen triple (reference/lot) of one table's context rows for one Job On.</summary>
    private static Task<IReadOnlyList<string>> ContextTriplesAsync(DmoDbContext context, string table, Guid jobOnId) =>
        QueryStringsAsync(
            context,
            $"SELECT tool_reference || '|' || tool_lot FROM {table} WHERE jobon_id = @p ORDER BY 1",
            new NpgsqlParameter("p", jobOnId));

    /// <summary>The context ids per table of one Job On.</summary>
    private static async Task<Dictionary<string, IReadOnlyList<string>>> ContextIdsByTableAsync(
        DmoDbContext context,
        Guid jobOnId)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var table in ContextTables)
        {
            var keyColumn = table[..table.IndexOf('_')] + "_id";
            result[table] = await QueryStringsAsync(
                context,
                $"SELECT {keyColumn}::text FROM {table} WHERE jobon_id = @p ORDER BY 1",
                new NpgsqlParameter("p", jobOnId));
        }

        return result;
    }

    /// <summary>The canonical tool ids of one table's context rows for one Job On.</summary>
    private static Task<IReadOnlyList<string>> ToolIdsAsync(DmoDbContext context, string table, Guid jobOnId) =>
        QueryStringsAsync(
            context,
            $"SELECT tool_id::text FROM {table} WHERE jobon_id = @p ORDER BY tool_id::text",
            new NpgsqlParameter("p", jobOnId));

    /// <summary>Every scoped <c>tools</c> row, so "unchanged" means every column.</summary>
    private static Task<IReadOnlyList<string>> TableFingerprintAsync(
        DmoDbContext context,
        string table,
        string reference) =>
        QueryStringsAsync(
            context,
            "SELECT tool_id::text || '|' || tool_type || '|' || reference || '|' || lot || '|' || " +
            "coalesce(processo, '-') || '|' || coalesce(quantity::text, '-') || '|' || created_at::text || '|' || " +
            "updated_at::text FROM " + table + " WHERE reference = @p ORDER BY 1",
            new NpgsqlParameter("p", reference));

    /// <summary>Every scoped <c>tool_machines</c> row (machines resolved through the Tool).</summary>
    private static Task<IReadOnlyList<string>> MachineFingerprintAsync(DmoDbContext context, string reference) =>
        QueryStringsAsync(
            context,
            "SELECT tool_id::text || '|' || machine FROM tool_machines WHERE tool_id IN " +
            "(SELECT tool_id FROM tools WHERE reference = @p) ORDER BY 1",
            new NpgsqlParameter("p", reference));


    /// <summary>Field-by-field ficha equality, because record equality over list members is by reference.</summary>
    private static void AssertFichaEqual(JobOnFicha expected, JobOnFicha actual)
    {
        Assert.Equal(expected.JobOnId, actual.JobOnId);
        Assert.Equal(expected.Reference, actual.Reference);
        Assert.Equal(expected.ProductionNumber, actual.ProductionNumber);
        Assert.Equal(expected.Machine, actual.Machine);
        Assert.Equal(expected.ProductionDate, actual.ProductionDate);
        Assert.Equal(expected.CopiedFromJobOnId, actual.CopiedFromJobOnId);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Contexts.Count, actual.Contexts.Count);

        for (var index = 0; index < expected.Contexts.Count; index++)
        {
            var left = expected.Contexts[index];
            var right = actual.Contexts[index];

            Assert.Equal(left.ContextType, right.ContextType);
            Assert.Equal(left.ContextId, right.ContextId);
            Assert.Equal(left.ToolId, right.ToolId);
            Assert.Equal(left.ToolType, right.ToolType);
            Assert.Equal(left.ToolReference, right.ToolReference);
            Assert.Equal(left.ToolLot, right.ToolLot);
            Assert.Equal(left.Tool.ToolId, right.Tool.ToolId);
            Assert.Equal(left.Tool.Type, right.Tool.Type);
            Assert.Equal(left.Tool.Reference, right.Tool.Reference);
            Assert.Equal(left.Tool.Lot, right.Tool.Lot);
            Assert.Equal(left.Tool.Processo, right.Tool.Processo);
            Assert.Equal(left.Tool.Quantity, right.Tool.Quantity);
            Assert.Equal(left.Tool.CompatibleMachines.Select(m => m.Value).ToArray(), right.Tool.CompatibleMachines.Select(m => m.Value).ToArray());
        }
    }
    private static Task<int> CountContextAsync(DmoDbContext context, string table, Guid jobOnId) =>
        CountAsync(context, $"{table} WHERE jobon_id = @p", new NpgsqlParameter("p", jobOnId));

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

    /// <summary>Deletes the rows this test created, in FK-safe order (disposable database only).</summary>
    private static async Task CleanupAsync(DmoDbContext context, string token)
    {
        var pattern = $"%{token}%";

        foreach (var table in ContextTables)
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
}