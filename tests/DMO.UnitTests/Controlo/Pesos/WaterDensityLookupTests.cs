using System.Reflection;
using DMO.Application.Controlo.Pesos;
using DMO.Application.JobOn;
using DMO.Application.Repositories;
using DMO.Application.Tools;
using DMO.Domain.Controlo;
using DMO.Domain.Tools;
using DMO.Infrastructure.Configuration;
using DMO.UnitTests.Controlo.Settings;
using DMO.Web.Endpoints.Controlo;
using Microsoft.Extensions.Configuration;

namespace DMO.UnitTests.Controlo.Pesos;

/// <summary>
/// Focused WDL proofs of the Owner clarification WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP
/// (P2-T05 focused correction): the operator enters ONLY the water temperature; the application
/// automatically resolves the water density from the authoritative water-temperature table
/// (per whole degree 5–35 °C, nearest whole degree by <c>MidpointRounding.AwayFromZero</c>, NO
/// interpolation); capacity = water weight ÷ resolved water density; the glass-density lookup
/// stays strictly independent (after the post-closure correction it resolves from the
/// Definições settings store, never from the calculation configuration); no manual
/// water-density/divisor input exists in the request/UI contracts; an unresolvable lookup fails
/// closed with <c>calculation-configuration-missing</c>; no invented/interpolated value is ever
/// used; <c>RESULT_NON_POSITIVE</c> behavior stays intact.
/// </summary>
/// <remarks>
/// Every WATER lookup proof runs against the REAL shipped configuration implementation
/// (<see cref="ConfigurationCalculationConfiguration"/>) and its built-in authoritative table
/// (<see cref="ConfigurationCalculationConfiguration.AuthoritativeWaterDensityByCelsius"/>), so
/// the tests prove the application's automatic resolution itself — not a test double. The
/// per-row formula proofs reuse the authoritative water values end-to-end through
/// <see cref="ControloCreateService"/>; the glass densities are store-seeded current operational
/// values (fixture values 2.50/2.52 or the authoritative bootstrap, per test).
/// </remarks>
public sealed class WaterDensityLookupTests
{
    // -----------------------------------------------------------------------------------------
    // WDL1 — operator supplies only the water temperature (no density, no divisor input)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL1 (Owner clarification requirement 1 + 6) — every Peso request carrier declares the
    /// entered <c>WaterTemperature</c> and NO water-density/divisor member: the operator cannot
    /// supply a density or a divisor through any create/edit/calculate carrier (application
    /// commands and transport requests alike); the per-row carrier carries the entered water
    /// weight only.
    /// </summary>
    [Fact]
    public void WDL1_OperatorSuppliesOnlyTheWaterTemperature_NoDensityOrDivisorInputExists()
    {
        var headerCarriers = new[]
        {
            typeof(CalculatePesoCommand),
            typeof(CreatePesoCommand),
            typeof(UpdatePesoCommand),
            typeof(ControloCreateEndpoints.CreatePesoRequest),
            typeof(ControloCreateEndpoints.UpdatePesoRequest),
            typeof(ControloCreateEndpoints.CalculatePesoRequest),
        };

        foreach (var carrier in headerCarriers)
        {
            var names = DeclaredProperties(carrier);

            Assert.Contains("WaterTemperature", names);
            AssertNoDensityOrDivisorInput(names, carrier);
        }

        // The per-row carrier is the entered water weight only — no temperature repetition, no
        // density, no divisor, no selection of any calculation factor.
        var rowNames = DeclaredProperties(typeof(ControloCreateEndpoints.PesoRowRequest));
        Assert.Equal(new[] { "WaterWeightG" }, rowNames);
    }

    private static List<string> DeclaredProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

