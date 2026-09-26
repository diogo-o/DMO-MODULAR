using DMO.Application.Controlo.Pesos;
using Microsoft.Extensions.Configuration;

namespace DMO.Infrastructure.Configuration;

/// <summary>
/// The configuration-backed <see cref="DMO.Application.ControloCreate.IControloCalculationConfiguration"/>:
/// the <c>Controlo:Calculation</c> sections.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §5.2/§5.3 and Q-CALC, as clarified by the Owner
/// (WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP) and as amended by the post-closure glass-density
/// correction (Owner rule GLASS_DENSITY_CONFIGURATION): after the correction this class carries
/// the WATER side only.
/// <para>
/// <b>Water density:</b> the application ships the authoritative water-temperature table
/// (<see cref="AuthoritativeWaterDensityByCelsius"/> — per whole degree 5–35 °C, recovered from
/// the legacy production calculation engine, see the table's own remarks). The operator enters
/// only the water temperature; the density of the entered temperature's closest whole degree
/// (<see cref="MidpointRounding.AwayFromZero"/>) is resolved automatically — <b>no
/// interpolation</b> and no invented value. A deployment may override the whole table through
/// the backend calculation configuration section <c>Controlo:Calculation:WaterDensities</c>
/// (entries <c>{"Temperature": 20, "Density": 0.99717}</c>, whole-degree keys; a supplied
/// section <b>replaces</b> the built-in table so fail-closed semantics stay exact — an entry
/// absent from the override resolves to <c>false</c> and the typed
/// <c>calculation-configuration-missing</c> refusal fires; an absent section means the
/// authoritative built-in table). The water density is never a Peso-owned editable fact, never
/// an operator input and never part of <c>Controlo → Definições</c>.</para>
/// <para>
/// <b>Glass density:</b> resolved ONLY from the current operational value of the anchor's
/// processo in the Definições settings store (<c>glass_density_settings</c>); the former
/// deployment section <c>Controlo:Calculation:GlassDensities</c> LOST authority and is no
/// longer read anywhere (no dual source of truth — the calculation reads the operational store
/// through <c>DMO.Application.Repositories.IGlassDensitySettingsRepository</c>). This lookup is
/// independent of the water-density lookup and is never confused with it.</para>
/// </remarks>
public sealed class ConfigurationCalculationConfiguration : IControloCalculationConfiguration
{
    private readonly IReadOnlyDictionary<int, decimal>? _waterDensities;

    /// <summary>Section root of the whole calculation configuration.</summary>
    public const string SectionName = "Controlo:Calculation";

    /// <summary>Name of the water-temperature table override section (whole-degree entries).</summary>
    public const string WaterDensitiesSectionName = "Controlo:Calculation:WaterDensities";

