using System.Reflection;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.Web.Pages.Controlo;

namespace DMO.UnitTests.ControloCreate;

/// <summary>
/// P2-T05 unit proofs of the backend calculation authority: rows MES3, MES4, MES7, MES8, MES9 and
/// MES11 of the test-to-acceptance matrix (<c>plans/contracts/P2-T05_CONTROLO_CREATE_CONTRACT.md</c>
/// §26.4). The formulas §5.3 are computed by <see cref="ControloCreateService"/> over local fakes
/// with fixed calculation configuration; the per-row results are stored with full
/// <c>numeric(18,4)</c> precision and presentation derives averages/deviations only at render time.
/// </summary>
public sealed class PesoCalculationTests
{
    private static readonly BindingFlags DeclaredInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>The fixed values of the Q-CALC configuration used by the formula proofs:
    /// arbitrary test-fixture water density 25 °C → 0.9971 g/cm³ (NOT the authoritative table —
    /// that table is proven in <c>WaterDensityLookupTests</c> against
    /// <c>ConfigurationCalculationConfiguration.AuthoritativeWaterDensityByCelsius</c>).
    /// <para>Glass densities are the CURRENT OPERATIONAL values of the settings store (the
    /// post-closure correction moved glass out of the calculation configuration): these proofs
    /// use the arbitrary fixture values NNPB → 2.50, PS → 2.52 via
    /// <see cref="FixtureGlassDensities"/>.</para></summary>
    private static readonly FixedCalculationConfiguration StandardConfiguration = new(
        new Dictionary<decimal, decimal> { [25m] = 0.9971m });

    /// <summary>The arbitrary fixture glass densities of the formula proofs (store-seeded).</summary>
    private static IReadOnlyDictionary<string, decimal> FixtureGlassDensities { get; } =
        new Dictionary<string, decimal> { ["NNPB"] = 2.50m, ["PS"] = 2.52m };

