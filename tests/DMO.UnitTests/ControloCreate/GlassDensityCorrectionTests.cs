using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Persistence;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;

namespace DMO.UnitTests.ControloCreate;

/// <summary>
/// Focused U proofs of the P2-T05 post-closure glass-density correction (Owner rule
/// GLASS_DENSITY_CONFIGURATION; correction contract §5.2/§5.4): the glass density used by a Peso
/// is the CURRENT OPERATIONAL value of the anchor's processo from the Definições settings store
/// (<c>glass_density_settings</c>, seeded NNPB 2.4027 / PS 2.4231 g/cm³), frozen on the Peso at
/// first successful calculate/save and NEVER re-resolved — recalculation and submit re-derive
/// with the frozen value, later settings changes affect only NEW Pesos, a missing row fails
/// closed with <c>calculation-configuration-missing</c>, and 4-dp precision is preserved
/// verbatim into the frozen <c>glass_density_g_cm3</c>.
/// </summary>
/// <remarks>
/// The water side stays untouched: <see cref="IControloCalculationConfiguration"/> resolves the
/// water density exactly as before (fixed test fixture values; the authoritative water table is
/// proven in <c>WaterDensityLookupTests</c>), and the glass lookup is never fed by the water
/// temperature or vice versa.</remarks>
public sealed class GlassDensityCorrectionTests
{
    // -----------------------------------------------------------------------------------------
    // Resolution — the current operational value per processo (R3)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// GD-U1 — a new Peso calculation resolves the CURRENT operational density of the anchor's
    /// processo from the settings store: NNPB → 2.4027 and PS → 2.4231 (the provenance-backed
    /// bootstrap), frozen into the calculation result with full precision.
    /// </summary>
    [Theory]
    [InlineData(Processo.Nnpb, 2.4027)]
    [InlineData(Processo.Ps, 2.4231)]
    public async Task GD_U1_ResolutionReturnsTheCurrentOperationalDensityPerProcesso(Processo processo, double expected)
    {
        var store = new FakeGlassDensitySettingsRepository();

        var (toolId, service, _) = Build(store, processo);

        var result = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 20m, null, null, null, null, [1000m]),
            CancellationToken.None);

        var value = Assert.IsType<PesoResult.Calculation>(result).Value;
        Assert.Equal((decimal)expected, value.GlassDensityGCm3);
    }

    /// <summary>
    /// GD-U2 — the resolution reflects a LATER settings change immediately: after the Definições
    /// value of NNPB changes (2.4027 → 2.50), a NEW NNPB calculation uses the new current value
    /// while PS keeps its own (per-processo independence at the resolution point).
    /// </summary>
    [Fact]
    public async Task GD_U2_ANewCalculationUsesTheCurrentValueAndProcessosStayIndependent()
    {
        var store = new FakeGlassDensitySettingsRepository();
        store.SetCurrent("NNPB", 2.50m);

        var (nnpbToolId, npbService, _) = Build(store, Processo.Nnpb);
        var (psToolId, psService, _) = Build(store, Processo.Ps);

        var npb = Assert.IsType<PesoResult.Calculation>(await npbService.CalculateAsync(
            new CalculatePesoCommand(null, nnpbToolId, 20m, null, null, null, null, [1000m]),
            CancellationToken.None)).Value;
        var ps = Assert.IsType<PesoResult.Calculation>(await psService.CalculateAsync(
            new CalculatePesoCommand(null, psToolId, 20m, null, null, null, null, [1000m]),
            CancellationToken.None)).Value;

        Assert.Equal(2.50m, npb.GlassDensityGCm3); // the NEW current NNPB value
        Assert.Equal(2.4231m, ps.GlassDensityGCm3); // PS untouched
    }

    /// <summary>
    /// GD-U3 — an absent settings row (defensive only — both rows are always seeded) fails
    /// closed with <c>calculation-configuration-missing</c> on calculate AND create: nothing is
    /// invented, no silent zero, nothing written.
    /// </summary>
    [Fact]
    public async Task GD_U3_AnAbsentSettingsRowFailsClosedAndNothingIsWritten()
    {
        var store = new FakeGlassDensitySettingsRepository();
        store.Remove("NNPB");

        var (toolId, service, pesos) = Build(store, Processo.Nnpb);

        var calculated = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 20m, null, null, null, null, [1000m]),
            CancellationToken.None);
        Assert.Equal(PesoRefusalReason.CalculationConfigurationMissing, Assert.IsType<PesoResult.Refused>(calculated).Reason);

        var created = await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 20m, null, null, null, null, [1000m], Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(PesoRefusalReason.CalculationConfigurationMissing, Assert.IsType<PesoResult.Refused>(created).Reason);

        Assert.Equal(0, pesos.StoredCount);
    }

    // -----------------------------------------------------------------------------------------
    // Freeze + historical immutability (R4/R5/R6)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// GD-U4 — the resolved value is FROZEN on the Peso at first successful save
    /// (<c>glass_density_g_cm3</c>); a later settings change does NOT change it: recalculation of
    /// the same Peso (edit) re-derives with the FROZEN density, and submit recompute-verifies
    /// with the frozen density (a current-settings read would fail the verification).
    /// </summary>
    [Fact]
    public async Task GD_U4_ExistingPesoKeepsItsFrozenDensityThroughSettingsChanges()
    {
        var store = new FakeGlassDensitySettingsRepository();
        var (toolId, service, pesos) = Build(store, Processo.Nnpb);

        // Save the Peso while NNPB = 2.4027.
        var created = Assert.IsType<PesoResult.Created>(await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 20m, null, null, null, null, [997.1m], Guid.NewGuid()),
            CancellationToken.None));
        var pesoId = created.PesoId;
        Assert.Equal(1, created.Version);

        var frozen = pesos.Store[pesoId].GlassDensityGCm3;
        Assert.Equal(2.4027m, frozen);

        // The settings change afterwards: NNPB → 3.00 (affects ONLY new Pesos).
        store.SetCurrent("NNPB", 3.00m);

        // Edit (recalculation) of the SAME Peso: keeps the frozen density, never re-resolves.
        var updated = Assert.IsType<PesoResult.Updated>(await service.UpdateAsync(
            new UpdatePesoCommand(
                pesoId,
                ExpectedVersion: 1,
                WaterTemperature: 20m,
                VolumeMarisaBq: null,
                VolumePuncaoPu: null,
                PreviousProductionEndReference: null,
                PreviousAverageWeightReference: null,
                [2000m]),
            CancellationToken.None));
        Assert.Equal(2, updated.Version);
        Assert.Equal(2.4027m, pesos.Store[pesoId].GlassDensityGCm3);
        // The re-derived row used the frozen density: glass = capacity × 2.4027.
        Assert.Equal(decimal.Round(2000m / 1m, 4) * 2.4027m, Assert.Single(pesos.Store[pesoId].Rows).GlassWeightG);

        // Submit recompute-verify: succeeds ONLY because it re-derives with the FROZEN density
        // (re-deriving with the current 3.00 would mismatch the stored rows).
        var submitted = Assert.IsType<PesoResult.Submitted>(await service.SubmitAsync(
            new SubmitPesoCommand(pesoId, ExpectedVersion: 2, Guid.NewGuid()),
            CancellationToken.None));
        Assert.NotEqual(default, submitted.SubmittedAt);

        // A NEW Peso after the change uses the NEW current value.
        var laterCreated = Assert.IsType<PesoResult.Created>(await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 20m, null, null, null, null, [997.1m], Guid.NewGuid()),
            CancellationToken.None));
        Assert.Equal(3.00m, pesos.Store[laterCreated.PesoId].GlassDensityGCm3);
    }

    /// <summary>
    /// GD-U5 — the frozen density keeps its four decimal places verbatim (2.4027 never drifts);
    /// an update through the settings store preserves the exact <c>numeric(18,4)</c> value.
    /// </summary>
    [Fact]
    public async Task GD_U5_FourDecimalPlacesArePreservedExactly()
    {
        var store = new FakeGlassDensitySettingsRepository();

        // The bootstrap values are exact 4-dp decimals; the resolution echoes them verbatim.
        Assert.Equal(2.4027m, store.Current("NNPB"));
        Assert.Equal(2.4231m, store.Current("PS"));

        // A 4-dp update round-trips exactly through the store's guarded write.
        var updated = await store.UpdatedAsync(
            (await store.GetByProcessoAsync("NNPB", CancellationToken.None))! with { DensityGCm3 = 2.4099m },
            CancellationToken.None);
        Assert.Equal(2.4099m, updated.DensityGCm3);
        Assert.Equal(2, updated.Version);

        // A Peso created with an updated 4-dp value freezes it verbatim (no rounding drift).
        var (toolId, service, pesos) = Build(store, Processo.Nnpb);
        var created = Assert.IsType<PesoResult.Created>(await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 20m, null, null, null, null, [1000m], Guid.NewGuid()),
            CancellationToken.None));
        Assert.Equal(2.4099m, pesos.Store[created.PesoId].GlassDensityGCm3);
    }

    // -----------------------------------------------------------------------------------------
    // Water behavior stays unchanged (R8)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// GD-U6 — the water side is untouched by the correction: the capacity still derives from the
    /// water density resolved by <see cref="IControloCalculationConfiguration"/> (fixture 1.0
    /// here; the authoritative table is proven separately), and the glass density never feeds the
    /// water lookup — the two facts remain fully independent.
    /// </summary>
    [Fact]
    public async Task GD_U6_WaterDensityBehaviorRemainsUnchanged()
    {
        var store = new FakeGlassDensitySettingsRepository();
        var (toolId, service, _) = Build(store, Processo.Nnpb);

        var calculated = Assert.IsType<PesoResult.Calculation>(await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 20m, null, null, null, null, [997.1m]),
            CancellationToken.None)).Value;

        Assert.Equal(1m, calculated.WaterDensityGCm3); // the fixed water fixture, unchanged
        Assert.Equal(997.1m, Assert.Single(calculated.Rows).CapacityCm3); // 997.1 ÷ 1
        Assert.Equal(decimal.Round(997.1m * 2.4027m, 4), Assert.Single(calculated.Rows).GlassWeightG);
    }

    // -----------------------------------------------------------------------------------------
    // Fakes
    // -----------------------------------------------------------------------------------------

    private static (Guid ToolId, ControloCreateService Service, StoringPesoRepository Pesos) Build(
        FakeGlassDensitySettingsRepository store,
        Processo processo)
    {
        var toolId = Guid.NewGuid();
        var pesos = new StoringPesoRepository();

        var service = new ControloCreateService(
            pesos,
            new NullPesoContextRead(),
            new UnusedJobOnService(),
            new FakeToolService(toolId, processo),
            new FixedWaterConfiguration(),
            store);

        return (toolId, service, pesos);
    }

    /// <summary>A Peso repository that persists created/updated/submitted Pesos with their rows.</summary>
    internal sealed class StoringPesoRepository : IPesoRepository
    {
        public Dictionary<Guid, Peso> Store { get; } = [];

        public int StoredCount => Store.Count;

        public Task<Peso?> GetByIdAsync(Guid pesoId, CancellationToken cancellationToken) =>
            Task.FromResult(Store.TryGetValue(pesoId, out var peso) ? peso : null);

        public Task<Peso> CreatedAsync(
            Peso peso,
            IReadOnlyList<PesoMeasurementRow> rows,
            CancellationToken cancellationToken)
        {
            Store[peso.PesoId.Value] = peso with { Rows = rows };
            return Task.FromResult(Store[peso.PesoId.Value]);
        }

        public Task<Peso> UpdatedAsync(
            Peso peso,
            IReadOnlyList<PesoMeasurementRow> rows,
            CancellationToken cancellationToken)
        {
            if (!Store.TryGetValue(peso.PesoId.Value, out var persisted))
            {
                throw new ConcurrencyConflictException("The Peso no longer exists.");
            }

            if (persisted.Version != peso.Version)
            {
                throw new ConcurrencyConflictException("The Peso was modified concurrently.");
            }

            var stored = peso with { Version = persisted.Version + 1, Rows = rows };
            Store[stored.PesoId.Value] = stored;
            return Task.FromResult(stored);
        }

        public Task<Peso> SubmittedAsync(Peso peso, CancellationToken cancellationToken)
        {
            if (!Store.TryGetValue(peso.PesoId.Value, out var persisted))
            {
                throw new ConcurrencyConflictException("The Peso no longer exists.");
            }

            if (persisted.Version != peso.Version)
            {
                throw new ConcurrencyConflictException("The Peso was modified concurrently.");
            }

            var stored = peso with { Version = persisted.Version + 1 };
            Store[stored.PesoId.Value] = stored;
            return Task.FromResult(stored);
        }

        public Task<Peso> AssociatedAsync(Peso peso, CancellationToken cancellationToken)
        {
            if (!Store.TryGetValue(peso.PesoId.Value, out var persisted))
            {
                throw new ConcurrencyConflictException("The Peso no longer exists.");
            }

            var stored = peso with { Version = persisted.Version + 1 };
            Store[stored.PesoId.Value] = stored;
            return Task.FromResult(stored);
        }
    }

    /// <summary>A context read that resolves no production anchor (the proofs anchor on the Tool).</summary>
    private sealed class NullPesoContextRead : IPesoContextRead
    {
        public Task<CmContextProjection?> GetCmContextAsync(Guid cmId, CancellationToken cancellationToken) =>
            Task.FromResult<CmContextProjection?>(null);
    }

    /// <summary>A Tool service answering one existing CM Tool ficha with the supplied processo.</summary>
    private sealed class FakeToolService : IToolService
    {
        private readonly Guid _cmToolId;
        private readonly Processo _processo;

        public FakeToolService(Guid cmToolId, Processo processo)
        {
            _cmToolId = cmToolId;
            _processo = processo;
        }

        public Task<ToolResult> SearchAsync(ToolSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<ToolResult>(new ToolResult.SearchResults([]));

        public Task<ToolResult> GetAsync(Guid toolId, CancellationToken cancellationToken) =>
            Task.FromResult<ToolResult>(
                toolId == _cmToolId
                    ? new ToolResult.Found(new ToolFicha(
                        toolId,
                        ToolType.Cm,
                        "5447T173",
                        "12",
                        _processo,
                        10,
                        [MachineCode.From("B1")],
                        []))
                    : new ToolResult.NotFound(toolId));

        public Task<ToolResult> CreateAsync(CreateToolCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<ToolResult>(new ToolResult.Created(Guid.NewGuid()));
    }

    /// <summary>A fixed water-only calculation configuration (1.0 g/cm³ — arbitrary fixture).</summary>
    private sealed class FixedWaterConfiguration : IControloCalculationConfiguration
    {
        public bool TryGetWaterDensity(decimal waterTemperature, out decimal waterDensity)
        {
            waterDensity = 1m;
            return true;
        }
    }

    /// <summary>A Job On service that is never invoked by the paths under test.</summary>
    private sealed class UnusedJobOnService : IJobOnService
    {
        public Task<JobOnResult> FindProductionsAsync(FindProductionsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.ProductionsFound([]));

        public Task<JobOnResult> GetAsync(Guid jobOnId, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.NotFound(jobOnId));

        public Task<JobOnResult> CreateAsync(CreateJobOnCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.ValidationFailed([]));

        public Task<JobOnResult> UpdateAsync(UpdateJobOnCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.ValidationFailed([]));

        public Task<JobOnResult> PreviewDuplicateAsync(Guid sourceJobOnId, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.NotFound(sourceJobOnId));

        public Task<JobOnResult> DuplicateAsync(DuplicateJobOnCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.ValidationFailed([]));

        public Task<JobOnResult> DeleteAsync(DeleteJobOnCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.ValidationFailed([]));

        public Task<JobOnResult> ListPesoAssociationCandidatesAsync(Guid toolId, CancellationToken cancellationToken) =>
            Task.FromResult<JobOnResult>(new JobOnResult.AssociationCandidates([]));
    }
}