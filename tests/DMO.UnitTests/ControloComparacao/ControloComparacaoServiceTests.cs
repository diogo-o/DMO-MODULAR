using DMO.Application.Controlo.Comparacao;
using DMO.Application.Controlo.Pesos;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Domain.Controlo;
using DMO.Domain.ControloComparacao;
using DMO.Domain.Tools;

namespace DMO.UnitTests.ControloComparacao;

/// <summary>
/// Service-level proofs of the Peso Comparação workflow over local fakes with fixed calculation
/// configuration: the optional-child behaviour (zero Comparação → zero effect), the natural
/// <c>comparacao_id + cm_id</c> identity, the separated NEW measurements computed by the SHARED
/// Peso calculation path, the individual FINAL per-CM decisions, the put-aside justification rule
/// and the completion rule (confirmation refused while a measured CM lacks its decision).
/// </summary>
public sealed class ControloComparacaoServiceTests
{
    private static readonly Guid CmToolId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CmOneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CmTwoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UnknownCmId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset FrozenAt = DateTimeOffset.UtcNow.AddDays(-1);

    /// <summary>The initial Peso fixture: pendente, one row, frozen glass density 2.50 at 25 °C.
    /// A single readonly instance so equality proofs compare the SAME expected facts.</summary>
    private static readonly Peso InitialPeso =
        new(
            PesoId.From(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            CmId: CmOneId,
            ToolId: null,
            PesoStatus.Pendente,
            SubmittedAt: null,
            SubmittedByUserId: null,
            WaterTemperature: 25m,
            VolumeMarisaBq: null,
            VolumePuncaoPu: null,
            GlassDensityGCm3: 2.50m,
            PreviousProductionEndReference: null,
            PreviousAverageWeightReference: null,
            Version: 3,
            CreatedByUserId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedAt: FrozenAt,
            UpdatedAt: FrozenAt,
            Rows:
            [
                new PesoMeasurementRow(
                    PesoMeasurementRowId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1")),
                    PesoId.From(Guid.Parse("55555555-5555-5555-5555-555555555555")),
                    1,
                    997.1m,
                    1000m,
                    2500m,
                    FrozenAt),
                new PesoMeasurementRow(
                    PesoMeasurementRowId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2")),
                    PesoId.From(Guid.Parse("55555555-5555-5555-5555-555555555555")),
                    2,
                    1994.2m,
                    2000m,
                    5000m,
                    FrozenAt),
            ]);

    // -----------------------------------------------------------------------------------------
    // Zero Comparação ⇒ zero effect (AC1, AC2, AC10)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task ZeroComparison_AbsenceIsExplicitAndThePesoIsNeverWritten()
    {
        var pesos = new FakePesoRepository(InitialPeso);
        var (service, _) = BuildService(pesos);

        // The Peso read itself is untouched by the Comparação contract.
        var absence = await service.GetByPesoIdAsync(InitialPeso.PesoId.Value, CancellationToken.None);
        Assert.IsType<ComparacaoResult.NoComparison>(absence);

        // No Comparação operation ever writes the initial Peso (no create/update/submit/associate).
        Assert.Equal(0, pesos.StoredWrites);
        Assert.Equal(InitialPeso, await pesos.GetByIdAsync(InitialPeso.PesoId.Value, CancellationToken.None));
    }

    [Fact]
    public async Task StartingComparacao_DoesNotChangeInitialPesoValuesStatusOrApproval()
    {
        var pesos = new FakePesoRepository(InitialPeso);
        var (service, comparacoes) = BuildService(pesos);

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;

        // The initial Peso facts are byte-identical after the comparison was started.
        Assert.Equal(0, pesos.StoredWrites);
        Assert.Equal(InitialPeso, await pesos.GetByIdAsync(InitialPeso.PesoId.Value, CancellationToken.None));
        Assert.Equal(InitialPeso.PesoId.Value, read.PesoId);
        Assert.Equal(3, InitialPeso.Version);

        // The approval lifecycle is untouched: the comparison surface knows no status transition.
        Assert.DoesNotContain(comparacoes.Store.Values, comparacao => comparacao.ConfirmedAt is not null);
    }

    [Fact]
    public async Task Starting_EveryEventAllocatesANewComparacaoId()
    {
        var pesos = new FakePesoRepository(InitialPeso);
        var (service, _) = BuildService(pesos);

        var first = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        var second = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        // A new comparison event always allocates a NEW comparacao_id (no single-header rule).
        Assert.NotEqual(first.ComparacaoId, second.ComparacaoId);
        Assert.Equal(1, first.Version);
        Assert.Equal(1, second.Version);
    }

    [Fact]
    public async Task Start_RefusesAnUnknownPeso()
    {
        var pesos = new FakePesoRepository(peso: null);
        var (service, _) = BuildService(pesos);

        var result = await service.StartAsync(
            new StartComparacaoCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.IsType<ComparacaoResult.PesoNotFound>(result);
    }

    // -----------------------------------------------------------------------------------------
    // Subject selection (AC3, AC4, identity)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task OneComparacao_CanContainExactlyOneCm()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        var added = Assert.IsType<ComparacaoResult.SubjectAdded>(await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None));
        Assert.Equal(CmOneId, added.CmId);
        Assert.Equal(started.Version + 1, added.Version);

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;

        var subject = Assert.Single(read.Subjects);
        Assert.Equal(CmOneId, subject.CmId);
        Assert.Equal(1, subject.Version);
        Assert.False(subject.HasMeasurements);
        Assert.False(subject.IsDecided);
    }

    [Fact]
    public async Task OneComparacao_CanContainMultipleSelectedCms()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmTwoId, Guid.NewGuid()),
            CancellationToken.None);

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;

        Assert.Equal(2, read.Subjects.Count);
        Assert.Equal(
            new[] { CmOneId, CmTwoId },
            read.Subjects.Select(subject => subject.CmId).OrderBy(cmId => cmId).ToArray());
    }

    [Fact]
    public async Task AddSubject_TheIdentityIsTheExactCmId_AndTheContextComesFromTheCmOnly()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;

        var subject = Assert.Single(read.Subjects);

        // The subject stores ONLY the canonical cm_id; every production-context fact (frozen
        // tool type/reference/lot, job_on, tool_id) is resolved through the SAME cm_id — never
        // stored on the comparison and never inferred from text.
        Assert.Equal(CmOneId, subject.CmId);
        Assert.NotNull(subject.Context);
        Assert.Equal(CmOneId, subject.Context!.CmId);
        Assert.Equal(CmToolId, subject.Context.ToolId);
        Assert.Equal("CM", subject.Context.FrozenToolType);
        Assert.Equal("5447T173", subject.Context.FrozenToolReference);
        Assert.Equal("12", subject.Context.FrozenToolLot);
    }

    [Fact]
    public async Task AddSubject_DuplicateCmInTheSameEvent_IsRefused()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        var duplicate = await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        var failed = Assert.IsType<ComparacaoResult.ValidationFailed>(duplicate);
        Assert.Equal([ComparacaoValidationErrors.CmAlreadySelected], failed.Errors);
    }

    [Fact]
    public async Task AddSubject_UnknownCm_IsRefused()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        var result = await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, UnknownCmId, Guid.NewGuid()),
            CancellationToken.None);

        var failed = Assert.IsType<ComparacaoResult.ValidationFailed>(result);
        Assert.Equal([ComparacaoValidationErrors.CmContextNotFound], failed.Errors);
    }

    // -----------------------------------------------------------------------------------------
    // Calculation reuse + separated measurements (AC5, AC6)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task RecordMeasurements_ReusesTheSharedCalculationPath_AndStoresSeparateRows()
    {
        var pesos = new FakePesoRepository(InitialPeso);
        var (service, comparacoes) = BuildService(pesos);

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        // The exact MES3 fixture: 997.1 g ÷ 0.9971 → 1000 cm³; × 2.50 → 2500 g. The comparison
        // uses the SHARED Peso row calculation path over the INITIAL Peso's frozen facts
        // (temperature 25 °C → water density 0.9971; frozen glass density 2.50).
        var recorded = Assert.IsType<ComparacaoResult.MeasurementsRecorded>(await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m, 1994.2m]),
            CancellationToken.None));
        Assert.Equal(2, recorded.SubjectVersion);

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;
        var subject = Assert.Single(read.Subjects);

        Assert.Equal(2, subject.Rows.Count);
        Assert.Equal(new ComparacaoMeasurementRowReadModel(1, 997.1m, 1000m, 2500m), subject.Rows[0]);
        Assert.Equal(new ComparacaoMeasurementRowReadModel(2, 1994.2m, 2000m, 5000m), subject.Rows[1]);
        Assert.True(subject.HasMeasurements);

        // The comparison rows live in the comparison repository only; the INITIAL Peso rows and
        // its average are untouched (the fake Peso store never received a write).
        Assert.Equal(0, pesos.StoredWrites);
        Assert.Equal(InitialPeso.Rows, (await pesos.GetByIdAsync(InitialPeso.PesoId.Value, CancellationToken.None))!.Rows);
        Assert.Equal(comparacoes.Store.Values.Single().Subjects.Single().Rows.Count, subject.Rows.Count);
    }

    [Fact]
    public async Task RecordMeasurements_FourDecimalPrecisionIsRetained()
    {
        var pesos = new FakePesoRepository(InitialPeso with { WaterTemperature = 25m });
        var (service, _) = BuildService(pesos, new Dictionary<decimal, decimal> { [25m] = 1m });

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [1.23456m]),
            CancellationToken.None);

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;
        var row = Assert.Single(Assert.Single(read.Subjects).Rows);

        // 1.23456 g ÷ water density 1 → capacity rounded to 4 dp (1.2346); glass = capacity × the
        // Peso's FROZEN density 2.50 → 3.0865. The 4-dp retention is the point: never the 2-dp
        // render form.
        Assert.Equal(1.2346m, row.CapacityCm3);
        Assert.Equal(3.0865m, row.GlassWeightG);
        Assert.NotEqual(decimal.Round(row.CapacityCm3, 2), row.CapacityCm3);
        Assert.Equal(row.CapacityCm3, decimal.Round(row.CapacityCm3, 4));
    }

    [Fact]
    public async Task RecordMeasurements_RefusedWhenTheFrozenFactsAreMissing()
    {
        var pesos = new FakePesoRepository(InitialPeso with { GlassDensityGCm3 = null });
        var (service, _) = BuildService(pesos);

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        var result = await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);

        var refused = Assert.IsType<ComparacaoResult.Refused>(result);
        Assert.Equal(ComparacaoRefusalReason.CalculationConfigurationMissing, refused.Reason);
    }

    // -----------------------------------------------------------------------------------------
    // Individual FINAL decisions (AC7, AC9)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Decisions_AreIndividualPerComparedCm()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmTwoId, Guid.NewGuid()),
            CancellationToken.None);

        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmTwoId, 1, [997.1m]),
            CancellationToken.None);

        // CM one → manter (no reason required); CM two → colocar de parte (with justification).
        var manter = Assert.IsType<ComparacaoResult.Decided>(await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 2, "manter", null, Guid.NewGuid()),
            CancellationToken.None));
        var colocar = Assert.IsType<ComparacaoResult.Decided>(await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmTwoId, 2, "colocar_de_parte", "Fuga detetada", Guid.NewGuid()),
            CancellationToken.None));

        Assert.Equal("manter", manter.Decision);
        Assert.Equal("colocar_de_parte", colocar.Decision);

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;
        var first = Assert.Single(read.Subjects, subject => subject.CmId == CmOneId);
        var second = Assert.Single(read.Subjects, subject => subject.CmId == CmTwoId);

        // The decisions are independent: different values, individual actor/time.
        Assert.True(first.IsDecided);
        Assert.Equal("manter", first.Decision);
        Assert.Null(first.Reason);
        Assert.NotNull(first.DecidedByUserId);
        Assert.NotNull(first.DecidedAt);

        Assert.True(second.IsDecided);
        Assert.Equal("colocar_de_parte", second.Decision);
        Assert.Equal("Fuga detetada", second.Reason);
        Assert.NotNull(second.DecidedByUserId);
        Assert.NotNull(second.DecidedAt);
        Assert.NotEqual(first.DecidedByUserId, second.DecidedByUserId);
    }

    [Fact]
    public async Task Decide_ManterRequiresNoFabricatedReason()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);

        var decided = Assert.IsType<ComparacaoResult.Decided>(await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 2, "manter", null, Guid.NewGuid()),
            CancellationToken.None));

        var subject = Assert.Single(
            Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
                started.ComparacaoId, CancellationToken.None)).Value.Subjects);
        Assert.Equal("manter", subject.Decision);
        Assert.Null(subject.Reason);
        Assert.Equal(3, decided.SubjectVersion);
    }

    [Fact]
    public async Task Decide_PutAsideWithoutJustification_IsRefusedBeforeAnyWrite()
    {
        var (service, comparacoes) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);

        var result = await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 2, "colocar_de_parte", null, Guid.NewGuid()),
            CancellationToken.None);

        var failed = Assert.IsType<ComparacaoResult.ValidationFailed>(result);
        Assert.Equal([ComparacaoValidationErrors.PutAsideReasonRequired], failed.Errors);
        Assert.False(comparacoes.Store.Values.Single().Subjects.Single().IsDecided);
    }

    [Fact]
    public async Task Decide_AnUnmeasuredSubject_CannotBeDecided()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        var result = await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 1, "manter", null, Guid.NewGuid()),
            CancellationToken.None);

        var refused = Assert.IsType<ComparacaoResult.Refused>(result);
        Assert.Equal(ComparacaoRefusalReason.SubjectNotMeasured, refused.Reason);
    }

    [Fact]
    public async Task Decide_ASecondDecisionOfTheSameComparedCm_IsRefused()
    {
        var (service, comparacoes) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);
        await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 2, "manter", null, Guid.NewGuid()),
            CancellationToken.None);

        // A second decision (and a re-measurement) are refused: the decision is FINAL inside one
        // comparison event — the prior decision is never overwritten, silently or otherwise.
        var second = await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 3, "colocar_de_parte", "mudou", Guid.NewGuid()),
            CancellationToken.None);
        var refused = Assert.IsType<ComparacaoResult.Refused>(second);
        Assert.Equal(ComparacaoRefusalReason.AlreadyDecided, refused.Reason);

        var reMeasure = await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 3, [1994.2m]),
            CancellationToken.None);
        Assert.Equal(ComparacaoRefusalReason.AlreadyDecided, Assert.IsType<ComparacaoResult.Refused>(reMeasure).Reason);

        var subject = Assert.Single(comparacoes.Store.Values.Single().Subjects);
        Assert.Equal(ComparacaoCmDecisionKind.Manter, subject.Decision);
        Assert.Equal(3, subject.Version); // 1 (selected) + 1 (recorded) + 1 (first decision)
        Assert.Equal(1, subject.Rows.Count);
    }

    // -----------------------------------------------------------------------------------------
    // Confirmation / completion rule (AC8)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Confirm_RefusedWhileAMeasuredCmLacksItsDecision()
    {
        var (service, comparacoes) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmTwoId, Guid.NewGuid()),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmTwoId, 1, [997.1m]),
            CancellationToken.None);

        // Only one of the two measured CMs is decided: the comparison is NOT functionally
        // complete and confirmation is refused.
        await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 2, "manter", null, Guid.NewGuid()),
            CancellationToken.None);

        var refused = await service.ConfirmAsync(
            new ConfirmComparacaoCommand(started.ComparacaoId, 2, Guid.NewGuid()),
            CancellationToken.None);

        var refusal = Assert.IsType<ComparacaoResult.Refused>(refused);
        Assert.Equal(ComparacaoRefusalReason.MeasurementsWithoutDecision, refusal.Reason);
        Assert.Null(comparacoes.Store.Values.Single().ConfirmedAt);
    }

    [Fact]
    public async Task Confirm_RequiresEveryMeasuredCmDecided_ThenConfirms()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);
        await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 2, "manter", null, Guid.NewGuid()),
            CancellationToken.None);

        var confirmed = Assert.IsType<ComparacaoResult.Confirmed>(await service.ConfirmAsync(
            new ConfirmComparacaoCommand(started.ComparacaoId, 2, Guid.NewGuid()),
            CancellationToken.None));
        Assert.Equal(3, confirmed.Version);
        Assert.NotNull(confirmed.ConfirmedAt);

        var read = Assert.IsType<ComparacaoResult.Found>(await service.GetAsync(
            started.ComparacaoId, CancellationToken.None)).Value;
        Assert.True(read.IsFunctionallyComplete);
        Assert.NotNull(read.ConfirmedAt);
        Assert.NotNull(read.ConfirmedByUserId);

        // The event still anchors the SAME initial Peso — the confirmation never re-anchors.
        Assert.Equal(InitialPeso.PesoId.Value, read.PesoId);
    }

    [Fact]
    public async Task Confirm_AnEventWithoutAnySelectedCm_IsRefused()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));

        var refused = await service.ConfirmAsync(
            new ConfirmComparacaoCommand(started.ComparacaoId, 1, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(
            ComparacaoRefusalReason.NoCmSubjects,
            Assert.IsType<ComparacaoResult.Refused>(refused).Reason);
    }

    [Fact]
    public async Task Mutations_AfterConfirmation_AreRefused()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);
        await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None);
        await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 2, "manter", null, Guid.NewGuid()),
            CancellationToken.None);
        await service.ConfirmAsync(
            new ConfirmComparacaoCommand(started.ComparacaoId, 2, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(
            ComparacaoRefusalReason.AlreadyConfirmed,
            Assert.IsType<ComparacaoResult.Refused>(await service.AddSubjectAsync(
                new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmTwoId, Guid.NewGuid()),
                CancellationToken.None)).Reason);

        Assert.Equal(
            ComparacaoRefusalReason.AlreadyConfirmed,
            Assert.IsType<ComparacaoResult.Refused>(await service.RecordMeasurementsAsync(
                new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmTwoId, 1, [997.1m]),
                CancellationToken.None)).Reason);

        Assert.Equal(
            ComparacaoRefusalReason.AlreadyConfirmed,
            Assert.IsType<ComparacaoResult.Refused>(await service.DecideAsync(
                new DecideComparacaoCmCommand(started.ComparacaoId, CmTwoId, 1, "manter", null, Guid.NewGuid()),
                CancellationToken.None)).Reason);

        Assert.Equal(
            ComparacaoRefusalReason.AlreadyConfirmed,
            Assert.IsType<ComparacaoResult.Refused>(await service.ConfirmAsync(
                new ConfirmComparacaoCommand(started.ComparacaoId, 3, Guid.NewGuid()),
                CancellationToken.None)).Reason);
    }

    [Fact]
    public async Task StaleSubjectVersions_AreRefused()
    {
        var (service, _) = BuildService(new FakePesoRepository(InitialPeso));

        var started = Assert.IsType<ComparacaoResult.Started>(await service.StartAsync(
            new StartComparacaoCommand(InitialPeso.PesoId.Value, Guid.NewGuid()),
            CancellationToken.None));
        await service.AddSubjectAsync(
            new AddComparacaoCmSubjectCommand(started.ComparacaoId, CmOneId, Guid.NewGuid()),
            CancellationToken.None);

        // The subject was observed at version 1; recording bumps it to 2.
        Assert.IsType<ComparacaoResult.MeasurementsRecorded>(await service.RecordMeasurementsAsync(
            new RecordComparacaoMeasurementsCommand(started.ComparacaoId, CmOneId, 1, [997.1m]),
            CancellationToken.None));

        // Deciding with the STALE observed version (1 instead of 2) is refused.
        var stale = await service.DecideAsync(
            new DecideComparacaoCmCommand(started.ComparacaoId, CmOneId, 1, "manter", null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(
            ComparacaoRefusalReason.StaleVersion,
            Assert.IsType<ComparacaoResult.Refused>(stale).Reason);
    }

    // -----------------------------------------------------------------------------------------
    // Fakes
    // -----------------------------------------------------------------------------------------

    private static (ControloComparacaoService Service, FakeComparacaoRepository Comparacoes) BuildService(
        FakePesoRepository pesos,
        IReadOnlyDictionary<decimal, decimal>? waterDensities = null)
    {
        var comparacoes = new FakeComparacaoRepository();

        var service = new ControloComparacaoService(
            pesos,
            new FakePesoContextRead(),
            new FixedCalculationConfiguration(waterDensities ?? new Dictionary<decimal, decimal> { [25m] = 0.9971m }),
            comparacoes);

        return (service, comparacoes);
    }

    /// <summary>A Peso repository that returns the configured Peso and counts every write call
    /// (the zero-effect proof: the Comparação service must never call a write).</summary>
    private sealed class FakePesoRepository : IPesoRepository
    {
        private readonly Peso? _peso;

        public FakePesoRepository(Peso? peso)
        {
            _peso = peso;
        }

        public int StoredWrites { get; private set; }

        public Task<Peso?> GetByIdAsync(Guid pesoId, CancellationToken cancellationToken) =>
            Task.FromResult(_peso);

        public Task<Peso> CreatedAsync(Peso peso, IReadOnlyList<PesoMeasurementRow> rows, CancellationToken cancellationToken)
        {
            StoredWrites++;
            return Task.FromResult(peso);
        }

        public Task<Peso> UpdatedAsync(Peso peso, IReadOnlyList<PesoMeasurementRow> rows, CancellationToken cancellationToken)
        {
            StoredWrites++;
            return Task.FromResult(peso);
        }

        public Task<Peso> SubmittedAsync(Peso peso, CancellationToken cancellationToken)
        {
            StoredWrites++;
            return Task.FromResult(peso);
        }

        public Task<Peso> AssociatedAsync(Peso peso, CancellationToken cancellationToken)
        {
            StoredWrites++;
            return Task.FromResult(peso);
        }
    }

    /// <summary>A context read resolving the two canonical CM identities of the fixture through
    /// the SAME <c>cm_contexts</c> surface the initial Peso anchors to.</summary>
    private sealed class FakePesoContextRead : IPesoContextRead
    {
        public Task<CmContextProjection?> GetCmContextAsync(Guid cmId, CancellationToken cancellationToken) =>
            Task.FromResult<CmContextProjection?>(cmId switch
            {
                _ when cmId == CmOneId => new CmContextProjection(
                    cmId,
                    JobOnId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
                    CmToolId,
                    FrozenToolType: "CM",
                    FrozenToolReference: "5447T173",
                    FrozenToolLot: "12"),
                _ when cmId == CmTwoId => new CmContextProjection(
                    cmId,
                    JobOnId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    CmToolId,
                    FrozenToolType: "CM",
                    FrozenToolReference: "5447T173",
                    FrozenToolLot: "12"),
                _ => null,
            });
    }

    /// <summary>A water-only calculation configuration with fixed water-density mappings (the
    /// glass density comes from the Peso's FROZEN fact).</summary>
    private sealed class FixedCalculationConfiguration : IControloCalculationConfiguration
    {
        private readonly IReadOnlyDictionary<decimal, decimal> _waterDensities;

        public FixedCalculationConfiguration(IReadOnlyDictionary<decimal, decimal> waterDensities)
        {
            _waterDensities = waterDensities;
        }

        public bool TryGetWaterDensity(decimal waterTemperature, out decimal waterDensity)
        {
            var key = (int)Math.Round(waterTemperature, MidpointRounding.AwayFromZero);
            return _waterDensities.TryGetValue(key, out waterDensity);
        }
    }

    /// <summary>
    /// The in-memory Comparação repository: the header/subjects/rows store with the natural keys
    /// and the version guards (the DB CHECKs and composite keys are proven by the integration
    /// tests; the service-level proofs need only the contract semantics).
    /// </summary>
    internal sealed class FakeComparacaoRepository : IComparacaoRepository
    {
        public Dictionary<Guid, Comparacao> Store { get; } = new();

        public Task<Comparacao?> GetByIdAsync(Guid comparacaoId, CancellationToken cancellationToken) =>
            Task.FromResult(Store.GetValueOrDefault(comparacaoId));

        public Task<Comparacao?> GetByPesoIdAsync(Guid pesoId, CancellationToken cancellationToken) =>
            Task.FromResult(Store.Values
                .Where(comparacao => comparacao.PesoId.Value == pesoId)
                .OrderByDescending(comparacao => comparacao.CreatedAt)
                .FirstOrDefault());

        public Task<Comparacao> StartedAsync(Comparacao comparacao, CancellationToken cancellationToken)
        {
            Store[comparacao.ComparacaoId.Value] = comparacao;
            return Task.FromResult(comparacao);
        }

        public Task<Comparacao> SubjectAddedAsync(
            ComparacaoCmSubject subject,
            int expectedComparacaoVersion,
            CancellationToken cancellationToken)
        {
            var current = Store[subject.ComparacaoId.Value];
            AssertVersion(current.Version, expectedComparacaoVersion);

            if (current.Subjects.Any(existing => existing.CmId == subject.CmId))
            {
                throw new ComparacaoPersistenceException(
                    ComparacaoPersistenceFailureReason.DuplicateCmSelected,
                    "The CM is already selected in this comparison event.");
            }

            var updated = current with
            {
                Version = current.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
                Subjects = [.. current.Subjects, subject],
            };
            Store[updated.ComparacaoId.Value] = updated;
            return Task.FromResult(updated);
        }

        public Task<Comparacao> MeasurementsRecordedAsync(
            ComparacaoCmSubject subject,
            int expectedSubjectVersion,
            CancellationToken cancellationToken)
        {
            var current = Store[subject.ComparacaoId.Value];
            var existing = Subject(current, subject.CmId);
            AssertVersion(existing.Version, expectedSubjectVersion);

            var updatedSubject = subject with
            {
                Version = existing.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            var updated = current with
            {
                Subjects = current.Subjects
                    .Select(entry => entry.CmId == subject.CmId ? updatedSubject : entry)
                    .ToList(),
            };
            Store[updated.ComparacaoId.Value] = updated;
            return Task.FromResult(updated);
        }

        public Task<Comparacao> DecidedAsync(
            ComparacaoCmSubject subject,
            int expectedSubjectVersion,
            CancellationToken cancellationToken)
        {
            var current = Store[subject.ComparacaoId.Value];
            var existing = Subject(current, subject.CmId);
            AssertVersion(existing.Version, expectedSubjectVersion);

            var updatedSubject = subject with
            {
                Version = existing.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            var updated = current with
            {
                Subjects = current.Subjects
                    .Select(entry => entry.CmId == subject.CmId ? updatedSubject : entry)
                    .ToList(),
            };
            Store[updated.ComparacaoId.Value] = updated;
            return Task.FromResult(updated);
        }

        public Task<Comparacao> ConfirmedAsync(
            Comparacao comparacao,
            int expectedComparacaoVersion,
            CancellationToken cancellationToken)
        {
            var current = Store[comparacao.ComparacaoId.Value];
            AssertVersion(current.Version, expectedComparacaoVersion);

            var updated = comparacao with
            {
                Version = current.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            Store[updated.ComparacaoId.Value] = updated;
            return Task.FromResult(updated);
        }

        private static ComparacaoCmSubject Subject(Comparacao comparacao, Guid cmId) =>
            comparacao.Subjects.First(subject => subject.CmId == cmId);

        private static void AssertVersion(int current, int expected)
        {
            if (current != expected)
            {
                throw new ConcurrencyConflictException(
                    $"Expected version {expected}, current version {current}.");
            }
        }
    }
}