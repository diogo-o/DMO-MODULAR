namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>comparacao_measurement_rows</c> table: one NEW comparison
/// measurement row of one compared CM subject, identified by the natural
/// <c>(comparacao_id, cm_id, row_position)</c> triple.
/// </summary>
/// <remarks>
/// <para>
/// The entered <see cref="WaterWeightG"/> and the backend-derived
/// <see cref="CapacityCm3"/>/<see cref="GlassWeightG"/> are persisted per row (registered facts,
/// computed by the SAME shared Peso calculation path as the initial Peso — never a duplicated
/// formula). The rows are physically separated from <c>peso_measurement_rows</c> and keyed to
/// the subject by the natural <c>(comparacao_id, cm_id)</c> pair: comparison measurements are
/// NEW results that can never be confused with — and never contribute to — the initial Peso
/// measurements/average. No reading UUID exists: the composite primary key (plus the dense
/// 1-based <see cref="RowPosition"/>) is the row identity.</para>
/// <para>
/// No <c>version</c>: rows are only written inside a subject mutation transaction and the
/// subject's version protects the aggregate (the accepted <c>PesoMeasurementRow</c> convention).</para>
/// </remarks>
public sealed class ComparacaoMeasurementRowEntity
{
    /// <summary>The owning comparison event (composite PK part 1; composite FK part 1 to the
    /// subject's natural key).</summary>
    public Guid ComparacaoId { get; set; }

    /// <summary>The EXACT canonical CM identity of the subject (composite PK part 2; composite FK
    /// part 2).</summary>
    public Guid CmId { get; set; }

    /// <summary>Dense 1-based ordinal within the subject (composite PK part 3; CHECK <c>&gt;= 1</c>).</summary>
    public int RowPosition { get; set; }

    /// <summary>"Peso de água" of the re-measurement, grams (CHECK <c>&gt; 0</c>).</summary>
    public decimal WaterWeightG { get; set; }

    /// <summary>Derived per-row capacity, cm³ (CHECK <c>&gt; 0</c>).</summary>
    public decimal CapacityCm3 { get; set; }

    /// <summary>Derived per-row glass weight, g (CHECK <c>&gt; 0</c>).</summary>
    public decimal GlassWeightG { get; set; }

    /// <summary>Creation instant (UTC, default <c>now()</c>).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}