    /// <summary>
    /// MES3 (AC-M3) — capacity = water weight ÷ resolved water density, computed by the backend:
    /// with the fixed water density 0.9971 g/cm³, 997.1 g → 1000 cm³ and 1994.2 g → 2000 cm³; the
    /// resolved water density, temperature and anchor are echoed in the stateless result, and no
    /// Peso is ever written.
    /// </summary>
    [Fact]
    public async Task MES3_CapacityIsWaterWeightDividedByTheResolvedWaterDensity()
    {
        var toolId = Guid.NewGuid();
        var (service, pesos) = BuildService(StandardConfiguration, toolId);

        var result = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m, 1994.2m]),
            CancellationToken.None);

        var value = Assert.IsType<PesoResult.Calculation>(result).Value;
        Assert.Null(value.CmId);
        Assert.Equal(toolId, value.PendingToolId);
        Assert.Equal(25m, value.WaterTemperature);
        Assert.Equal(0.9971m, value.WaterDensityGCm3);
        Assert.Equal(2.50m, value.GlassDensityGCm3);

        Assert.Equal(2, value.Rows.Count);
        Assert.Equal(new PesoRowCalculation(1, 997.1m, 1000m, 2500m), value.Rows[0]);
        Assert.Equal(new PesoRowCalculation(2, 1994.2m, 2000m, 5000m), value.Rows[1]);

        // Route 8 is stateless: nothing was persisted and no identity was allocated.
        Assert.Equal(0, pesos.StoredCount);
    }

    /// <summary>
    /// MES4 (AC-M4) — glass weight = (capacity + Marisa/BQ − Punção/PU) × density: with Marisa 10,
    /// Punção 5 and density 2.50, (1000 + 10 − 5) × 2.50 = 2512.5.
    /// </summary>
    [Fact]
    public async Task MES4_GlassWeightIsCapacityPlusMarisaMinusPuncaoTimesDensity()
    {
        var toolId = Guid.NewGuid();
        var (service, pesos) = BuildService(StandardConfiguration, toolId);

        var result = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 25m, 10m, 5m, null, null, [997.1m]),
            CancellationToken.None);

        var value = Assert.IsType<PesoResult.Calculation>(result).Value;
        var row = Assert.Single(value.Rows);
        Assert.Equal(1000m, row.CapacityCm3);
        Assert.Equal(2512.5m, row.GlassWeightG);
        Assert.Equal(0, pesos.StoredCount);

        // The same formula is applied to a second row without the volume terms cancelling anything:
        // glass is per row over the row's own capacity.
        var twoRows = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 25m, 10m, 5m, null, null, [997.1m, 1994.2m]),
            CancellationToken.None);
        var twoRowValue = Assert.IsType<PesoResult.Calculation>(twoRows).Value;
        Assert.Equal(new PesoRowCalculation(1, 997.1m, 1000m, 2512.5m), twoRowValue.Rows[0]);
        Assert.Equal(new PesoRowCalculation(2, 1994.2m, 2000m, 5012.5m), twoRowValue.Rows[1]);
    }

    /// <summary>
    /// MES9 (AC-M9, AC-M3) — a missing water-density/glass-density mapping is the typed
    /// <c>calculation-configuration-missing</c> refusal: no value is invented, no silent zero is
    /// produced and the fake repository store stays at zero Pesos.
    /// </summary>
    [Fact]
    public async Task MES9_MissingCalculationConfigurationIsRefusedAndNothingIsWritten()
    {
        var toolId = Guid.NewGuid();
        var (service, pesos) = BuildService(
            new FixedCalculationConfiguration(new Dictionary<decimal, decimal>()),
            toolId,
            EmptyGlassStore());

        var calculated = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m]),
            CancellationToken.None);

        var refusal = Assert.IsType<PesoResult.Refused>(calculated);
        Assert.Equal(PesoRefusalReason.CalculationConfigurationMissing, refusal.Reason);
        Assert.Equal(0, pesos.StoredCount);

        // The create path refuses the same way BEFORE any write: the Peso never reaches the
        // repository when its results cannot be derived.
        var created = await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [997.1m], Guid.NewGuid()),
            CancellationToken.None);

        var createRefusal = Assert.IsType<PesoResult.Refused>(created);
        Assert.Equal(PesoRefusalReason.CalculationConfigurationMissing, createRefusal.Reason);
        Assert.Equal(0, pesos.StoredCount);
    }

    /// <summary>
    /// MES11 (AC-M10, C2) — a computed per-row result that is not strictly positive is refused
    /// BEFORE any write with exactly <c>RESULT_NON_POSITIVE</c>: (a) an invalid water-density
    /// configuration (density 0) can never fabricate a capacity; (b) Punção/PU &gt; capacity +
    /// Marisa/BQ turns the glass weight negative. Nothing reaches the repository in either case.
    /// </summary>
    [Fact]
    public async Task MES11_NonPositiveDerivedResultsAreRefusedBeforeAnyWrite()
    {
        // (a) Invalid water-density configuration: the entered combination yields no derivable capacity.
        var toolId = Guid.NewGuid();
        var (zeroDivisorService, zeroDivisorPesos) = BuildService(
            new FixedCalculationConfiguration(new Dictionary<decimal, decimal> { [25m] = 0m }),
            toolId,
            StoreWith("NNPB", 1m));

        var zeroDivisor = await zeroDivisorService.CreateAsync(
            new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [1000m], Guid.NewGuid()),
            CancellationToken.None);
        var zeroDivisorErrors = Assert.IsType<PesoResult.ValidationFailed>(zeroDivisor).Errors;
        Assert.Equal(new[] { ControloCreateValidationErrors.ResultNonPositive }, zeroDivisorErrors);
        Assert.Equal(0, zeroDivisorPesos.StoredCount);

        // (b) Punção/PU above capacity + Marisa/BQ: capacity 1000, glass (1000 − 5000) × 1 = −4000.
        var (service, pesos) = BuildService(
            new FixedCalculationConfiguration(new Dictionary<decimal, decimal> { [25m] = 1m }),
            toolId,
            StoreWith("NNPB", 1m));

        var calculated = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 25m, 0m, 5000m, null, null, [1000m]),
            CancellationToken.None);
        var calculatedErrors = Assert.IsType<PesoResult.ValidationFailed>(calculated).Errors;
        Assert.Equal(new[] { ControloCreateValidationErrors.ResultNonPositive }, calculatedErrors);

        var created = await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 25m, 0m, 5000m, null, null, [1000m], Guid.NewGuid()),
            CancellationToken.None);
        var createdErrors = Assert.IsType<PesoResult.ValidationFailed>(created).Errors;
        Assert.Equal(new[] { ControloCreateValidationErrors.ResultNonPositive }, createdErrors);
        Assert.Equal(0, pesos.StoredCount);
    }

    /// <summary>
    /// MES7 (AC-M7) — presentation rounding is a render-time rule that never reduces the stored
    /// precision: the model retains full <c>numeric(18,4)</c> precision (an entered 1.23456 g with
    /// water density 1 is stored as capacity 1.2346 — never rounded to 2 dp), and the presentation wrapper
    /// over 3+ dp rows keeps the model values verbatim.
    /// </summary>
    [Fact]
    public async Task MES7_StoredResultsRetainFourDecimalPrecisionAndPresentationNeverNormalizes()
    {
        var toolId = Guid.NewGuid();
        var (service, pesos) = BuildService(
            new FixedCalculationConfiguration(new Dictionary<decimal, decimal> { [25m] = 1m }),
            toolId,
            StoreWith("NNPB", 1m));

        var result = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 25m, null, null, null, null, [1.23456m]),
            CancellationToken.None);

        var row = Assert.Single(Assert.IsType<PesoResult.Calculation>(result).Value.Rows);
        Assert.Equal(1.2346m, row.CapacityCm3);
        Assert.Equal(1.2346m, row.GlassWeightG);

        // 4 dp is kept: the value is NOT the 2 dp render form (1.23) the presentation would show.
        Assert.NotEqual(decimal.Round(row.CapacityCm3, 2), row.CapacityCm3);
        Assert.Equal(1.23m, decimal.Round(row.CapacityCm3, 2));
        Assert.Equal(row.CapacityCm3, decimal.Round(row.CapacityCm3, 4));

        // The presentation wrapper never normalizes: a 5 dp model value persists verbatim through
        // it, in the row and in the derived average.
        var rows = new List<PesoRowReadModel>
        {
            new(Guid.NewGuid(), 1, 10m, 1000.12345m, 2500m),
        };
        var presentation = new PesoResultsPresentation(rows, 2.50m);
        Assert.Equal(1000.12345m, Assert.Single(presentation.Rows).CapacityCm3);
        Assert.Equal(1000.12345m, presentation.AverageCapacityCm3);
        Assert.Equal(0, pesos.StoredCount);
    }

    /// <summary>
    /// MES8 (AC-M8) — averages and deviations are derived at presentation time and never stored:
    /// the presentation averages equal the recomputed averages over the model rows, every individual
    /// row stays present (none hidden), and no Peso read/calculation type declares an average or
    /// deviation member.
    /// </summary>
    [Fact]
    public void MES8_AveragesAndDeviationsAreDerivedAndNeverStored()
    {
        var rows = new List<PesoRowReadModel>
        {
            new(Guid.NewGuid(), 1, 10m, 100m, 250m),
            new(Guid.NewGuid(), 2, 20m, 200m, 500m),
            new(Guid.NewGuid(), 3, 30m, 300m, 750m),
        };

        var presentation = new PesoResultsPresentation(rows, 2.50m);

        // The derived averages equal the recomputed arithmetic means over the presented rows.
        Assert.Equal(20m, presentation.AverageWaterWeightG);
        Assert.Equal(200m, presentation.AverageCapacityCm3);
        Assert.Equal(500m, presentation.AverageGlassWeightG);

        Assert.Equal(20m, rows.Average(row => row.WaterWeightG));
        Assert.Equal(200m, rows.Average(row => row.CapacityCm3));
        Assert.Equal(500m, rows.Average(row => row.GlassWeightG));

        // Deviations are per row, from the average capacity: cm³ and %.
        Assert.Equal(-100m, presentation.DeviationCm3(rows[0]));
        Assert.Equal(0m, presentation.DeviationCm3(rows[1]));
        Assert.Equal(100m, presentation.DeviationCm3(rows[2]));
        Assert.Equal(-50m, presentation.DeviationPercent(rows[0]));
        Assert.Equal(0m, presentation.DeviationPercent(rows[1]));
        Assert.Equal(50m, presentation.DeviationPercent(rows[2]));

        // Presentation over no rows derives nothing and hides nothing: an empty model stays empty.
        var empty = new PesoResultsPresentation([], null);
        Assert.Null(empty.AverageWaterWeightG);
        Assert.Null(empty.AverageCapacityCm3);
        Assert.Null(empty.AverageGlassWeightG);
        Assert.Empty(empty.Rows);
        Assert.Equal(0m, empty.DeviationCm3(new PesoRowReadModel(Guid.NewGuid(), 1, 1m, 1m, 1m)));
        Assert.Null(empty.DeviationPercent(new PesoRowReadModel(Guid.NewGuid(), 1, 1m, 1m, 1m)));

        // Every individual row is present in the presentation: nothing is hidden behind an average.
        Assert.Equal(rows.Count, presentation.Rows.Count);
        Assert.Equal(rows.Select(row => row.PesoMeasurementRowId), presentation.Rows.Select(row => row.PesoMeasurementRowId));
        Assert.Equal(rows.Select(row => row.CapacityCm3), presentation.Rows.Select(row => row.CapacityCm3));

        // No model or domain carrier stores an average or deviation column: those live on the
        // presentation type only, so a stored read can never contain a derived column. The one
        // legitimate "average" noun is the input-fact reference <c>PreviousAverageWeightReference</c>,
        // never a derived average.
        foreach (var type in new[] { typeof(PesoSheetReadModel), typeof(PesoRowReadModel), typeof(PesoCalculation) })
        {
            var propertyNames = type.GetProperties(DeclaredInstance)
                .Select(property => property.Name)
                .ToList();
            Assert.DoesNotContain(
                propertyNames,
                name => name.Contains("Deviation", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                propertyNames,
                name => name.Contains("Average", StringComparison.OrdinalIgnoreCase) &&
                        name != nameof(PesoSheetReadModel.PreviousAverageWeightReference));
        }

        // The presentation type itself is where the derived facts live.
        Assert.NotNull(typeof(PesoResultsPresentation).GetProperty("AverageCapacityCm3", DeclaredInstance));
        Assert.NotNull(typeof(PesoResultsPresentation).GetMethod("DeviationCm3", DeclaredInstance));
    }

    // ---------------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------------

    private static (ControloCreateService Service, FakePesoRepository Pesos) BuildService(
        IControloCalculationConfiguration calculation,
        Guid cmToolId,
        FakeGlassDensitySettingsRepository? glassDensities = null)
    {
        var pesos = new FakePesoRepository();

        var service = new ControloCreateService(
            pesos,
            new NullPesoContextRead(),
            new UnusedJobOnService(),
            new FakeToolService(cmToolId),
            calculation,
            glassDensities ?? new FakeGlassDensitySettingsRepository(FixtureGlassDensities));

        return (service, pesos);
    }

    /// <summary>A glass store with NO row (every resolution fails closed).</summary>
    private static FakeGlassDensitySettingsRepository EmptyGlassStore()
    {
        var store = new FakeGlassDensitySettingsRepository();
        store.Remove("NNPB");
        store.Remove("PS");
        return store;
    }

    /// <summary>A glass store with exactly the supplied current density for one processo.</summary>
    private static FakeGlassDensitySettingsRepository StoreWith(string processo, decimal density)
    {
        var store = new FakeGlassDensitySettingsRepository(new Dictionary<string, decimal> { [processo] = density });
        return store;
    }

    /// <summary>A Peso repository that records how many Pesos were ever written.</summary>
    private sealed class FakePesoRepository : IPesoRepository
    {
        public int StoredCount { get; private set; }

        public Task<Peso?> GetByIdAsync(Guid pesoId, CancellationToken cancellationToken) =>
            Task.FromResult<Peso?>(null);

        public Task<Peso> CreatedAsync(
            Peso peso,
            IReadOnlyList<PesoMeasurementRow> rows,
            CancellationToken cancellationToken)
        {
            StoredCount++;
            return Task.FromResult(peso);
        }

        public Task<Peso> UpdatedAsync(
            Peso peso,
            IReadOnlyList<PesoMeasurementRow> rows,
            CancellationToken cancellationToken)
        {
            StoredCount++;
            return Task.FromResult(peso);
        }

        public Task<Peso> SubmittedAsync(Peso peso, CancellationToken cancellationToken)
        {
            StoredCount++;
            return Task.FromResult(peso);
        }

        public Task<Peso> AssociatedAsync(Peso peso, CancellationToken cancellationToken)
        {
            StoredCount++;
            return Task.FromResult(peso);
        }
    }

    /// <summary>A context read that resolves no production anchor (the proofs anchor on the Tool).</summary>
    private sealed class NullPesoContextRead : IPesoContextRead
    {
        public Task<CmContextProjection?> GetCmContextAsync(Guid cmId, CancellationToken cancellationToken) =>
            Task.FromResult<CmContextProjection?>(null);
    }

    /// <summary>A Tool service answering one existing CM Tool ficha (processo NNPB).</summary>
    private sealed class FakeToolService : IToolService
    {
        private readonly Guid _cmToolId;

        public FakeToolService(Guid cmToolId)
        {
            _cmToolId = cmToolId;
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
                        Processo.Nnpb,
                        10,
                        [MachineCode.From("B1")],
                        []))
                    : new ToolResult.NotFound(toolId));

        public Task<ToolResult> CreateAsync(CreateToolCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<ToolResult>(new ToolResult.Created(Guid.NewGuid()));
    }

    /// <summary>A Job On service that is never invoked by the calculation/create paths under test.</summary>
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

    /// <summary>A WATER-only calculation configuration with fixed water-density mappings (the glass
    /// density lives in the settings store after the post-closure correction).</summary>
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
}