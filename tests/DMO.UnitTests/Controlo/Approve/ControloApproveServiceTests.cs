using DMO.Application.Controlo.Approve;
using DMO.Application.Controlo.Pesos;
using DMO.Domain.Controlo;
using DMO.UnitTests.Controlo.Approve.Fakes;
using PesoId = DMO.Domain.Controlo.PesoId;

namespace DMO.UnitTests.Controlo.Approve;

/// <summary>
/// P2-T06 unit tests â€” the decision service semantics over the in-memory fakes: drafts are never
/// decided (D5/AC-D1/K3), approve/reject/reopen mutate the SAME <c>peso_id</c> with backend
/// actor/time (AC-D4), stale versions refuse with zero writes (AC-K1), decided records refuse a
/// second event (AC-K3) and reopen restores the pending handoff for Create (AC-O1).
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract Â§11/Â§19 and Â§26.4 rows D5/K1/K3/O1/D4 (the database proofs of the
/// same rows live in <c>PesoReviewRepositoryIntegrationTests</c>).
/// </remarks>
public sealed class ControloApproveServiceTests
{
    private static readonly Guid PesoIdValue = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ControloApproveService CreateService(
        FakePesoRepository pesos,
        FakePesoReviewRepository review,
        FixedCurrentAccount account) =>
        new(
            new FakeControloCreateService(),
            pesos,
            review,
            account);

    private static Peso ReviewablePeso() => new(
        PesoId.From(PesoIdValue),
        CmId: null,
        ToolId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
        PesoStatus.Pendente,
        SubmittedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
        SubmittedByUserId: FixedCurrentAccount.UserId,
        WaterTemperature: 25m,
        VolumeMarisaBq: null,
        VolumePuncaoPu: null,
        GlassDensityGCm3: 2.4027m,
        PreviousProductionEndReference: null,
        PreviousAverageWeightReference: null,
        Version: 1,
        CreatedByUserId: FixedCurrentAccount.UserId,
        CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
        UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-6),
        Rows:
        [
            new PesoMeasurementRow(
                PesoMeasurementRowId.New(),
                PesoId.From(PesoIdValue),
                1,
                997.1m,
                1000m,
                2402.7000m,
                DateTimeOffset.UtcNow.AddHours(-1)),
        ]);

    private static Peso DraftPeso() => ReviewablePeso() with { SubmittedAt = null, SubmittedByUserId = null };

    private static Peso DecidedPeso(PesoStatus status) => ReviewablePeso() with { Status = status };

    private static ControloApproveService Service(out FakePesoRepository pesos, out FakePesoReviewRepository review, out FixedCurrentAccount account)
    {
        pesos = new FakePesoRepository();
        review = new FakePesoReviewRepository();
        account = FixedCurrentAccount.User();
        return CreateService(pesos, review, account);
    }

    // ---- D5 (AC-D1/AC-K3): a draft never entered the review circuit --------------------------

    [Fact]
    public async Task D5_ApproveOnADraftRefusesNotReviewableWithNoEventAndNoStatusChange()
    {
        var service = Service(out var pesos, out var review, out _);
        var draft = DraftPeso();
        pesos.Seed(draft);
        review.Seed(draft.PesoId.Value, PesoStatus.Pendente);

        var result = await service.ApproveAsync(
            new ApprovePesoCommand(draft.PesoId.Value, ExpectedVersion: 1),
            CancellationToken.None);

        var refused = Assert.IsType<ReviewResult.Refused>(result);
        Assert.Equal(ReviewRefusalReason.NotReviewable, refused.Reason);
        Assert.Equal(0, review.WriteAttempts);
        Assert.Empty(review.Decisions);
    }

    [Fact]
    public async Task D5_RejectAndReopenOnADraftAlsoRefuseNotReviewable()
    {
        var service = Service(out var pesos, out var review, out _);
        var draft = DraftPeso();
        pesos.Seed(draft);
        review.Seed(draft.PesoId.Value, PesoStatus.Pendente);

        var reject = await service.RejectAsync(
            new RejectPesoCommand(draft.PesoId.Value, ExpectedVersion: 1, "não interessa"),
            CancellationToken.None);
        Assert.Equal(ReviewRefusalReason.NotReviewable, Assert.IsType<ReviewResult.Refused>(reject).Reason);

        var reopen = await service.ReopenAsync(
            new ReopenPesoCommand(draft.PesoId.Value, ExpectedVersion: 1, "não interessa"),
            CancellationToken.None);
        Assert.Equal(ReviewRefusalReason.NotReviewable, Assert.IsType<ReviewResult.Refused>(reopen).Reason);

        Assert.Equal(0, review.WriteAttempts);
        Assert.Empty(review.Decisions);
    }

    // ---- Approve (AC-I1/AC-D4/AC-O4): same id, backend actor/time, human-only ----------------

    [Fact]
    public async Task Approve_MutatesTheSamePesoIdWithBackendActorAndTime()
    {
        var service = Service(out var pesos, out var review, out _);
        var peso = ReviewablePeso();
        pesos.Seed(peso);
        review.Seed(peso.PesoId.Value, PesoStatus.Pendente);
        var before = DateTimeOffset.UtcNow;

        var result = await service.ApproveAsync(
            new ApprovePesoCommand(peso.PesoId.Value, ExpectedVersion: 1),
            CancellationToken.None);

        var approved = Assert.IsType<ReviewResult.Approved>(result);
        Assert.Equal(peso.PesoId.Value, approved.PesoId);
        Assert.Equal(2, approved.Version);
        Assert.True(approved.DecidedAt >= before);

        var decision = Assert.Single(review.Decisions);
        Assert.Equal(peso.PesoId.Value, decision.PesoId.Value);
        Assert.Equal(PesoReviewDecisionKind.Aprovado, decision.Decision);
        Assert.Equal(FixedCurrentAccount.UserId, decision.DecidedByUserId); // backend actor
        Assert.NotEqual(default, decision.DecidedAt);
        Assert.Null(decision.Reason);
        Assert.Equal(PesoStatus.Pendente, decision.PriorStatus);
        Assert.Equal(1, decision.PesoVersionAtDecision);
    }

    // ---- Reject (AC-J1): same id, reason stored, backend actor/time --------------------------

    [Fact]
    public async Task Reject_TransitionsTheSamePesoIdWithReasonAndBackendAttribution()
    {
        var service = Service(out var pesos, out var review, out _);
        var peso = ReviewablePeso();
        pesos.Seed(peso);
        review.Seed(peso.PesoId.Value, PesoStatus.Pendente);

        var result = await service.RejectAsync(
            new RejectPesoCommand(peso.PesoId.Value, ExpectedVersion: 1, "  Leitura fora do tolerável.  "),
            CancellationToken.None);

        var rejected = Assert.IsType<ReviewResult.Rejected>(result);
        Assert.Equal(peso.PesoId.Value, rejected.PesoId);

        var decision = Assert.Single(review.Decisions);
        Assert.Equal(PesoReviewDecisionKind.NaoAprovado, decision.Decision);
        Assert.Equal("Leitura fora do tolerável.", decision.Reason); // trimmed, non-blank
        Assert.Equal(FixedCurrentAccount.UserId, decision.DecidedByUserId);
        Assert.Equal(1, decision.PesoVersionAtDecision);
    }

    // ---- Reopen (AC-O1/O2): same id, pendente handoff, prior history preserved ----------------

    [Fact]
    public async Task Reopen_ReturnsTheSamePesoToPendenteAndPreservesTheTrail()
    {
        var service = Service(out var pesos, out var review, out _);
        var peso = ReviewablePeso() with { Status = PesoStatus.Aprovado };
        pesos.Seed(peso);
        review.Seed(peso.PesoId.Value, PesoStatus.Aprovado);

        // A prior approval event already exists (the trail the reopen must preserve).
        ((System.Collections.Generic.List<PesoReviewDecision>)review.Decisions).Add(new PesoReviewDecision(
            PesoReviewDecisionId.New(),
            peso.PesoId,
            PesoReviewDecisionKind.Aprovado,
            FixedCurrentAccount.UserId,
            DateTimeOffset.UtcNow.AddHours(-1),
            Reason: null,
            PriorStatus: PesoStatus.Pendente,
            PesoVersionAtDecision: 1,
            DateTimeOffset.UtcNow.AddHours(-1)));

        var result = await service.ReopenAsync(
            new ReopenPesoCommand(peso.PesoId.Value, ExpectedVersion: 1, "Correção da temperatura."),
            CancellationToken.None);

        var reopened = Assert.IsType<ReviewResult.Reopened>(result);
        Assert.Equal(peso.PesoId.Value, reopened.PesoId);

        Assert.Equal(2, review.Decisions.Count); // prior approval + reopen, nothing erased
        var reopenEvent = review.Decisions[1];
        Assert.Equal(PesoReviewDecisionKind.Reaberto, reopenEvent.Decision);
        Assert.Equal("Correção da temperatura.", reopenEvent.Reason);
        Assert.Equal(PesoStatus.Aprovado, reopenEvent.PriorStatus);
    }

    // ---- Concurrency (AC-K1): a stale version refuses with zero writes ------------------------

    [Fact]
    public async Task K1_StaleVersionRefusesStaleVersionWithNoEventWritten()
    {
        var service = Service(out var pesos, out var review, out _);
        var peso = ReviewablePeso();
        pesos.Seed(peso);
        review.Seed(peso.PesoId.Value, PesoStatus.Pendente);

        var result = await service.ApproveAsync(
            new ApprovePesoCommand(peso.PesoId.Value, ExpectedVersion: 99),
            CancellationToken.None);

        var refused = Assert.IsType<ReviewResult.Refused>(result);
        Assert.Equal(ReviewRefusalReason.StaleVersion, refused.Reason);
        Assert.Equal(0, review.WriteAttempts);
        Assert.Empty(review.Decisions);
    }

    // ---- Double-decision guard (AC-K3): no second event, no flip ------------------------------

    [Fact]
    public async Task K3_DecidedRecordsRefuseADecisionWithAlreadyDecided()
    {
        foreach (var decided in new[] { PesoStatus.Aprovado, PesoStatus.NaoAprovado })
        {
            var service = Service(out var pesos, out var review, out _);
            var peso = DecidedPeso(decided);
            pesos.Seed(peso);
            review.Seed(peso.PesoId.Value, decided);

            var approve = await service.ApproveAsync(
                new ApprovePesoCommand(peso.PesoId.Value, ExpectedVersion: 1),
                CancellationToken.None);
            Assert.Equal(ReviewRefusalReason.AlreadyDecided, Assert.IsType<ReviewResult.Refused>(approve).Reason);

            var reject = await service.RejectAsync(
                new RejectPesoCommand(peso.PesoId.Value, ExpectedVersion: 1, "motivo"),
                CancellationToken.None);
            Assert.Equal(ReviewRefusalReason.AlreadyDecided, Assert.IsType<ReviewResult.Refused>(reject).Reason);

            Assert.Equal(0, review.WriteAttempts);
            Assert.Empty(review.Decisions);
        }
    }

    // ---- Review sheet (RD1/AC-AP2): the shared read model + availability ----------------------

    [Fact]
    public async Task ReviewSheet_EmbedsTheSharedReadModelAndComputesAvailabilityFromFacts()
    {
        var sheet = new PesoSheetReadModel(
            PesoIdValue,
            Version: 3,
            "pendente",
            CmId: null,
            ToolId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Context: null,
            Pending: null,
            Production: null,
            CreatedByUserId: FixedCurrentAccount.UserId,
            CreatedAt: DateTimeOffset.UtcNow.AddHours(-2),
            SubmittedByUserId: FixedCurrentAccount.UserId,
            SubmittedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            WaterTemperature: 25m,
            VolumeMarisaBq: null,
            VolumePuncaoPu: null,
            GlassDensityGCm3: 2.4027m,
            PreviousProductionEndReference: null,
            PreviousAverageWeightReference: null,
            Rows: []);

        var service = new ControloApproveService(
            new FakeControloCreateService(sheet),
            new FakePesoRepository(),
            new FakePesoReviewRepository(),
            FixedCurrentAccount.User());

        var result = await service.GetReviewSheetAsync(PesoIdValue, CancellationToken.None);

        var review = Assert.IsType<ReviewResult.ReviewSheet>(result);
        Assert.Same(sheet, review.Sheet.Peso); // RD1: the EXACT shared instance/type â€” no copy
        Assert.True(review.Sheet.Availability.CanApprove);
        Assert.True(review.Sheet.Availability.CanReject);
        Assert.True(review.Sheet.Availability.CanReopen);
    }

    [Fact]
    public async Task ReviewSheet_NotFoundWhenTheSharedReadCannotResolveTheRecord()
    {
        var service = new ControloApproveService(
            new FakeControloCreateService(),
            new FakePesoRepository(),
            new FakePesoReviewRepository(),
            FixedCurrentAccount.User());

        var result = await service.GetReviewSheetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ReviewResult.NotFound>(result);
    }
}