    /// <summary>
    /// The application's AUTHORITATIVE water-temperature table: water density (g/cm³) per whole
    /// degree Celsius, 5–35 °C — 31 entries.
    /// </summary>
    /// <remarks>
    /// Provenance (Owner clarification WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP, "find the real
    /// table"): recovered from the legacy BA-DMO production engine
    /// <c>BA.Dmo.Domain.Modules.Peso.WeightCalculator.LookupDensity</c> /
    /// <c>WaterDensityByCelsius</c> (the single authoritative weight/volume engine, "GLM-PESO-05",
    /// TD-25) and confirmed by the legacy <c>WeightCalculatorTests</c> (31-row density theory,
    /// rounding boundaries D1–D4), the legacy shipped web application <c>DENSITY_TABLE</c>
    /// (<c>app-core.js</c> of the PORTAL_DMO 5.5.0 production distribution and its archived
    /// copies), and the legacy spec <c>02_PESO_COMPLETE_SPEC.md</c> §4
    /// ("lookupDensity(temperature) devolve o valor da tabela DENSITY_TABLE para a temperatura
    /// arredondada (5–35 °C). Se fora do intervalo, erro."). All independent copies agree on the
    /// exact 31 values. The source lookup rule is: round the entered temperature to the nearest
    /// integer degree (<c>Math.Round(…, MidpointRounding.AwayFromZero)</c> in C#;
    /// <c>Math.round</c> in the JS engine — identical for positive temperatures), take the exact
    /// entry; <b>no interpolation</b>, no fallback, no external formula; out of 5–35 °C fails
    /// closed. These values are <b>not</b> generic internet values and are <b>not</b> invented:
    /// they are the application's own historical authoritative table.
    /// </remarks>
    public static readonly IReadOnlyDictionary<int, decimal> AuthoritativeWaterDensityByCelsius =
        new Dictionary<int, decimal>
        {
            [5] = 0.99888m, [6] = 0.99885m, [7] = 0.99882m, [8] = 0.99877m, [9] = 0.99871m,
            [10] = 0.99863m, [11] = 0.99854m, [12] = 0.99844m, [13] = 0.99832m, [14] = 0.99819m,
            [15] = 0.99805m, [16] = 0.99789m, [17] = 0.99773m, [18] = 0.99765m, [19] = 0.99737m,
            [20] = 0.99717m, [21] = 0.99696m, [22] = 0.99674m, [23] = 0.99652m, [24] = 0.99628m,
            [25] = 0.99603m, [26] = 0.99577m, [27] = 0.99551m, [28] = 0.99523m, [29] = 0.99494m,
            [30] = 0.99485m, [31] = 0.99435m, [32] = 0.99403m, [33] = 0.99371m, [34] = 0.99339m,
            [35] = 0.99305m,
        };

    /// <summary>The minimum supported water temperature (inclusive).</summary>
    public const int MinTemperatureCelsius = 5;

    /// <summary>The maximum supported water temperature (inclusive).</summary>
    public const int MaxTemperatureCelsius = 35;

    /// <summary>Binds the configuration sections (a missing section = the authoritative built-in
    /// water table).</summary>
    public ConfigurationCalculationConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // The override section, when present, REPLACES the authoritative built-in table: a
        // deployment that supplies a table keeps exact fail-closed semantics for every whole
        // degree it omits (it never silently falls back to a value it did not configure).
        var entries = configuration.GetSection(WaterDensitiesSectionName).GetChildren()
            .Select(entry => new
            {
                Temperature = TryParseDecimal(entry["Temperature"]),
                Density = TryParseDecimal(entry["Density"]),
            })
            .Where(entry => entry.Temperature is not null && entry.Density is not null)
            .ToArray();

        _waterDensities = entries.Length == 0
            ? null
            : entries
                .GroupBy(entry => RoundToWholeDegree(entry.Temperature!.Value))
                .ToDictionary(group => group.Key, group => group.First().Density!.Value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The authoritative lookup rule is applied here (and MUST be applied by every
    /// implementation): the entered temperature is rounded to the nearest whole degree with
    /// <see cref="MidpointRounding.AwayFromZero"/> and the exact 5–35 °C entry is returned. A
    /// rounded degree outside 5–35 (defensive fail-closed; the request validator already refuses
    /// entered temperatures outside the accepted range), or a whole degree absent from a supplied
    /// override table, resolves to <c>false</c>. No interpolation ever occurs.
    /// </remarks>
    public bool TryGetWaterDensity(decimal waterTemperature, out decimal waterDensity)
    {
        var key = RoundToWholeDegree(waterTemperature);
        if (key < MinTemperatureCelsius || key > MaxTemperatureCelsius)
        {
            waterDensity = default;
            return false;
        }

        if (_waterDensities is not null)
        {
            return _waterDensities.TryGetValue(key, out waterDensity);
        }

        return AuthoritativeWaterDensityByCelsius.TryGetValue(key, out waterDensity);
    }

    /// <summary>The authoritative rounding rule of the water-temperature table (nearest whole
    /// degree, away from zero — the legacy <c>Math.Round(decimal, MidpointRounding.AwayFromZero)</c>
    /// / <c>Math.round</c> rule; identical for positive temperatures).</summary>
    private static int RoundToWholeDegree(decimal temperatureCelsius) =>
        (int)Math.Round(temperatureCelsius, MidpointRounding.AwayFromZero);

    private static decimal? TryParseDecimal(string? value) =>
        decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
}