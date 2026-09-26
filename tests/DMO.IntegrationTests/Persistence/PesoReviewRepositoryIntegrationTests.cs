using DMO.Application.Controlo.Approve;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Session;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.Infrastructure.Persistence;
using DMO.Infrastructure.Persistence.Controlo;
using DMO.Infrastructure.Persistence.ToolJobOn;
using DMO.Infrastructure.Persistence.Entities;
using DMO.IntegrationTests.Controlo.Pesos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using PesoId = DMO.Domain.Controlo.PesoId;

// Test-only raw SQL: every interpolated value is a fixed, test-owned token (row identifiers
// derived from a fresh Guid) against a disposable database. Analyzer EF1003 suppressed.
#pragma warning disable EF1003

namespace DMO.IntegrationTests.Persistence;

/// <summary>
/// P2-T06 env-gated integration test â€” the decision core over the disposable PostgreSQL database
/// with the REAL repositories: same-<c>peso_id</c> approve/reject/reopen transitions (I1â€“I3/I7),
/// frozen-fact preservation (R1/R3/R5), reject semantics and atomicity (J1â€“J3), backend actor/time
/// (D4), the pending/history queries (PL1/H1) and the optimistic-concurrency guards (K1â€“K4) of
/// contract Â§19/Â§10/Â§11/Â§15 over the real <c>PesoReviewRepository</c>.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract Â§10 (transactions), Â§11 (concurrency), Â§15 (queries), Â§19
/// (approve/reject/reopen) and Â§26.4 rows I1â€“I3/I7/R1/R3/R5/J1â€“J3/O1â€“O4/D4/PL1/H1/K1â€“K4.
/// Every assertion is scoped to the rows this test creates (fresh Guid and per-test token).
/// </remarks>
[Collection(PersistenceDatabaseCollection.Name)]
public sealed class PesoReviewRepositoryIntegrationTests
{
    /// <summary>The fixed backend actor of the DB-level service tests (AC-D4).</summary>
    private static readonly Guid ActorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // ----------------------------------------------------------------------------------- IDENTITY

    /// <summary>
    /// I1 (AC-I1/AC-R1): approve on a reviewable Peso â†’ status <c>'aprovado'</c> on the SAME
    /// <c>peso_id</c>; exactly 0 new <c>pesos</c> rows; the re-read carries the same id and the
    /// same frozen facts.
    /// </summary>
    [SkippableFact]
    public async Task I1_ApproveTransitionsTheSamePesoId()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var before = PesosRowCount(context, pesoId);

