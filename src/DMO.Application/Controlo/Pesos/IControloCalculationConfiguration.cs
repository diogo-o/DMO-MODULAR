namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// The backend calculation configuration consumed by the Peso formulas (Q-CALC, as clarified by
/// the Owner WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP). After the post-closure glass-density
/// correction (Owner rule GLASS_DENSITY_CONFIGURATION) this seam carries the WATER side only:
/// the <b>glass density</b> is no longer configuration — it is the CURRENT OPERATIONAL value
/// maintained in <c>Controlo → Definições</c> (<c>glass_density_settings</c>, one row per
/// canonical processo NNPB/PS) and is resolved by the Peso service through the settings store.
/// </summary>
/// <remarks>
/// <para>
/// <b>Water density</b> — resolved automatically from the <b>entered water temperature</b>
/// through the application's authoritative water-temperature table (per whole degree 5–35 °C,
/// the closest whole degree by <see cref="MidpointRounding.AwayFromZero"/>; <b>no
/// interpolation</b>). The operator never enters a water density and never selects a divisor:
/// the "valor da tabela de temperatura" of the contracted formulas <i>is</i> the water density
/// corresponding to the entered temperature. It is used to calculate capacity
/// (<c>capacity = water weight ÷ water density</c>); it is never persisted as a Peso fact itself
/// (the frozen per-row results are the historical truth).</para>
/// <para>
/// <b>Glass density</b> — resolved at first successful calculate/save from the anchor's
/// processo (<c>cm_id → tool_id → processo</c>; pending: <c>tool_id → processo</c>) through the
/// Definições settings store (<c>glass_density_settings</c>), then FROZEN onto the Peso
/// (<c>pesos.glass_density_g_cm3</c>): recalculation and submit re-derive with the frozen value
/// and no later settings change ever rewrites an existing Peso. The former deployment
/// configuration section <c>Controlo:Calculation:GlassDensities</c> lost authority and is no
/// longer read (no dual source of truth).</para>
/// <para>
/// The water-temperature table is <b>application calculation configuration</b>: not Peso-owned
/// editable user data, not an operator-typed field, not part of <c>Controlo → Definições</c>, not
/// a business identity and not a user-CRUD table. It ships with the application as the
/// authoritative built-in table and may be overridden only by backend calculation configuration.</para>
/// <para>
/// Until a value is resolvable, every calculate/create/update/submit that needs it is refused
/// with the typed <c>calculation-configuration-missing</c> refusal and nothing is written
/// (MES9/AC-M9); no value is ever invented and no silent zero is ever produced (the fail-closed
/// rule covers an authoritative lookup point missing from an override table exactly like a
/// missing settings row — defensive only, since both glass rows are always seeded). The
/// configuration-backed implementation lives in DMO.Infrastructure; tests and test hosts supply
/// their own implementations with fixed values.
/// </para>
/// </remarks>
public interface IControloCalculationConfiguration
{
    /// <summary>
    /// Resolves the <b>water density</b> (g/cm³) for the entered water temperature, or
    /// <c>false</c> when no authoritative table entry exists for it.
    /// </summary>
    /// <remarks>
    /// The authoritative lookup rule: the entered temperature is rounded to the nearest whole
    /// degree with <see cref="MidpointRounding.AwayFromZero"/> and the exact 5–35 °C entry of
    /// the water-temperature table is returned. The rounded degree outside 5–35, or a whole
    /// degree absent from a supplied override table, resolves to <c>false</c> — <b>no
    /// interpolation</b> between entries, no fallback formula, no invented value. Implementations
    /// MUST apply this exact rule so the resolved value is always the table density of the
    /// entered temperature's closest whole degree.
    /// </remarks>
    bool TryGetWaterDensity(decimal waterTemperature, out decimal waterDensity);
}