    private static void AssertNoDensityOrDivisorInput(List<string> names, Type carrier)
    {
        Assert.DoesNotContain(names, name => name.Contains("Density", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Divisor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Factor", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------------------------
    // WDL2 — every whole degree 5–35 resolves the exact authoritative density
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL2 (requirement 2) — the built-in authoritative table holds exactly the 31 whole-degree
    /// entries 5–35 °C, and the real shipped configuration resolves each of them to the exact
    /// authoritative value. The values are the application's own historical table (legacy
    /// production engine <c>WeightCalculator.LookupDensity</c> / <c>DENSITY_TABLE</c>), NOT
    /// invented and NOT generic internet values.
    /// </summary>
    [Theory]
    [InlineData(5, 0.99888)]
    [InlineData(6, 0.99885)]
    [InlineData(7, 0.99882)]
    [InlineData(8, 0.99877)]
    [InlineData(9, 0.99871)]
    [InlineData(10, 0.99863)]
    [InlineData(11, 0.99854)]
    [InlineData(12, 0.99844)]
    [InlineData(13, 0.99832)]
    [InlineData(14, 0.99819)]
    [InlineData(15, 0.99805)]
    [InlineData(16, 0.99789)]
    [InlineData(17, 0.99773)]
    [InlineData(18, 0.99765)]
    [InlineData(19, 0.99737)]
    [InlineData(20, 0.99717)]
    [InlineData(21, 0.99696)]
    [InlineData(22, 0.99674)]
    [InlineData(23, 0.99652)]
    [InlineData(24, 0.99628)]
    [InlineData(25, 0.99603)]
    [InlineData(26, 0.99577)]
    [InlineData(27, 0.99551)]
    [InlineData(28, 0.99523)]
    [InlineData(29, 0.99494)]
    [InlineData(30, 0.99485)]
    [InlineData(31, 0.99435)]
    [InlineData(32, 0.99403)]
    [InlineData(33, 0.99371)]
    [InlineData(34, 0.99339)]
    [InlineData(35, 0.99305)]
    public void WDL2_EveryWholeDegree5To35ResolvesTheExactAuthoritativeDensity(int celsius, double expected)
    {
        var configuration = EmptyConfiguration();

        Assert.True(configuration.TryGetWaterDensity(celsius, out var density));
        Assert.Equal((decimal)expected, density);
    }

    /// <summary>
    /// WDL2 (requirement 2) — the built-in table shape is exactly the 31 whole-degree entries
    /// 5–35 °C: no fractional keys exist, no entry lies outside the accepted range, and the
    /// table count matches the authority (31 entries, TD-25).
    /// </summary>
    [Fact]
    public void WDL2_TheBuiltInTableIsExactlyThe31WholeDegreeEntriesFrom5To35()
    {
        Assert.Equal(31, ConfigurationCalculationConfiguration.AuthoritativeWaterDensityByCelsius.Count);

        for (var degree = 5; degree <= 35; degree++)
        {
            Assert.True(
                ConfigurationCalculationConfiguration.AuthoritativeWaterDensityByCelsius.ContainsKey(degree),
                $"The authoritative table must contain the whole degree {degree}.");
        }

        Assert.Equal(5, ConfigurationCalculationConfiguration.MinTemperatureCelsius);
        Assert.Equal(35, ConfigurationCalculationConfiguration.MaxTemperatureCelsius);

        Assert.All(
            ConfigurationCalculationConfiguration.AuthoritativeWaterDensityByCelsius.Keys,
            degree => Assert.InRange(degree, 5, 35));
    }

    // -----------------------------------------------------------------------------------------
    // WDL3 — rounding rule; NO interpolation, NO invented value
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL3 (requirement 8) — the authoritative lookup rule: the entered temperature is rounded
    /// to the nearest whole degree (<see cref="MidpointRounding.AwayFromZero"/>) and the exact
    /// table entry is returned. A fractional temperature NEVER produces an interpolated value:
    /// 20.5 → 21 → 0.99696 (not the arithmetic midpoint 0.997065 of the 20/21 entries).
    /// </summary>
    [Fact]
    public void WDL3_ResolutionRoundsToTheNearestWholeDegreeAndNeverInterpolates()
    {
        var configuration = EmptyConfiguration();

        Assert.True(configuration.TryGetWaterDensity(20.4m, out var at204));
        Assert.Equal(0.99717m, at204); // 20.4 → 20 °C entry

        Assert.True(configuration.TryGetWaterDensity(20.5m, out var at205));
        Assert.Equal(0.99696m, at205); // 20.5 → 21 °C entry (AwayFromZero)

        Assert.True(configuration.TryGetWaterDensity(20.49m, out var at2049));
        Assert.Equal(0.99717m, at2049); // 20.49 → 20 °C entry

        Assert.True(configuration.TryGetWaterDensity(25.49m, out var at2549));
        Assert.Equal(0.99603m, at2549); // 25.49 → 25 °C entry

        Assert.True(configuration.TryGetWaterDensity(34.5m, out var at345));
        Assert.Equal(0.99305m, at345); // 34.5 → 35 °C entry

        // The 20/21 midpoint is NEVER used: no interpolation exists between entries.
        var inventedMidpoint = (0.99717m + 0.99696m) / 2m;
        Assert.NotEqual(inventedMidpoint, at205);
        Assert.NotEqual(inventedMidpoint, at204);
    }

    // -----------------------------------------------------------------------------------------
    // WDL4 — capacity = water weight ÷ resolved water density (authoritative values, end-to-end)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL4 (requirement 3) — capacity uses exactly <c>water_weight ÷ resolved water_density</c>
    /// with the AUTHORITATIVE 20 °C density 0.99717: 997.17 g → 1000.0000 cm³, and glass weight
    /// = (capacity + Marisa − Punção) × glass density. The stateless result echoes the resolved
    /// water density as a display fact; nothing is persisted by the calculation.
    /// </summary>
    [Fact]
    public async Task WDL4_CapacityIsWaterWeightDividedByTheResolvedAuthoritativeWaterDensity()
    {
        var toolId = Guid.NewGuid();
        var store = new FakeGlassDensitySettingsRepository(
            new Dictionary<string, decimal> { ["NNPB"] = 2.50m });
        var (service, pesos) = BuildService(EmptyConfiguration(), toolId, Processo.Nnpb, store);

        var result = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 20m, null, null, null, null, [997.17m]),
            CancellationToken.None);

        var value = Assert.IsType<PesoResult.Calculation>(result).Value;
        Assert.Equal(20m, value.WaterTemperature);
        Assert.Equal(0.99717m, value.WaterDensityGCm3); // authoritative 20 °C entry
        Assert.Equal(2.50m, value.GlassDensityGCm3);

        var row = Assert.Single(value.Rows);
        Assert.Equal(1000m, row.CapacityCm3); // 997.17 ÷ 0.99717 = 1000
        Assert.Equal(2500m, row.GlassWeightG); // (1000 + 0 − 0) × 2.50

        Assert.Equal(0, pesos.StoredCount);
    }

    // -----------------------------------------------------------------------------------------
    // WDL5 — temperature change changes the resolved density and the capacity
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL5 (requirement 4) — changing the entered temperature changes the resolved density and,
    /// with the same water weight, the resulting capacity: warmer water is less dense, so the
    /// same weight occupies MORE volume (capacity grows as temperature rises).
    /// </summary>
    [Fact]
    public async Task WDL5_ChangingTheTemperatureChangesTheResolvedDensityAndTheCapacity()
    {
        var toolId = Guid.NewGuid();
        var store = new FakeGlassDensitySettingsRepository(
            new Dictionary<string, decimal> { ["NNPB"] = 2.50m });
        var (service, pesos) = BuildService(EmptyConfiguration(), toolId, Processo.Nnpb, store);

        var at20 = await CalculateAsync(service, toolId, 20m, 1000m);
        var at25 = await CalculateAsync(service, toolId, 25m, 1000m);

        // The resolved densities differ (authoritative values).
        Assert.Equal(0.99717m, at20.WaterDensityGCm3);
        Assert.Equal(0.99603m, at25.WaterDensityGCm3);
        Assert.NotEqual(at20.WaterDensityGCm3, at25.WaterDensityGCm3);

        // Same 1000 g of water: capacity(25 °C) > capacity(20 °C), both strictly positive.
        var capacity20 = Assert.Single(at20.Rows).CapacityCm3;
        var capacity25 = Assert.Single(at25.Rows).CapacityCm3;
        Assert.True(capacity20 > 0 && capacity25 > 0);
        Assert.True(capacity25 > capacity20, "Warmer water is less dense: the same weight occupies more volume.");

        Assert.Equal(0, pesos.StoredCount);
    }

    // -----------------------------------------------------------------------------------------
    // WDL6 — glass-density lookup remains strictly independent of the water-density lookup
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL6 (requirement 5) — the two density lookups are separate facts: for the SAME
    /// temperature and water facts, the capacity (fed by water density) is identical while the
    /// glass weight (fed by the processo→glass-density CURRENT OPERATIONAL settings value)
    /// differs per processo.
    /// </summary>
    [Fact]
    public async Task WDL6_GlassDensityLookupIsIndependentFromTheWaterDensityLookup()
    {
        var toolId = Guid.NewGuid();
        var store = new FakeGlassDensitySettingsRepository(
            new Dictionary<string, decimal> { ["NNPB"] = 2.50m, ["PS"] = 2.52m });

        var (npbService, _) = BuildService(EmptyConfiguration(), toolId, Processo.Nnpb, store);
        var (psService, _) = BuildService(EmptyConfiguration(), toolId, Processo.Ps, store);

        var npb = await CalculateAsync(npbService, toolId, 20m, 997.17m);
        var ps = await CalculateAsync(psService, toolId, 20m, 997.17m);

        // Same water density resolution → same capacity.
        Assert.Equal(0.99717m, npb.WaterDensityGCm3);
        Assert.Equal(0.99717m, ps.WaterDensityGCm3);
        Assert.Equal(
            Assert.Single(npb.Rows).CapacityCm3,
            Assert.Single(ps.Rows).CapacityCm3);

        // Different glass density (NNPB 2.50 vs PS 2.52) → different glass weight.
        Assert.Equal(2.50m, npb.GlassDensityGCm3);
        Assert.Equal(2.52m, ps.GlassDensityGCm3);
        Assert.Equal(2500m, Assert.Single(npb.Rows).GlassWeightG);
        Assert.Equal(2520m, Assert.Single(ps.Rows).GlassWeightG);
    }

    /// <summary>
    /// WDL6 (requirement 5) — independence in BOTH directions: with the water table resolvable
    /// but the glass settings row absent, the calculation is refused with
    /// <c>calculation-configuration-missing</c> (nothing written, no invented glass density) —
    /// while the water-density lookup itself still resolves. The water lookup is never
    /// contaminated by the glass settings and vice versa.
    /// </summary>
    [Fact]
    public async Task WDL6_GlassMappingAbsentRefusesTheCalculationWithoutAffectingTheWaterLookup()
    {
        var configuration = EmptyConfiguration(); // built-in water table; NO glass settings row
        var toolId = Guid.NewGuid();
        var store = new FakeGlassDensitySettingsRepository();
        store.Remove("NNPB");
        var (service, pesos) = BuildService(configuration, toolId, Processo.Nnpb, store);

        // The water lookup itself fully resolves.
        Assert.True(configuration.TryGetWaterDensity(20m, out var waterDensity));
        Assert.Equal(0.99717m, waterDensity);

        // The calculation cannot proceed without the glass settings row: typed refusal, nothing written.
        var calculated = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 20m, null, null, null, null, [997.17m]),
            CancellationToken.None);
        var refusal = Assert.IsType<PesoResult.Refused>(calculated);
        Assert.Equal(PesoRefusalReason.CalculationConfigurationMissing, refusal.Reason);
        Assert.Equal(0, pesos.StoredCount);
    }

    // -----------------------------------------------------------------------------------------
    // WDL7/WDL8 — unresolvable lookups fail closed (typed calculation-configuration semantics)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL7 (requirement 7) — a valid-range temperature absent from a SUPPLIED override table
    /// fails closed exactly like a missing mapping: the lookup returns <c>false</c> and the
    /// calculation is refused with <c>calculation-configuration-missing</c> — nothing is
    /// invented, nothing is silently substituted, nothing is written.
    /// </summary>
    [Fact]
    public async Task WDL7_ValidRangeTemperatureAbsentFromTheOverrideTableFailsClosed()
    {
        // Override table covers ONLY 20 °C (deployment calibration); 25 °C is absent.
        var configuration = ConfigurationWith(
            ("Controlo:Calculation:WaterDensities:0:Temperature", "20"),
            ("Controlo:Calculation:WaterDensities:0:Density", "0.99717"));

        Assert.True(configuration.TryGetWaterDensity(20m, out _));
        Assert.False(configuration.TryGetWaterDensity(25m, out _)); // fail closed

        var toolId = Guid.NewGuid();
        var store = new FakeGlassDensitySettingsRepository(
            new Dictionary<string, decimal> { ["NNPB"] = 2.50m });
        var (service, pesos) = BuildService(configuration, toolId, Processo.Nnpb, store);

        var calculated = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, 25m, null, null, null, null, [997.17m]),
            CancellationToken.None);
        var refusal = Assert.IsType<PesoResult.Refused>(calculated);
        Assert.Equal(PesoRefusalReason.CalculationConfigurationMissing, refusal.Reason);

        var created = await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 25m, null, null, null, null, [997.17m], Guid.NewGuid()),
            CancellationToken.None);
        var createRefusal = Assert.IsType<PesoResult.Refused>(created);
        Assert.Equal(PesoRefusalReason.CalculationConfigurationMissing, createRefusal.Reason);
        Assert.Equal(0, pesos.StoredCount);
    }

    /// <summary>
    /// WDL8 (requirement 7) — lookup-level fail-closed for unsupported degrees: a temperature
    /// whose nearest whole degree lies outside 5–35 °C resolves to <c>false</c> (defense in
    /// depth; the request validator already refuses entered temperatures outside the accepted
    /// 5–35 °C range), while the legitimate boundaries of the authoritative rounding rule
    /// (4.50 → 5, 35.49 → 35) still resolve.
    /// </summary>
    [Fact]
    public void WDL8_UnsupportedDegreesFailClosedAtTheLookupAndBoundariesStillResolve()
    {
        var configuration = EmptyConfiguration();

        Assert.False(configuration.TryGetWaterDensity(4.49m, out _)); // rounds to 4 → outside 5–35
        Assert.False(configuration.TryGetWaterDensity(35.50m, out _)); // rounds to 36 → outside 5–35
        Assert.False(configuration.TryGetWaterDensity(0m, out _));
        Assert.False(configuration.TryGetWaterDensity(40m, out _));

        // The authoritative rounding boundaries (legacy D2/D4): 4.50 rounds to 5, 35.49 rounds to 35.
        Assert.True(configuration.TryGetWaterDensity(4.50m, out var at450));
        Assert.Equal(0.99888m, at450);
        Assert.True(configuration.TryGetWaterDensity(35.49m, out var at3549));
        Assert.Equal(0.99305m, at3549);
    }

    /// <summary>
    /// WDL8 (requirement 8) — a SUPPLIED override table replaces the built-in table exactly:
    /// its entries win (deployment calibration), and a whole degree it omits fails closed — the
    /// built-in value is NEVER silently substituted under a supplied override.
    /// </summary>
    [Fact]
    public void WDL8_ASuppliedOverrideTableReplacesTheBuiltInTableWithoutSilentFallback()
    {
        var configuration = ConfigurationWith(
            ("Controlo:Calculation:WaterDensities:0:Temperature", "20"),
            ("Controlo:Calculation:WaterDensities:0:Density", "0.9980"));

        Assert.True(configuration.TryGetWaterDensity(20m, out var at20));
        Assert.Equal(0.9980m, at20); // the override entry wins over the built-in 0.99717

        Assert.False(configuration.TryGetWaterDensity(21m, out _)); // absent → fail closed, no fallback
        Assert.False(configuration.TryGetWaterDensity(25m, out _));
        Assert.False(configuration.TryGetWaterDensity(35m, out _));
    }

    // -----------------------------------------------------------------------------------------
    // WDL9 — RESULT_NON_POSITIVE stays intact with the shipped configuration
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// WDL9 (requirement 9) — with the REAL shipped configuration (authoritative water table +
    /// glass mapping), a computed per-row result that is not strictly positive is still refused
    /// BEFORE any write with exactly <c>RESULT_NON_POSITIVE</c>: Punção/PU exceeding
    /// capacity + Marisa/BQ turns the glass weight non-positive. Nothing reaches the repository.
    /// </summary>
    [Fact]
    public async Task WDL9_ResultNonPositiveBehaviorRemainsIntactWithTheShippedConfiguration()
    {
        var toolId = Guid.NewGuid();
        var store = new FakeGlassDensitySettingsRepository(
            new Dictionary<string, decimal> { ["NNPB"] = 2.50m });
        var (service, pesos) = BuildService(EmptyConfiguration(), toolId, Processo.Nnpb, store);

        var created = await service.CreateAsync(
            new CreatePesoCommand(null, toolId, 20m, 0m, 5000m, null, null, [1000m], Guid.NewGuid()),
            CancellationToken.None);

        var errors = Assert.IsType<PesoResult.ValidationFailed>(created).Errors;
        Assert.Equal(new[] { ControloCreateValidationErrors.ResultNonPositive }, errors);
        Assert.Equal(0, pesos.StoredCount);
    }

    // -----------------------------------------------------------------------------------------
    // Fakes
    // -----------------------------------------------------------------------------------------

    private static async Task<PesoCalculation> CalculateAsync(
        IControloCreateService service,
        Guid toolId,
        decimal waterTemperature,
        decimal waterWeightG)
    {
        var result = await service.CalculateAsync(
            new CalculatePesoCommand(null, toolId, waterTemperature, null, null, null, null, [waterWeightG]),
            CancellationToken.None);

        return Assert.IsType<PesoResult.Calculation>(result).Value;
    }

    private static (ControloCreateService Service, FakePesoRepository Pesos) BuildService(
        IControloCalculationConfiguration calculation,
        Guid cmToolId,
        Processo processo,
        FakeGlassDensitySettingsRepository? glassDensities = null)
    {
        var pesos = new FakePesoRepository();

        var service = new ControloCreateService(
            pesos,
            new NullPesoContextRead(),
            new UnusedJobOnService(),
            new FakeToolService(cmToolId, processo),
            calculation,
            glassDensities ?? new FakeGlassDensitySettingsRepository());

        return (service, pesos);
    }

    private static ConfigurationCalculationConfiguration EmptyConfiguration() =>
        new(new ConfigurationBuilder().Build());

    private static ConfigurationCalculationConfiguration ConfigurationWith(
        params (string Key, string Value)[] entries) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build());

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
}