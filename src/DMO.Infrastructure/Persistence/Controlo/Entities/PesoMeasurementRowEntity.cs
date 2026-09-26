namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>peso_measurement_rows</c> table: one measurement row of one Peso.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.2. The entered <see cref="WaterWeightG"/> and the backend-derived
/// <see cref="CapacityCm3"/>/<see cref="GlassWeightG"/> are persisted per row (registered facts,
/// never recomputed from current Tool/Job On state). No <c>version</c>: the parent Peso's version
/// protects the aggregate. Dense 1-based <see cref="RowPosition"/>; no label column (Q-ROWLBL).
/// </remarks>
public sealed class PesoMeasurementRowEntity
{
    /// <summary>Primary key (backend-allocated; stable row identity).</summary>
    public Guid PesoMeasurementRowId { get; set; }

    /// <summary>The owning Peso (FK → pesos RESTRICT).</summary>
    public Guid PesoId { get; set; }

    /// <summary>Dense 1-based ordinal within the Peso (CHECK <c>&gt;= 1</c>).</summary>
    public int RowPosition { get; set; }

    /// <summary>"Peso de água", grams (CHECK <c>&gt; 0</c>).</summary>
    public decimal WaterWeightG { get; set; }

    /// <summary>Derived per-row capacity, cm³ (CHECK <c>&gt; 0</c>).</summary>
    public decimal CapacityCm3 { get; set; }

    /// <summary>Derived per-row glass weight, g (CHECK <c>&gt; 0</c>).</summary>
    public decimal GlassWeightG { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}