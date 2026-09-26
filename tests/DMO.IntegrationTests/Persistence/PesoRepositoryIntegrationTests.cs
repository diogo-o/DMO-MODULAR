using System.Data;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.Entities;
using DMO.Infrastructure.Persistence.EntityConfigurations;
using DMO.IntegrationTests.ControloCreate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T05 env-gated integration test — the Peso occurrence over the disposable PostgreSQL
/// database: create/associate/submit/edit transactions, the frozen-density rule, the snapshot
/// separation and the optimistic-concurrency guards of §19 over the real repositories.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §5.3/§5.4 (formulas), §6.3.3 (frozen density), §16 (schema), §18
/// (transactions), §19 (concurrency) and §30 rows PID1–PID3, PID6–PID8, PID10, MES2–MES4, MES6,
/// MES10, MES11, SNA1, SNA2, SNA4, CRE1–CRE3, CRE5–CRE9 (AC-P1/AC-P2/AC-P3/AC-P5/AC-P7/AC-M2/
/// AC-M3/AC-M4/AC-M6/AC-H1/AC-H2/AC-H3/AC-C1/AC-C3/AC-C4/AC-C5/AC-C7/AC-C8/AC-C9/MES10 backstop).
/// Every assertion is scoped to the rows this test creates (fresh Guid and per-test token).
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class PesoRepositoryIntegrationTests
{
    /// <summary>
    /// PID1 (AC-P1): a create anchored to a real <c>cm_id</c> writes the production-bound Peso and
    /// its sheet explains the anchor through the frozen triple and the Job On production facts.
    /// </summary>
    [SkippableFact]
    public async Task PID1_CreateWithCmAnchorProducesAProductionBoundSheet()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);
            var reference = $"ref-{token}";
            var productionNumber = $"pn-{token}-01";

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));
            Assert.Equal(1, created.Version);

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            Assert.Equal(cmId, sheet.CmId);
            Assert.Null(sheet.ToolId);
            Assert.Equal("pendente", sheet.Status);
            Assert.NotNull(sheet.Context);
            Assert.Equal(reference, sheet.Context!.FrozenToolReference);
            Assert.Equal("CM", sheet.Context.FrozenToolType);
            Assert.NotNull(sheet.Production);
            Assert.Equal(reference, sheet.Production!.Reference);
            Assert.Equal(productionNumber, sheet.Production.ProductionNumber);
            Assert.Equal("B1", sheet.Production.Machine);
            Assert.Null(sheet.Pending);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PID2 (AC-P2/AC-P3): a create anchored to a pending <c>tool_id</c> writes the truthful pending
    /// Peso; the stateless calculation works against the same pending anchor.
    /// </summary>
    [SkippableFact]
    public async Task PID2_CreateWithPendingToolAnchorProducesAPendingSheetAndCalculates()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context); // the glass assertions need the canonical NNPB 2.4027

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            Assert.Equal(toolId, sheet.ToolId);
            Assert.Null(sheet.CmId);
            Assert.Null(sheet.Production);
            Assert.NotNull(sheet.Pending);
            Assert.Equal(toolId, sheet.Pending!.ToolId);
            Assert.Equal($"ref-{token}", sheet.Pending.Tool.Reference);

            // The stateless calculation resolves the same pending anchor.
            var calculation = Assert.IsType<PesoResult.Calculation>(await Pesos(context).CalculateAsync(
                new CalculatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m]),
                CancellationToken.None));

            Assert.Equal(toolId, calculation.Value.PendingToolId);
            var row = Assert.Single(calculation.Value.Rows);
            Assert.Equal(1000m, row.CapacityCm3);
            Assert.Equal(2402.7000m, row.GlassWeightG); // (1000 + 0 − 0) × 2.4027 (seeded NNPB)
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PID3 (AC-P1): the exclusive-anchor CHECK (<c>pesos_anchor_check</c>) rejects a Peso with both
    /// anchors set or with no anchor (SQLSTATE 23514), while a valid single-anchor insert succeeds.
    /// </summary>
    [SkippableFact]
    public async Task PID3_TheAnchorCheckRejectsMissingAndDoubleAnchorsAndAcceptsASingleOne()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            // A dedicated Tool identity for the raw-insert checks: the production seeder below
            // creates its OWN CM Tool with the token reference, so this one uses a distinct
            // reference to avoid a duplicate canonical identity (type + reference + lot unique).
            var toolId = await CreateCmToolAsync(context, $"ref-{token}-raw", "01");
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            // Both anchors set: the CHECK fails even though both ids are valid.
            var both = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO pesos (peso_id, cm_id, tool_id, status, water_temperature, version, " +
                    "created_by_user_id, created_at, updated_at) VALUES (@id, @cm, @tool, 'pendente', 25, 1, " +
                    "@user, now(), now())",
                    new NpgsqlParameter("id", Guid.NewGuid()),
                    new NpgsqlParameter("cm", cmId),
                    new NpgsqlParameter("tool", toolId),
                    new NpgsqlParameter("user", userId)));
            Assert.Equal("23514", both.SqlState);
            Assert.Equal(PesoEntityConfiguration.AnchorCheckConstraintName, both.ConstraintName);

            // No anchor at all: the CHECK fails as well.
            var none = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO pesos (peso_id, status, water_temperature, version, created_by_user_id, " +
                    "created_at, updated_at) VALUES (@id, 'pendente', 25, 1, @user, now(), now())",
                    new NpgsqlParameter("id", Guid.NewGuid()),
                    new NpgsqlParameter("user", userId)));
            Assert.Equal("23514", none.SqlState);
            Assert.Equal(PesoEntityConfiguration.AnchorCheckConstraintName, none.ConstraintName);

            // A valid single-anchor insert (tool_id) with every required column succeeds.
            var pesoId = Guid.NewGuid();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO pesos (peso_id, tool_id, status, water_temperature, version, created_by_user_id, " +
                "created_at, updated_at) VALUES (@id, @tool, 'pendente', 25, 1, @user, now(), now())",
                new NpgsqlParameter("id", pesoId),
                new NpgsqlParameter("tool", toolId),
                new NpgsqlParameter("user", userId));

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
    /// PID6 (AC-P5/AC-C8): associating a pending Peso with a candidate <c>cm_id</c> whose Job On
    /// uses the SAME anchor Tool swaps the anchor in place and bumps version exactly once.
    /// </summary>
    [SkippableFact]
    public async Task PID6_AssociationSwapsAPendingPesoToTheCandidateCm()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var toolA = await CreateCmToolAsync(context, $"ref-{token}", "01");
            await CreateJobOnAsync(context, $"ref-{token}", $"pn-{token}-01", toolA); // candidate 1
            var candidate2 = Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
                new CreateJobOnCommand($"ref2-{token}", $"pn-{token}-02", "B1", null, toolA, null, null),
                CancellationToken.None));
            var cmId2 = Guid.Parse(await ScalarAsync(
                context,
                "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", candidate2.JobOnId)));

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(null, toolA, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            var associated = Assert.IsType<PesoResult.Associated>(await Pesos(context).AssociateAsync(
                new AssociatePesoCommand(created.PesoId, cmId2, ExpectedVersion: 1),
                CancellationToken.None));

            Assert.Equal(created.PesoId, associated.PesoId);
            Assert.Equal(2, associated.Version); // exactly one increment
            Assert.Equal(cmId2, associated.CmId);

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            Assert.Equal(cmId2, sheet.CmId);
            Assert.Null(sheet.ToolId);
            Assert.Equal(2, sheet.Version);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PID7 (AC-P5): a candidate <c>cm_id</c> resolving to a DIFFERENT Tool than the Peso's pending
    /// anchor is refused with <c>AssociationMismatch</c> and nothing changes.
    /// </summary>
    [SkippableFact]
    public async Task PID7_AssociationWithAMismatchedToolIsRefusedAndWritesNothing()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var toolA = await CreateCmToolAsync(context, $"ref-{token}", "01");
            var toolB = await CreateCmToolAsync(context, $"refb-{token}", "01");
            var jobOnB = Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
                new CreateJobOnCommand($"refb-{token}", $"pn-{token}-b", "B1", null, toolB, null, null),
                CancellationToken.None));
            var cmIdB = Guid.Parse(await ScalarAsync(
                context,
                "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
                new NpgsqlParameter("p", jobOnB.JobOnId)));

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(null, toolA, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            var refused = Assert.IsType<PesoResult.Refused>(await Pesos(context).AssociateAsync(
                new AssociatePesoCommand(created.PesoId, cmIdB, ExpectedVersion: 1),
                CancellationToken.None));

            Assert.Equal(PesoRefusalReason.AssociationMismatch, refused.Reason);

            // Unchanged: still pending on tool A at version 1.
            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            Assert.Equal(toolA, sheet.ToolId);
            Assert.Null(sheet.CmId);
            Assert.Equal(1, sheet.Version);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PID8 (AC-P5): association is refused for an already production-bound Peso
    /// (<c>AlreadyAssociated</c>) and for a submitted Peso (<c>AlreadySubmitted</c>).
    /// </summary>
    [SkippableFact]
    public async Task PID8_AssociationIsRefusedForProductionBoundAndSubmittedPesos()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            // Distinct reference: the production seeder creates its OWN CM Tool with the token
            // reference (canonical identity unique per type + reference + lot).
            var toolA = await CreateCmToolAsync(context, $"ref-{token}-a", "01");
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            // (a) Production-bound Peso: association is offered only while pending.
            var productionBound = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            var associated = Assert.IsType<PesoResult.Refused>(await Pesos(context).AssociateAsync(
                new AssociatePesoCommand(productionBound.PesoId, cmId, ExpectedVersion: 1),
                CancellationToken.None));
            Assert.Equal(PesoRefusalReason.AlreadyAssociated, associated.Reason);

            // (b) Submitted Peso: Create-side mutations are closed after submission.
            var pending = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(null, toolA, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
                new SubmitPesoCommand(pending.PesoId, ExpectedVersion: 1, userId),
                CancellationToken.None));

            var submitted = Assert.IsType<PesoResult.Refused>(await Pesos(context).AssociateAsync(
                new AssociatePesoCommand(pending.PesoId, cmId, ExpectedVersion: 2),
                CancellationToken.None));
            Assert.Equal(PesoRefusalReason.AlreadySubmitted, submitted.Reason);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// PID10 (AC-P7): two creates with the SAME anchor/temperature/weights produce two DISTINCT
    /// Peso records; every created record is independently readable.
    /// </summary>
    [SkippableFact]
    public async Task PID10_TwoCreatesWithTheSameFactsAreTwoDistinctPesos()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var first = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));
            var second = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            Assert.NotEqual(first.PesoId, second.PesoId);

            Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(first.PesoId, CancellationToken.None));
            Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(second.PesoId, CancellationToken.None));

            Assert.Equal(
                2,
                await CountAsync(
                    context,
                    "pesos WHERE created_by_user_id = @p",
                    new NpgsqlParameter("p", userId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// MES2 (AC-M2): the entered weight round-trips with full <c>numeric(18,4)</c> precision, and a
    /// non-positive weight is refused by the validator before any write.
    /// </summary>
    [SkippableFact]
    public async Task MES2_RowWeightPrecisionRoundTripsAndZeroWeightsAreRefusedBeforeAnyWrite()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [123.4567m], userId),
                CancellationToken.None));

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            Assert.Equal(123.4567m, Assert.Single(sheet.Rows).WaterWeightG);

            // A non-positive weight is a contracted validation failure: nothing was written.
            var refused = Assert.IsType<PesoResult.ValidationFailed>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [0m], userId),
                CancellationToken.None));
            Assert.Contains(ControloCreateValidationErrors.RowWeightInvalid, refused.Errors);

            Assert.Equal(
                1,
                await CountAsync(
                    context,
                    "pesos WHERE created_by_user_id = @p",
                    new NpgsqlParameter("p", userId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// MES3/MES4-DB (AC-M3/AC-M4): the exact persisted per-row results of the authoritative §5.3
    /// formulas, and the stateless calculation never writes (no row, no version bump).
    /// </summary>
    [SkippableFact]
    public async Task MES3_4_TheExactPersistedResultsRoundTripAndCalculateNeverWrites()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context); // the glass assertions need the canonical NNPB 2.4027

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(
                    cmId, null, WaterTemperature: 25m, VolumeMarisaBq: 10m, VolumePuncaoPu: 5m,
                    null, null, [997.1m, 1994.2m], userId),
                CancellationToken.None));

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            // 997.1 ÷ 0.9971 = 1000.0000 — (1000 + 10 − 5) × 2.4027 = 2414.7135.
            // 1994.2 ÷ 0.9971 = 2000.0000 — (2000 + 10 − 5) × 2.4027 = 4817.4135.
            Assert.Equal(1000.0000m, sheet.Rows[0].CapacityCm3);
            Assert.Equal(2414.7135m, sheet.Rows[0].GlassWeightG);
            Assert.Equal(2000.0000m, sheet.Rows[1].CapacityCm3);
            Assert.Equal(4817.4135m, sheet.Rows[1].GlassWeightG);

            // The stateless calculation with the same anchor writes nothing and bumps nothing.
            var before = await CountAsync(
                context,
                "pesos WHERE created_by_user_id = @p",
                new NpgsqlParameter("p", userId));

            Assert.IsType<PesoResult.Calculation>(await Pesos(context).CalculateAsync(
                new CalculatePesoCommand(
                    cmId, null, WaterTemperature: 25m, VolumeMarisaBq: 10m, VolumePuncaoPu: 5m,
                    null, null, [997.1m, 1994.2m]),
                CancellationToken.None));

            Assert.Equal(before, await CountAsync(
                context,
                "pesos WHERE created_by_user_id = @p",
                new NpgsqlParameter("p", userId)));
            Assert.Equal("1", await ScalarAsync(
                context,
                "SELECT version FROM pesos WHERE peso_id = @p",
                new NpgsqlParameter("p", created.PesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// MES6 (AC-M6): a three-row Peso round-trips with dense positions 1..3 and stable row ids
    /// across repeated reads.
    /// </summary>
    [SkippableFact]
    public async Task MES6_ThreeRowsRoundTripWithDensePositionsAndStableIds()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m, 998.2m, 999.3m], userId),
                CancellationToken.None));

            var first = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            var second = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            Assert.Equal(3, first.Rows.Count);
            Assert.Equal(new[] { 1, 2, 3 }, first.Rows.Select(row => row.RowPosition).ToArray());
            Assert.Equal(first.Rows.Select(row => row.PesoMeasurementRowId), second.Rows.Select(row => row.PesoMeasurementRowId));
            Assert.Equal(first.Rows.Select(row => row.RowPosition), second.Rows.Select(row => row.RowPosition));
            Assert.Equal(first.Rows.Select(row => row.WaterWeightG), second.Rows.Select(row => row.WaterWeightG));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// MES10 (AC-H3/AC-M4; post-closure correction R4/R5): the glass density is written once and
    /// frozen — a later Definições SETTINGS change never refreshes an existing Peso (§6.3.3) and
    /// affects new Peso records only. The glass value now resolves from
    /// <c>glass_density_settings</c> (seeded NNPB 2.4027); the "later" value is applied through
    /// the REAL settings repository exactly like the Definições surface would.
    /// </summary>
    [SkippableFact]
    public async Task MES10_FrozenDensityIsNeverRefreshedByLaterConfiguration()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            // Under the seeded settings the NNPB density is 2.4027.
            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(
                    cmId, null, WaterTemperature: 25m, VolumeMarisaBq: 10m, VolumePuncaoPu: 5m,
                    null, null, [997.1m], userId),
                CancellationToken.None));

            // The Definições surface later changes NNPB → 3.00 (version-guarded settings write).
            var settings = new GlassDensitySettingsRepository(context);
            var current = await settings.GetByProcessoAsync("NNPB", CancellationToken.None);
            Assert.Equal(2.4027m, current!.DensityGCm3);
            Assert.Equal(1, current.Version);
            var changed = await settings.UpdatedAsync(
                current with { DensityGCm3 = 3.00m },
                CancellationToken.None);
            Assert.Equal(2, changed.Version);

            // The edit with SAME facts is recomputed with the FROZEN density 2.4027, so the
            // stored values never change.
            var updated = Assert.IsType<PesoResult.Updated>(await Pesos(context).UpdateAsync(
                new UpdatePesoCommand(
                    created.PesoId, ExpectedVersion: 1, WaterTemperature: 25m,
                    VolumeMarisaBq: 10m, VolumePuncaoPu: 5m, null, null, [997.1m]),
                CancellationToken.None));
            Assert.Equal(2, updated.Version);

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            Assert.Equal(2.4027m, sheet.GlassDensityGCm3); // frozen — never refreshed
            Assert.Equal(2414.7135m, Assert.Single(sheet.Rows).GlassWeightG); // recomputed with 2.4027

            // A NEW Peso created under the changed settings freezes the NEW value.
            var laterCreated = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(
                    cmId, null, WaterTemperature: 25m, VolumeMarisaBq: 10m, VolumePuncaoPu: 5m,
                    null, null, [997.1m], userId),
                CancellationToken.None));

            var laterSheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                laterCreated.PesoId, CancellationToken.None)).Sheet;
            Assert.Equal(3.00m, laterSheet.GlassDensityGCm3);
            Assert.Equal(3015.0000m, Assert.Single(laterSheet.Rows).GlassWeightG); // 1005 × 3.00
        }
        finally
        {
            // The shared disposable DB must never leak the mutated setting into later tests.
            await GlassDensityTestState.RestoreAsync(context);
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// SNA1 (AC-H1): a live canonical Tool change is never reflected in the context's frozen triple
    /// — the sheet keeps the frozen reference and shows the live projection separately.
    /// </summary>
    [SkippableFact]
    public async Task SNA1_AFrozenTripleIsNeverRewrittenByALiveToolChange()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (toolId, _, cmId) = await SeedCmProductionAsync(context, token);
            var reference = $"ref-{token}";

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            // The live canonical Tool metadata changes directly in the database.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE tools SET reference = @r, lot = @l WHERE tool_id = @p",
                new NpgsqlParameter("r", $"NEW-REF-{token}"),
                new NpgsqlParameter("l", $"NEW-LOT-{token}"),
                new NpgsqlParameter("p", toolId));

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            // The frozen triple is untouched…
            Assert.Equal(reference, sheet.Context!.FrozenToolReference);
            Assert.Equal("01", sheet.Context.FrozenToolLot);

            // …while the live projection shows the changed canonical values.
            Assert.Equal($"NEW-REF-{token}", sheet.Context.Tool.Reference);
            Assert.Equal($"NEW-LOT-{token}", sheet.Context.Tool.Lot);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// SNA2 (AC-H1/AC-H2): a Job On fact change (reference/production number) never rewrites the
    /// stored Peso facts — the sheet inputs/results are unchanged and only the traversal labels move.
    /// </summary>
    [SkippableFact]
    public async Task SNA2_AJobOnRenameNeverChangesTheStoredSheetFacts()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);
        await GlassDensityTestState.RestoreAsync(context); // the glass assertions need the canonical NNPB 2.4027

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, jobOnId, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(
                    cmId, null, WaterTemperature: 25m, VolumeMarisaBq: 10m, VolumePuncaoPu: 5m,
                    null, null, [997.1m], userId),
                CancellationToken.None));

            var repository = new JobOnRepository(context);
            var persisted = await repository.GetByIdAsync(jobOnId, CancellationToken.None);

            // A legitimate Job On edit: rename reference/production number, Keep the CM slot.
            var updated = await repository.UpdatedAsync(
                persisted! with
                {
                    Reference = $"ref-{token}-renamed",
                    ProductionNumber = $"pn-{token}-renamed",
                },
                [new ToolContextChange(ToolContextType.Cm, ToolAssociationAction.Keep, null)],
                CancellationToken.None);
            Assert.Equal(2, updated.Version);

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            // The stored inputs/results are byte-identical…
            Assert.Equal(1, sheet.Version);
            Assert.Equal(25m, sheet.WaterTemperature);
            Assert.Equal(1000m, Assert.Single(sheet.Rows).CapacityCm3);
            Assert.Equal(2414.7135m, sheet.Rows[0].GlassWeightG);

            // …and only the traversal labels show the new Job On facts.
            Assert.Equal($"ref-{token}-renamed", sheet.Production!.Reference);
            Assert.Equal($"pn-{token}-renamed", sheet.Production.ProductionNumber);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// SNA4 (AC-H1): a CM re-selection keeps the SAME <c>cm_id</c> (in-place P2-T04 update) and the
    /// Peso sheet shows the new frozen triple — the Peso anchor itself is unchanged.
    /// </summary>
    [SkippableFact]
    public async Task SNA4_ACmReselectionKeepsThePesoAnchorAndRefreshesTheFrozenTriple()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, jobOnId, cmId) = await SeedCmProductionAsync(context, token);
            var secondTool = await CreateCmToolAsync(context, $"ref2-{token}", "02");

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            // Re-select a DIFFERENT CM Tool for the same slot (P2-T04 in-place update).
            Assert.IsType<JobOnResult.Updated>(await JobOns(context).UpdateAsync(
                new UpdateJobOnCommand(
                    jobOnId,
                    ExpectedVersion: 1,
                    $"ref-{token}",
                    $"pn-{token}-01",
                    "B1",
                    null,
                    Associations: [new ToolAssociationChange(ToolContextType.Cm, ToolAssociationAction.Set, secondTool)],
                    DateThresholdWarningAcknowledged: false),
                CancellationToken.None));

            // The Job On ficha still carries the SAME cm_id with the new frozen triple.
            var ficha = Assert.IsType<JobOnResult.Ficha>(await JobOns(context).GetAsync(jobOnId, CancellationToken.None)).Value;
            var cmContext = Assert.Single(ficha.Contexts, contextEntry => contextEntry.ContextType == ToolContextType.Cm);
            Assert.Equal(cmId, cmContext.ContextId);
            Assert.Equal($"ref2-{token}", cmContext.ToolReference);

            // The Peso sheet keeps the same cm anchor; the frozen triple shows the new Tool.
            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            Assert.Equal(cmId, sheet.Context!.CmId);
            Assert.Equal($"ref2-{token}", sheet.Context.FrozenToolReference);
            Assert.Equal("02", sheet.Context.FrozenToolLot);
            Assert.Equal(secondTool, sheet.Context.ToolId);
            Assert.Equal(secondTool, sheet.Context.Tool.ToolId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CRE1/CRE2 (AC-C1): a create whose row set violates the unique (peso_id, row_position) key
    /// fails with the unmapped <c>23505</c> propagating as <c>DbUpdateException</c>, and the whole
    /// unit rolls back — no <c>pesos</c> row and no <c>peso_measurement_rows</c> row survive.
    /// </summary>
    [SkippableFact]
    public async Task CRE1_2_ACreateWithDuplicateRowPositionsRollsBackEntirely()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");
            var now = DateTimeOffset.UtcNow;
            var pesoId = PesoId.New();

            var peso = new Peso(
                pesoId,
                CmId: null,
                toolId,
                PesoStatus.Pendente,
                SubmittedAt: null,
                SubmittedByUserId: null,
                WaterTemperature: 25m,
                VolumeMarisaBq: null,
                VolumePuncaoPu: null,
                GlassDensityGCm3: 2.50m,
                PreviousProductionEndReference: null,
                PreviousAverageWeightReference: null,
                Version: 1,
                userId,
                now,
                now,
                Rows: []);

            // The domain rows carry positions; two rows with the same position violate the key.
            var rows = new[]
            {
                new PesoMeasurementRow(PesoMeasurementRowId.New(), pesoId, RowPosition: 1, 997.1m, 1000m, 2500m, now),
                new PesoMeasurementRow(PesoMeasurementRowId.New(), pesoId, RowPosition: 1, 998.2m, 1001.1m, 2502.75m, now),
            };

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                new PesoRepository(context).CreatedAsync(peso, rows, CancellationToken.None));

            var postgres = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal("23505", postgres.SqlState);
            Assert.Equal(PesoMeasurementRowEntityConfiguration.PesoPositionUniqueConstraintName, postgres.ConstraintName);

            // Rollback proof: neither the Peso nor any measurement row survived the failure.
            Assert.Equal(
                0,
                await CountAsync(context, "pesos WHERE peso_id = @p", new NpgsqlParameter("p", pesoId.Value)));
            Assert.Equal(
                0,
                await CountAsync(
                    context,
                    "peso_measurement_rows WHERE peso_id = @p",
                    new NpgsqlParameter("p", pesoId.Value)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CRE3 (AC-C7/AC-C8): <c>version</c> starts at 1, increments exactly once per committed
    /// mutation and is never bumped by a read.
    /// </summary>
    [SkippableFact]
    public async Task CRE3_VersionIncrementsExactlyPerMutationAndReadsNeverBump()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));
            Assert.Equal(1, created.Version);

            var firstEdit = Assert.IsType<PesoResult.Updated>(await Pesos(context).UpdateAsync(
                new UpdatePesoCommand(created.PesoId, ExpectedVersion: 1, 25m, null, null, null, null, [997.1m, 998.2m]),
                CancellationToken.None));
            Assert.Equal(2, firstEdit.Version);

            // A read is a read: it never bumps.
            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            Assert.Equal(2, sheet.Version);

            var secondEdit = Assert.IsType<PesoResult.Updated>(await Pesos(context).UpdateAsync(
                new UpdatePesoCommand(created.PesoId, ExpectedVersion: 2, 25m, null, null, null, null, [996.5m]),
                CancellationToken.None));
            Assert.Equal(3, secondEdit.Version);

            Assert.Equal(
                "3",
                await ScalarAsync(
                    context,
                    "SELECT version FROM pesos WHERE peso_id = @p",
                    new NpgsqlParameter("p", created.PesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CRE5 (AC-C3): an update replaces the WHOLE measurement-row set atomically — the old row ids
    /// are gone and the three new rows occupy dense positions 1..3.
    /// </summary>
    [SkippableFact]
    public async Task CRE5_AnUpdateReplacesTheWholeRowSetAtomically()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m, 998.2m], userId),
                CancellationToken.None));

            var before = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            var oldIds = before.Rows.Select(row => row.PesoMeasurementRowId).ToHashSet();
            Assert.Equal(2, oldIds.Count);

            Assert.IsType<PesoResult.Updated>(await Pesos(context).UpdateAsync(
                new UpdatePesoCommand(created.PesoId, ExpectedVersion: 1, 25m, null, null, null, null, [997.1m, 998.2m, 999.3m]),
                CancellationToken.None));

            var after = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;

            Assert.Equal(3, after.Rows.Count);
            Assert.Equal(new[] { 1, 2, 3 }, after.Rows.Select(row => row.RowPosition).ToArray());
            Assert.All(after.Rows, row => Assert.DoesNotContain(row.PesoMeasurementRowId, oldIds));

            Assert.Equal(
                3,
                await CountAsync(
                    context,
                    "peso_measurement_rows WHERE peso_id = @p",
                    new NpgsqlParameter("p", created.PesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CRE6 (AC-C7/AC-C9): the save-time optimistic-concurrency race — a version bump committed by a
    /// second connection between the repository's explicit compare and its guarded save surfaces as
    /// the typed <c>ConcurrencyConflictException</c> and nothing of the losing write persists.
    /// </summary>
    [SkippableFact]
    public async Task CRE6_TheSaveTimeConcurrencyRaceSurfacesTheTypedConflict()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(
                    cmId, null, WaterTemperature: 25m, VolumeMarisaBq: 10m, VolumePuncaoPu: 5m,
                    null, null, [997.1m], userId),
                CancellationToken.None));
            Assert.Equal(1, created.Version);

            // Load the version-1 aggregate the losing edit will build on.
            var persisted = await new PesoRepository(context).GetByIdAsync(created.PesoId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted!.Version);

            // The race seam: a second real connection commits version 2 inside the guarded save.
            var racer = new PesoVersionRaceInterceptor(created.PesoId);
            await using var marked = CreateMarkedContext(racer);

            var losing = persisted with
            {
                WaterTemperature = 26m, // a real fact change, so the guarded UPDATE is actually issued
                Rows =
                [
                    new PesoMeasurementRow(
                        PesoMeasurementRowId.New(), persisted.PesoId, RowPosition: 1, 998.1m, 1100m, 2700m,
                        persisted.CreatedAt),
                ],
            };

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                new PesoRepository(marked).UpdatedAsync(losing, losing.Rows, CancellationToken.None));

            Assert.Equal(1, racer.Bumps);

            // The persisted row keeps its original facts and the racer's committed version 2;
            // nothing of the losing write survived (its temperature/rows are absent).
            Assert.Equal(
                "true|2",
                await ScalarAsync(
                    context,
                    "SELECT (water_temperature = 25)::text || '|' || version FROM pesos WHERE peso_id = @p",
                    new NpgsqlParameter("p", created.PesoId)));
            Assert.Equal(
                1,
                await CountAsync(
                    context,
                    "peso_measurement_rows WHERE peso_id = @p AND water_weight_g = 997.1",
                    new NpgsqlParameter("p", created.PesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CRE7 (AC-C4/AC-C5): submit transitions the SAME row into the reviewable handoff with backend
    /// attribution and closes every Create-side mutation on it.
    /// </summary>
    [SkippableFact]
    public async Task CRE7_SubmitClosesCreateSideMutations()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (toolId, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            var submitted = Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
                new SubmitPesoCommand(created.PesoId, ExpectedVersion: 1, userId),
                CancellationToken.None));

            Assert.Equal(created.PesoId, submitted.PesoId);
            Assert.Equal(2, submitted.Version);
            Assert.NotEqual(default, submitted.SubmittedAt);

            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            Assert.NotNull(sheet.SubmittedAt);
            Assert.Equal(userId, sheet.SubmittedByUserId);
            Assert.Equal("pendente", sheet.Status);

            // The edit path is closed after submission.
            var edit = Assert.IsType<PesoResult.Refused>(await Pesos(context).UpdateAsync(
                new UpdatePesoCommand(created.PesoId, ExpectedVersion: 2, 25m, null, null, null, null, [998.1m]),
                CancellationToken.None));
            Assert.Equal(PesoRefusalReason.AlreadySubmitted, edit.Reason);

            // The association path is closed after submission as well.
            var associate = Assert.IsType<PesoResult.Refused>(await Pesos(context).AssociateAsync(
                new AssociatePesoCommand(created.PesoId, cmId, ExpectedVersion: 2),
                CancellationToken.None));
            Assert.Equal(PesoRefusalReason.AlreadySubmitted, associate.Reason);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CRE8 (AC-C5): a double submit is refused with <c>AlreadySubmitted</c> and never creates a
    /// second record.
    /// </summary>
    [SkippableFact]
    public async Task CRE8_ADoubleSubmitIsRefusedAndNoSecondRowExists()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
                new SubmitPesoCommand(created.PesoId, ExpectedVersion: 1, userId),
                CancellationToken.None));

            var second = Assert.IsType<PesoResult.Refused>(await Pesos(context).SubmitAsync(
                new SubmitPesoCommand(created.PesoId, ExpectedVersion: 2, userId),
                CancellationToken.None));
            Assert.Equal(PesoRefusalReason.AlreadySubmitted, second.Reason);

            // Exactly ONE pesos row exists for this id.
            Assert.Equal(
                1,
                await CountAsync(context, "pesos WHERE peso_id = @p", new NpgsqlParameter("p", created.PesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// CRE9 (AC-C7): a draft edit carrying a stale observed version is refused with
    /// <c>StaleVersion</c> and every persisted fact stays unchanged.
    /// </summary>
    [SkippableFact]
    public async Task CRE9_AStaleDraftEditIsRefusedAndWritesNothing()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var (_, _, cmId) = await SeedCmProductionAsync(context, token);

            var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
                new CreatePesoCommand(cmId, null, 25m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));

            // Another connection bumps the row behind the observer's back.
            await using (var other = PersistenceTestDatabase.CreateContext())
            {
                await other.Database.ExecuteSqlRawAsync(
                    "UPDATE pesos SET version = version + 1 WHERE peso_id = @p",
                    new NpgsqlParameter("p", created.PesoId));
            }

            var refused = Assert.IsType<PesoResult.Refused>(await Pesos(context).UpdateAsync(
                new UpdatePesoCommand(created.PesoId, ExpectedVersion: 1, 26m, null, null, null, null, [900m]),
                CancellationToken.None));
            Assert.Equal(PesoRefusalReason.StaleVersion, refused.Reason);

            // Nothing of the losing edit was written: facts and rows unchanged, version 2 kept.
            var sheet = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
                created.PesoId, CancellationToken.None)).Sheet;
            Assert.Equal(2, sheet.Version);
            Assert.Equal(25m, sheet.WaterTemperature);
            Assert.Equal(997.1m, Assert.Single(sheet.Rows).WaterWeightG);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// MES11-DB (AC-M10 backstop): a non-positive water-density configuration is refused before any write
    /// with <c>RESULT_NON_POSITIVE</c>, and the database CHECKs reject a zero capacity/glass row with
    /// <c>23514</c> naming the contracted constraints.
    /// </summary>
    [SkippableFact]
    public async Task MES11_Db_NonPositiveResultsAreRefusedBeforeAndAtTheDatabase()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        var userId = await SeedUserAsync(context, token);

        try
        {
            var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");

            // Service level: the invalid-density configuration can never fabricate a result.
            var refused = Assert.IsType<PesoResult.ValidationFailed>(await Pesos(context, FixedCalculationConfiguration.InvalidWaterDensity).CreateAsync(
                new CreatePesoCommand(null, toolId, WaterTemperature: 20m, null, null, null, null, [997.1m], userId),
                CancellationToken.None));
            Assert.Contains(ControloCreateValidationErrors.ResultNonPositive, refused.Errors);

            Assert.Equal(
                0,
                await CountAsync(
                    context,
                    "pesos WHERE created_by_user_id = @p",
                    new NpgsqlParameter("p", userId)));

            // Database backstop: seed a valid parent Peso row, then violate the row CHECKs directly.
            var pesoId = Guid.NewGuid();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO pesos (peso_id, tool_id, water_temperature, created_by_user_id) " +
                "VALUES (@id, @tool, 25, @user)",
                new NpgsqlParameter("id", pesoId),
                new NpgsqlParameter("tool", toolId),
                new NpgsqlParameter("user", userId));

            var capacity = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO peso_measurement_rows (peso_id, row_position, water_weight_g, capacity_cm3, glass_weight_g) " +
                    "VALUES (@peso, 1, 997.1, 0, 100)",
                    new NpgsqlParameter("peso", pesoId)));
            Assert.Equal("23514", capacity.SqlState);
            Assert.Equal(PesoMeasurementRowEntityConfiguration.CapacityCheckConstraintName, capacity.ConstraintName);

            var glass = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO peso_measurement_rows (peso_id, row_position, water_weight_g, capacity_cm3, glass_weight_g) " +
                    "VALUES (@peso, 2, 997.1, 100, 0)",
                    new NpgsqlParameter("peso", pesoId)));
            Assert.Equal("23514", glass.SqlState);
            Assert.Equal(PesoMeasurementRowEntityConfiguration.GlassCheckConstraintName, glass.ConstraintName);

            // The rejected rows were never written.
            Assert.Equal(
                0,
                await CountAsync(
                    context,
                    "peso_measurement_rows WHERE peso_id = @p",
                    new NpgsqlParameter("p", pesoId)));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // --------------------------------------------------------------------------------------------
    // Composition helpers
    // --------------------------------------------------------------------------------------------

    private static ControloCreateService Pesos(
        DmoDbContext context,
        FixedCalculationConfiguration? calculation = null,
        IGlassDensitySettingsRepository? glassDensities = null) => new(
        new PesoRepository(context),
        new DmoPesoContextRead(context),
        new JobOnService(new JobOnRepository(context), new ToolRepository(context), []),
        new ToolService(new ToolRepository(context)),
        calculation ?? new FixedCalculationConfiguration(),
        glassDensities ?? new GlassDensitySettingsRepository(context));

    private static ToolService Tools(DmoDbContext context) => new(new ToolRepository(context));

    private static JobOnService JobOns(DmoDbContext context) => new(
        new JobOnRepository(context),
        new ToolRepository(context),
        []);

    private static async Task<Guid> CreateCmToolAsync(
        DmoDbContext context,
        string reference,
        string lot) =>
        Assert.IsType<ToolResult.Created>(await Tools(context).CreateAsync(
            new CreateToolCommand("CM", reference, lot, "NNPB", null, ["B1"]),
            CancellationToken.None)).ToolId;

    private static async Task<JobOnResult.Created> CreateJobOnAsync(
        DmoDbContext context,
        string reference,
        string productionNumber,
        Guid cmToolId) =>
        Assert.IsType<JobOnResult.Created>(await JobOns(context).CreateAsync(
            new CreateJobOnCommand(reference, productionNumber, "B1", null, cmToolId, null, null),
            CancellationToken.None));

    /// <summary>
    /// Seeds a user row (real <c>users</c> entity) — the <c>pesos</c> actor FKs require a real user.
    /// </summary>
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
        var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");
        var jobOn = await CreateJobOnAsync(context, $"ref-{token}", $"pn-{token}-01", toolId);

        var cmId = Guid.Parse(await ScalarAsync(
            context,
            "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOn.JobOnId)));

        return (toolId, jobOn.JobOnId, cmId);
    }

    private static DmoDbContext CreateMarkedContext(PesoVersionRaceInterceptor interceptor)
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

    /// <summary>
    /// The test-only race seam: on the repository save in flight, a second — real — database
    /// connection commits <c>SET version = version + 1</c> for the target row before the first
    /// write's guarded <c>UPDATE</c> evaluates its concurrency-token predicate (accepted
    /// <see cref="JobOnSaveTimeConcurrencyTests"/> pattern).
    /// </summary>
    private sealed class PesoVersionRaceInterceptor(Guid pesoId) : SaveChangesInterceptor
    {
        private int _bumps;

        /// <summary>The number of version bumps committed by the second connection.</summary>
        public int Bumps => _bumps;

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _bumps, 1) == 0)
            {
                using var racer = PersistenceTestDatabase.CreateContext();
                racer.Database.ExecuteSqlRaw(
                    "UPDATE pesos SET version = version + 1 WHERE peso_id = @p",
                    new NpgsqlParameter("p", pesoId));
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}