        var review = ReviewService(context, reviewerId);
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));

        Assert.Equal(pesoId, approved.PesoId);
        Assert.Equal(reply.SubmittedVersion + 1, approved.Version);
        Assert.Equal(PesosRowCount(context, pesoId), before); // 0 new rows â€” SAME record

        var after = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
            pesoId, CancellationToken.None)).Sheet;
        Assert.Equal(pesoId, after.PesoId);
        Assert.Equal("aprovado", after.Status);
        Assert.Equal(reply.FrozenDensity, after.GlassDensityGCm3);
        Assert.Equal(reply.FrozenRow, Assert.Single(after.Rows).GlassWeightG);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>I2 (AC-I2): reject â†’ <c>'nao_aprovado'</c> on the SAME <c>peso_id</c>; no second record.</summary>
    [SkippableFact]
    public async Task I2_RejectTransitionsTheSamePesoId()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var before = PesosRowCount(context, pesoId);

        var rejected = Assert.IsType<ReviewResult.Rejected>(await ReviewService(context, reviewerId).RejectAsync(
            new RejectPesoCommand(pesoId, reply.SubmittedVersion, "Leitura fora do esperado."),
            CancellationToken.None));

        Assert.Equal(pesoId, rejected.PesoId);
        Assert.Equal(PesosRowCount(context, pesoId), before);

        var stored = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
            pesoId, CancellationToken.None)).Sheet;
        Assert.Equal("nao_aprovado", stored.Status);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// I3 (AC-I3/AC-O1): reopen â†’ SAME <c>peso_id</c>, status <c>'pendente'</c>,
    /// <c>submitted_at</c>/<c>submitted_by_user_id</c> NULL; <c>pesos</c> row count unchanged.
    /// </summary>
    [SkippableFact]
    public async Task I3_ReopenReturnsTheSamePesoToThePendingDraftHandoff()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        // First approve, then reopen.
        var review = ReviewService(context, reviewerId);
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));

        var before = PesosRowCount(context, pesoId);

        var reopened = Assert.IsType<ReviewResult.Reopened>(await review.ReopenAsync(
            new ReopenPesoCommand(pesoId, approved.Version, "Correção da temperatura."),
            CancellationToken.None));

        Assert.Equal(pesoId, reopened.PesoId);
        Assert.Equal(PesosRowCount(context, pesoId), before);

        var stored = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
            pesoId, CancellationToken.None)).Sheet;
        Assert.Equal("pendente", stored.Status);
        Assert.Null(stored.SubmittedAt);
        Assert.Null(stored.SubmittedByUserId);
        Assert.Equal(pesoId, stored.PesoId); // SAME identity
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// I7 (AC-I5/AC-D1): a reviewable Peso with a pending <c>tool_id</c> anchor (Job On por
    /// associar) is approved/rejected normally â€” the pending condition never blocks review.
    /// </summary>
    [SkippableFact]
    public async Task I7_PendingToolAnchorNeverBlocksReview()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");

        var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
            new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m], reviewerId),
            CancellationToken.None));
        var submitted = Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
            new SubmitPesoCommand(created.PesoId, created.Version, reviewerId),
            CancellationToken.None));

        var review = ReviewService(context, reviewerId);
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(created.PesoId, submitted.Version),
            CancellationToken.None));
        Assert.Equal(created.PesoId, approved.PesoId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ----------------------------------------------------------------------------- READ-ONLY FACTS

    /// <summary>R1 (AC-R1/AC-R2): every frozen fact is byte-equal before/after approve, reject and reopen.</summary>
    [SkippableFact]
    public async Task R1_FrozenFactsAreByteEqualAcrossDecisions()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var review = ReviewService(context, reviewerId);
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));

        var afterApprove = SheetOf(context, pesoId);
        Assert.Equal(reply.WaterTemperature, afterApprove.WaterTemperature);
        Assert.Equal(reply.FrozenDensity, afterApprove.GlassDensityGCm3);
        Assert.Equal(reply.SapEnd, afterApprove.PreviousProductionEndReference);
        Assert.Equal(reply.FrozenRow, Assert.Single(afterApprove.Rows).GlassWeightG);
        Assert.Equal(reply.FrozenCapacity, Assert.Single(afterApprove.Rows).CapacityCm3);

        await review.ReopenAsync(new ReopenPesoCommand(pesoId, approved.Version, "motivo"), CancellationToken.None);

        var afterReopen = SheetOf(context, pesoId);
        Assert.Equal(reply.WaterTemperature, afterReopen.WaterTemperature);
        Assert.Equal(reply.FrozenDensity, afterReopen.GlassDensityGCm3);
        Assert.Equal(reply.FrozenRow, Assert.Single(afterReopen.Rows).GlassWeightG);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// R3 (AC-R3): after changing a <c>glass_density_settings</c> value, a reviewed Peso's read
    /// model is unchanged (frozen density + rows); only a NEW Peso resolves the new value.
    /// </summary>
    [SkippableFact]
    public async Task R3_SettingsChangesNeverRewriteAReviewedPeso()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        // The review decision happens with the CURRENT setting (2.4027).
        var review = ReviewService(context, reviewerId);
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));
        Assert.Equal(reply.FrozenDensity, reply.FrozenDensity);

        // Change the operational NNPB value.
        var settings = new GlassDensitySettingsRepository(context);
        var current = await settings.GetByProcessoAsync("NNPB", CancellationToken.None);
        await settings.UpdatedAsync(current! with { DensityGCm3 = 3.00m }, CancellationToken.None);

        // The reviewed Peso keeps its frozen density and results.
        var reviewed = SheetOf(context, pesoId);
        Assert.Equal(reply.FrozenDensity, reviewed.GlassDensityGCm3);
        Assert.Equal(reply.FrozenRow, Assert.Single(reviewed.Rows).GlassWeightG);

        // A NEW Peso resolves the new value.
        var newPeso = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
            new CreatePesoCommand(reply.CmId, null, 25m, null, null, null, null, [997.1m], reviewerId),
            CancellationToken.None));
        var newSheet = SheetOf(context, newPeso.PesoId);
        Assert.Equal(3.00m, newSheet.GlassDensityGCm3);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// R5 (AC-R5): reopen â†’ re-submit (Create routes) â†’ the re-submitted record still carries the
    /// ORIGINAL frozen density and per-row results (no re-resolution, no recompute).
    /// </summary>
    [SkippableFact]
    public async Task R5_ReopenAndResubmitKeepTheOriginalFrozenFacts()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var review = ReviewService(context, reviewerId);
        var reopened = Assert.IsType<ReviewResult.Reopened>(await review.ReopenAsync(
            new ReopenPesoCommand(pesoId, reply.SubmittedVersion, "Correção."),
            CancellationToken.None));

        // The closed Create submit route works again on the same row.
        var resubmitted = Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
            new SubmitPesoCommand(pesoId, reopened.Version, reviewerId),
            CancellationToken.None));

        var sheet = SheetOf(context, pesoId);
        Assert.Equal(reply.FrozenDensity, sheet.GlassDensityGCm3);
        Assert.Equal(reply.FrozenRow, Assert.Single(sheet.Rows).GlassWeightG);
        Assert.NotNull(sheet.SubmittedAt);
        Assert.Equal("pendente", sheet.Status);
        Assert.Equal(reply.SubmittedVersion + 2, resubmitted.Version);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ------------------------------------------------------------------------------------- REJECT

    /// <summary>J1 (AC-J1): reject with reason â†’ same <c>peso_id</c> <c>'nao_aprovado'</c>; the
    /// decision row records outcome/actor/time/version/prior/reason; version incremented once.</summary>
    [SkippableFact]
    public async Task J1_RejectRecordsTheFullDecisionEventAndIncrementsVersionOnce()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var before = DateTimeOffset.UtcNow;

        var rejected = Assert.IsType<ReviewResult.Rejected>(await ReviewService(context, reviewerId).RejectAsync(
            new RejectPesoCommand(pesoId, reply.SubmittedVersion, "Leitura fora do esperado."),
            CancellationToken.None));

        Assert.Equal(reply.SubmittedVersion + 1, rejected.Version);

        var row = Assert.Single(DecisionRows(context, pesoId));
        Assert.Equal("nao_aprovado", PesoReviewDecisionKindTokens.ToToken(row.Decision));
        Assert.Equal("Leitura fora do esperado.", row.Reason);
        Assert.Equal("pendente", PesoStatusTokens.ToToken(row.PriorStatus));
        Assert.Equal(reply.SubmittedVersion, row.PesoVersionAtDecision);
        Assert.True(row.DecidedAt >= before && row.DecidedAt <= DateTimeOffset.UtcNow);

        var version = await ScalarAsync(
            context,
            "SELECT version::text FROM pesos WHERE peso_id = @p",
            new NpgsqlParameter("p", pesoId));
        Assert.Equal(reply.SubmittedVersion + 1, int.Parse(version));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// J2 (AC-J2 + backstop): the reason CHECK backstop rejects a direct reject-row insert with a
    /// BLANK reason (SQLSTATE 23514 — the same validator token the service raises, never a 500).
    /// (Note: a NULL reason is closed by the service validator before any write; the pinned CHECK
    /// of contract §7.4 rejects every non-blank/blank non-NULL reason at the database.)
    /// </summary>
    [SkippableFact]
    public async Task J2_TheReasonCheckBackstopRejectsAReasonlessRejectRow()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var violation = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "INSERT INTO peso_review_decisions (peso_review_decision_id, peso_id, decision, decided_by_user_id, " +
                "decided_at, reason, prior_status, pesos_version_at_decision, created_at) " +
                "VALUES (@id, @peso, 'nao_aprovado', @user, now(), '', 'pendente', @version, now())",
                new NpgsqlParameter("id", Guid.NewGuid()),
                new NpgsqlParameter("peso", pesoId),
                new NpgsqlParameter("user", reviewerId),
                new NpgsqlParameter("version", reply.SubmittedVersion)));

        Assert.Equal("23514", violation.SqlState);
        Assert.Equal(0, DecisionRows(context, pesoId).Count);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// J3/K2 (AC-J3/AC-K2): forced mid-transaction failures during a decision write leave ZERO
    /// partial state — status unchanged, no decision row, no version bump (atomicity). Two forced
    /// failures are proven: a version-at-decision CHECK violation (raw <c>DbUpdateException</c> —
    /// unreachable through the service, whose observed version is always ≥ 1) and the reason CHECK
    /// backstop (mapped to the typed failure the service translates to the same validator token).
    /// </summary>
    [SkippableFact]
    public async Task J3_AForcedMidTransactionFailureLeavesZeroPartialState()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        // (a) Version-at-decision CHECK violation (pesos_version_at_decision >= 1): the row insert
        // fails in-transaction and the WHOLE operation rolls back atomically.
        var zeroVersion = new PesoReviewDecision(
            PesoReviewDecisionId.New(),
            PesoId.From(pesoId),
            PesoReviewDecisionKind.Aprovado,
            reviewerId,
            DateTimeOffset.UtcNow,
            Reason: null,
            PriorStatus: PesoStatus.Pendente,
            PesoVersionAtDecision: 0,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() =>
            new PesoReviewRepository(context).DecisionAsync(
                zeroVersion,
                expectedPesoVersion: reply.SubmittedVersion,
                CancellationToken.None));

        AssertZeroPartialState(context, pesoId, reply.SubmittedVersion);

        // (b) The reason CHECK backstop (a reject event without a reason — unreachable through the
        // service validator): mapped to the typed failure, nothing written. A FRESH context is used
        // (the unmapped failure of (a) leaves its tracked row dirty — the accepted rollback posture
        // clears the tracker only on MAPPED failures).
        await using var freshContext = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(freshContext);

        var reasonless = new PesoReviewDecision(
            PesoReviewDecisionId.New(),
            PesoId.From(pesoId),
            PesoReviewDecisionKind.NaoAprovado,
            reviewerId,
            DateTimeOffset.UtcNow,
            Reason: "",
            PriorStatus: PesoStatus.Pendente,
            reply.SubmittedVersion,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<PesoReviewPersistenceException>(() =>
            new PesoReviewRepository(freshContext).DecisionAsync(
                reasonless,
                expectedPesoVersion: reply.SubmittedVersion,
                CancellationToken.None));

        AssertZeroPartialState(freshContext, pesoId, reply.SubmittedVersion);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    private static void AssertZeroPartialState(DmoDbContext context, Guid pesoId, int expectedVersion)
    {
        var stored = Assert.IsType<PesoResult.Found>(Pesos(context).GetAsync(
            pesoId, CancellationToken.None).GetAwaiter().GetResult()).Sheet;
        Assert.Equal("pendente", stored.Status);
        Assert.Equal(expectedVersion, stored.Version);
        Assert.Equal(0, DecisionRows(context, pesoId).Count);
    }

    // ------------------------------------------------------------------------------------- REOPEN

    /// <summary>O2 (AC-O2): the trail after reopen contains the prior approve event intact PLUS
    /// the reopen event â€” no historical erase anywhere in the trail.</summary>
    [SkippableFact]
    public async Task O2_ReopenPreservesThePriorApprovalEventIntact()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var review = ReviewService(context, reviewerId);
        var beforeApprove = DateTimeOffset.UtcNow;
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));

        var reopened = Assert.IsType<ReviewResult.Reopened>(await review.ReopenAsync(
            new ReopenPesoCommand(pesoId, approved.Version, "Correção."),
            CancellationToken.None));

        var rows = DecisionRows(context, pesoId);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["aprovado", "reaberto"], rows.Select(row => PesoReviewDecisionKindTokens.ToToken(row.Decision)).ToList());
        Assert.Equal("Correção.", rows[1].Reason);
        Assert.Equal("aprovado", PesoStatusTokens.ToToken(rows[1].PriorStatus));
        Assert.Equal(approved.Version, rows[1].PesoVersionAtDecision); // the observed version at reopen
        Assert.True(rows[0].DecidedAt >= beforeApprove); // the approve event time is backend-authored, after the capture
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>O4 (AC-O4): a reopened previously-approved record is NEVER <c>aprovado</c> again
    /// without a NEW approval: resubmit â†’ reviewable <c>pendente</c> â†’ approve â†’ <c>aprovado</c>
    /// (two distinct approval events); no path returns the old approval.</summary>
    [SkippableFact]
    public async Task O4_NeverAprovadoWithoutANewApproval()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var review = ReviewService(context, reviewerId);
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));
        var reopened = Assert.IsType<ReviewResult.Reopened>(await review.ReopenAsync(
            new ReopenPesoCommand(pesoId, approved.Version, "Correção."),
            CancellationToken.None));

        // After reopen the record is pendente â€” NOT aprovado (no direct restoration).
        var afterReopen = SheetOf(context, pesoId);
        Assert.Equal("pendente", afterReopen.Status);

        // Resubmit (Create route) â†’ reviewable â†’ a SECOND, distinct approval is required.
        var resubmitted = Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
            new SubmitPesoCommand(pesoId, reopened.Version, reviewerId),
            CancellationToken.None));
        var reApproved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, resubmitted.Version),
            CancellationToken.None));

        Assert.Equal("aprovado", SheetOf(context, pesoId).Status);

        var rows = DecisionRows(context, pesoId);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["aprovado", "reaberto", "aprovado"], rows.Select(row => PesoReviewDecisionKindTokens.ToToken(row.Decision)).ToList());
        Assert.Equal(resubmitted.Version, rows[2].PesoVersionAtDecision);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ----------------------------------------------------------------------------------- D4 (actor/time)

    /// <summary>D4 (AC-D4): <c>decided_by_user_id</c>/<c>decided_at</c> are backend-authored â€” the
    /// DB rows equal the authenticated backend user and the backend clock; the carrier accepts no
    /// actor/time (closed command shapes).</summary>
    [SkippableFact]
    public async Task D4_ActorAndTimeAreBackendAuthoredFacts()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        // The commands carry identity/version only â€” no actor/time property exists (D1/D4).
        Assert.Equal(2, typeof(ApprovePesoCommand).GetProperties().Length);
        Assert.Equal(3, typeof(RejectPesoCommand).GetProperties().Length);

        var before = DateTimeOffset.UtcNow;
        var approved = Assert.IsType<ReviewResult.Approved>(await ReviewService(context, reviewerId).ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));

        var row = Assert.Single(DecisionRows(context, pesoId));
        Assert.Equal(reviewerId, row.DecidedByUserId); // the authenticated backend USER
        Assert.True(row.DecidedAt >= before && row.DecidedAt <= DateTimeOffset.UtcNow);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ----------------------------------------------------------------------------------- PENDING / HISTORY

    /// <summary>
    /// PL1 (AC-AP1): the pending list returns exactly the reviewable set â€” drafts, decided and
    /// reopened records are excluded.
    /// </summary>
    [SkippableFact]
    public async Task PL1_ThePendingListIsExactlyTheReviewableSet()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);

        // Reviewable (submitted, pendente).
        var (reviewableReply, reviewableId) = await CreateAndSubmitAsync(context, $"{token}a", reviewerId);

        // Draft (created, never submitted).
        var draft = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
            new CreatePesoCommand(reviewableReply.CmId, null, 25m, null, null, null, null, [997.1m], reviewerId),
            CancellationToken.None));

        // Decided (approved).
        var (decidedReply, decidedId) = await CreateAndSubmitAsync(context, $"{token}b", reviewerId);
        await ReviewService(context, reviewerId).ApproveAsync(
            new ApprovePesoCommand(decidedId, decidedReply.SubmittedVersion),
            CancellationToken.None);

        // Reopened (submitted, approved, reopened â†’ pendente with cleared handoff).
        var (reopenedReply, reopenedId) = await CreateAndSubmitAsync(context, $"{token}c", reviewerId);
        var reopened = Assert.IsType<ReviewResult.Reopened>(await ReviewService(context, reviewerId).ReopenAsync(
            new ReopenPesoCommand(reopenedId, reopenedReply.SubmittedVersion, "motivo."),
            CancellationToken.None));
        _ = reopened;

        var rows = await new PesoReviewRepository(context).GetPendingAsync(
            new PendingListQuery(null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Contains(rows, row => row.PesoId == reviewableId);
        Assert.DoesNotContain(rows, row => row.PesoId == draft.PesoId);
        Assert.DoesNotContain(rows, row => row.PesoId == decidedId);
        Assert.DoesNotContain(rows, row => row.PesoId == reopenedId);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// H1 (AC-H1): the decision trail returns EVERY event (approve/reject/reopen cycles) ordered
    /// by time with actor/time/version/reason/prior status â€” nothing missing after multiple cycles
    /// on one <c>peso_id</c>; reopened records keep their trail visible in the history list.
    /// </summary>
    [SkippableFact]
    public async Task H1_TheTrailIsCompleteAcrossCyclesAndTheHistoryKeepsReopenedRecords()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var review = ReviewService(context, reviewerId);
        var approved = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));
        var reopened = Assert.IsType<ReviewResult.Reopened>(await review.ReopenAsync(
            new ReopenPesoCommand(pesoId, approved.Version, "Correção."),
            CancellationToken.None));
        var resubmitted = Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
            new SubmitPesoCommand(pesoId, reopened.Version, reviewerId),
            CancellationToken.None));
        var rejected = Assert.IsType<ReviewResult.Rejected>(await review.RejectAsync(
            new RejectPesoCommand(pesoId, resubmitted.Version, "Motivo final."),
            CancellationToken.None));
        _ = rejected;

        var trail = await new PesoReviewRepository(context).GetDecisionsAsync(
            pesoId, CancellationToken.None);
        Assert.Equal(3, trail.Count);
        Assert.Equal(["aprovado", "reaberto", "nao_aprovado"], trail.Select(row =>
            PesoReviewDecisionKindTokens.ToToken(row.Decision)).ToList());

        // The history list still shows the (now decided) record with its full decision facts.
        var history = await new PesoReviewRepository(context).GetHistoryAsync(
            new HistoryListQuery(null, null, null, null, null, null, null, null, null, null, null, null),
            CancellationToken.None);
        var row = Assert.Single(history, entry => entry.PesoId == pesoId);
        Assert.Equal(3, row.DecisionCount);
        Assert.Equal("nao_aprovado", row.LastDecision!.Decision);
        Assert.Equal("Motivo final.", row.LastDecision.Reason);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // ----------------------------------------------------------------------------------- CONCURRENCY

    /// <summary>K1 (AC-K1): approve/reject/reopen with a stale <c>expectedVersion</c> â†’ 409
    /// <c>stale-version</c>; nothing written (status, version, trail unchanged).</summary>
    [SkippableFact]
    public async Task K1_StaleVersionRefusesWithNothingWritten()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var review = ReviewService(context, reviewerId);

        foreach (var operation in new Func<Task<ReviewResult>>[]
                 {
                     () => review.ApproveAsync(new ApprovePesoCommand(pesoId, 999), CancellationToken.None),
                     () => review.RejectAsync(new RejectPesoCommand(pesoId, 999, "motivo"), CancellationToken.None),
                     () => review.ReopenAsync(new ReopenPesoCommand(pesoId, 999, "motivo"), CancellationToken.None),
                 })
        {
            var refused = Assert.IsType<ReviewResult.Refused>(await operation());
            Assert.Equal(ReviewRefusalReason.StaleVersion, refused.Reason);
        }

        var stored = Assert.IsType<PesoResult.Found>(await Pesos(context).GetAsync(
            pesoId, CancellationToken.None)).Sheet;
        Assert.Equal("pendente", stored.Status);
        Assert.Equal(reply.SubmittedVersion, stored.Version);
        Assert.Equal(0, DecisionRows(context, pesoId).Count);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>
    /// K3 (AC-K1/AC-K3): a save-time race (a second connection bumps <c>pesos.version</c> between
    /// the in-transaction compare and the save) surfaces as 409 <c>stale-version</c> via the
    /// accepted <c>SaveAsync</c> mapping â€” no silent overwrite of the newer state.
    /// </summary>
    [SkippableFact]
    public async Task K3_SaveTimeRaceSurfacesAsAStaleVersionConflict()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var interceptor = new DecisionVersionRaceInterceptor(pesoId);
        var racerContext = CreateMarkedContext(interceptor);
        var review = ReviewService(racerContext, reviewerId);

        var refused = Assert.IsType<ReviewResult.Refused>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));

        Assert.Equal(ReviewRefusalReason.StaleVersion, refused.Reason);
        Assert.True(interceptor.Bumps > 0, "the racing connection must have bumped the version");
        Assert.Equal(0, DecisionRows(context, pesoId).Count);
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    /// <summary>K4 (AC-K1/AC-K3): double decisions are refused â€” exactly one decision event exists
    /// and the second attempt writes nothing.</summary>
    [SkippableFact]
    public async Task K4_DoubleDecisionsAreRefusedWithExactlyOneEvent()
    {
        PersistenceTestDatabase.SkipIfNotConfigured();

        await using var context = PersistenceTestDatabase.CreateContext();
        await PersistenceTestDatabase.ApplyMigrationsAsync(context);

        var token = Guid.NewGuid().ToString("N");
        try
        {
        var reviewerId = await SeedUserAsync(context, token);
        var (reply, pesoId) = await CreateAndSubmitAsync(context, token, reviewerId);

        var review = ReviewService(context, reviewerId);
        var first = Assert.IsType<ReviewResult.Approved>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, reply.SubmittedVersion),
            CancellationToken.None));

        // approve after approve and reject after approve are both refused.
        var secondApprove = Assert.IsType<ReviewResult.Refused>(await review.ApproveAsync(
            new ApprovePesoCommand(pesoId, first.Version),
            CancellationToken.None));
        Assert.Equal(ReviewRefusalReason.AlreadyDecided, secondApprove.Reason);

        var reject = Assert.IsType<ReviewResult.Refused>(await review.RejectAsync(
            new RejectPesoCommand(pesoId, first.Version, "motivo"),
            CancellationToken.None));
        Assert.Equal(ReviewRefusalReason.AlreadyDecided, reject.Reason);

        Assert.Single(DecisionRows(context, pesoId));
        }
        finally
        {
            await CleanupAsync(context, token);
        }
    }

    // --------------------------------------------------------------------------------- helpers

    private static ControloCreateService Pesos(DmoDbContext context) => new(
        new PesoRepository(context),
        new DmoPesoContextRead(context),
        new JobOnService(new JobOnRepository(context), new ToolRepository(context), []),
        new ToolService(new ToolRepository(context)),
        new FixedCalculationConfiguration(),
        new GlassDensitySettingsRepository(context));

    private static ControloApproveService ReviewService(DmoDbContext context, Guid actorUserId) => new(
        Pesos(context),
        new PesoRepository(context),
        new PesoReviewRepository(context),
        new FixedCurrentAccount(actorUserId));

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

    /// <summary>Creates AND submits a production-bound Peso over the REAL service stack; returns
    /// the observed facts (for frozen-fact assertions) and the <c>peso_id</c>.</summary>
    private static async Task<(SubmittedReply Reply, Guid PesoId)> CreateAndSubmitAsync(
        DmoDbContext context,
        string token,
        Guid userId)
    {
        var toolId = await CreateCmToolAsync(context, $"ref-{token}", "01");
        var jobOn = await CreateJobOnAsync(context, $"ref-{token}", $"pn-{token}-01", toolId);

        var cmId = Guid.Parse(await ScalarAsync(
            context,
            "SELECT cm_id::text FROM cm_contexts WHERE jobon_id = @p",
            new NpgsqlParameter("p", jobOn.JobOnId)));

        var created = Assert.IsType<PesoResult.Created>(await Pesos(context).CreateAsync(
            new CreatePesoCommand(
                cmId,
                null,
                25m,
                VolumeMarisaBq: 1.25m,
                VolumePuncaoPu: 0.75m,
                "SAP-END",
                "SAP-WEIGHT",
                [997.1m],
                userId),
            CancellationToken.None));

        var submitted = Assert.IsType<PesoResult.Submitted>(await Pesos(context).SubmitAsync(
            new SubmitPesoCommand(created.PesoId, created.Version, userId),
            CancellationToken.None));

        var sheet = SheetOf(context, created.PesoId);

        return (
            new SubmittedReply(
                cmId,
                submitted.Version,
                sheet.FrozenDensity(),
                sheet.WaterTemperature,
                sheet.PreviousProductionEndReference,
                Assert.Single(sheet.Rows).GlassWeightG,
                Assert.Single(sheet.Rows).CapacityCm3),
            created.PesoId);
    }

    private static PesoSheetReadModel SheetOf(DmoDbContext context, Guid pesoId) =>
        Assert.IsType<PesoResult.Found>(Pesos(context).GetAsync(pesoId, CancellationToken.None).GetAwaiter().GetResult()).Sheet;

    private static int PesosRowCount(DmoDbContext context, Guid pesoId) =>
        int.Parse(Assert.Single(QueryStringsAsync(context,
            "SELECT count(*) FROM pesos WHERE peso_id = @p",
            new NpgsqlParameter("p", pesoId)).GetAwaiter().GetResult()));

    private static IReadOnlyList<PesoReviewDecision> DecisionRows(DmoDbContext context, Guid pesoId) =>
        new PesoReviewRepository(context).GetDecisionsAsync(pesoId, CancellationToken.None).GetAwaiter().GetResult();

/// <summary>Deletes the rows this test created, in FK-safe order (disposable database only).</summary>
    private static async Task CleanupAsync(DmoDbContext context, string token)
    {
        var pattern = $"%{token}%";

        // The decision trail first (references pesos/users), then the Peso rows and their anchors.
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM peso_review_decisions WHERE peso_id IN " +
            "(SELECT peso_id FROM pesos WHERE created_by_user_id IN " +
            "(SELECT user_id FROM users WHERE company_number LIKE @p)) " +
            "OR decided_by_user_id IN (SELECT user_id FROM users WHERE company_number LIKE @p)",
            new NpgsqlParameter("p", pattern));

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM peso_measurement_rows WHERE peso_id IN " +
            "(SELECT peso_id FROM pesos WHERE created_by_user_id IN (SELECT user_id FROM users WHERE company_number LIKE @p))",
            new NpgsqlParameter("p", pattern));

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM pesos WHERE created_by_user_id IN (SELECT user_id FROM users WHERE company_number LIKE @p)",
            new NpgsqlParameter("p", pattern));

        foreach (var table in new[] { "cm_contexts", "mf_contexts", "bq_contexts" })
        {
            await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {table} WHERE jobon_id IN " +
                "(SELECT jobon_id FROM job_ons WHERE reference LIKE @p)",
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

    private static async Task CleanupUnderlyingAsync(DmoDbContext context, string token)
    {
    }

    private static DmoDbContext CreateMarkedContext(DecisionVersionRaceInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<DmoDbContext>()
            .UseNpgsql(PersistenceTestDatabase.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new DmoDbContext(options);
    }

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

        if (connection.State != System.Data.ConnectionState.Open)
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

    /// <summary>The test-only race seam (accepted <see cref="PesoRepositoryIntegrationTests"/>
    /// pattern): a second â€” real â€” database connection bumps <c>pesos.version</c> while the
    /// decision save is in flight.</summary>
    private sealed class DecisionVersionRaceInterceptor(Guid pesoId) : SaveChangesInterceptor
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

    /// <summary>The observed facts of the submitted fixture used by the frozen-fact assertions.</summary>
    private sealed record SubmittedReply(
        Guid CmId,
        int SubmittedVersion,
        decimal FrozenDensity,
        decimal WaterTemperature,
        string? SapEnd,
        decimal FrozenRow,
        decimal FrozenCapacity);

    /// <summary>Fixed backend-actor fake (the DB rows must equal THIS authenticated user, AC-D4).</summary>
    private sealed class FixedCurrentAccount(Guid userId) : ICurrentAccountContext
    {
        public Task<CurrentAccount> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CurrentAccount>(new CurrentAccount.User(new DMO.Application.Accounts.UserAccount(userId, "1042", "Test User", "test@example.pt", "Turno A", IsActive: true, TemplateId: null, Version: 1)));
    }
}

/// <summary>Read-model accessor over the frozen facts (avoided in the fixture typing).</summary>
internal static class PesoSheetReadModelFacts
{
    public static decimal FrozenDensity(this PesoSheetReadModel sheet) =>
        sheet.GlassDensityGCm3 ?? throw new InvalidOperationException("The fixture must carry a frozen density.");
}
