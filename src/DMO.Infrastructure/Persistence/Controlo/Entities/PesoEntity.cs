namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>pesos</c> table: one Peso control/result fact.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.1. The exclusive anchor is DB-enforced
/// (<c>((cm_id IS NULL)::int + (tool_id IS NULL)::int) = 1</c>): a Peso always anchors to exactly
/// one of <see cref="CmId"/> (production) or <see cref="ToolId"/> (truthful pending). Deliberately
/// absent: <c>jobon_id</c>, <c>reference</c>, <c>production_number</c>, <c>machine</c>,
/// <c>previous_peso_id</c>, approval columns (P2-T06) and any jsonb.
/// </remarks>
public sealed class PesoEntity
{
    /// <summary>Primary key (backend-allocated; never client-supplied).</summary>
    public Guid PesoId { get; set; }

    /// <summary>Production anchor (FK → cm_contexts RESTRICT); null while pending.</summary>
    public Guid? CmId { get; set; }

    /// <summary>Truthful pending anchor (FK → tools RESTRICT); null once production-bound.</summary>
    public Guid? ToolId { get; set; }

    /// <summary>Stored status token; P2-T05 writes only <c>pendente</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Reviewable handoff carrier; NULL until submit.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>Backend-set submitter (FK → users RESTRICT); NULL until submit.</summary>
    public Guid? SubmittedByUserId { get; set; }

    /// <summary>Required water temperature (°C, 5–35 CHECK).</summary>
    public decimal WaterTemperature { get; set; }

    /// <summary>Drawing input, cm³; NULL = not entered.</summary>
    public decimal? VolumeMarisaBq { get; set; }

    /// <summary>Drawing input, cm³; NULL = not entered.</summary>
    public decimal? VolumePuncaoPu { get; set; }

    /// <summary>Frozen process-derived density used; NULL only before the first successful calculate/save.</summary>
    public decimal? GlassDensityGCm3 { get; set; }

    /// <summary>Optional manual SAP reference; nonblank when present.</summary>
    public string? PreviousProductionEndReference { get; set; }

    /// <summary>Optional manual SAP reference; nonblank when present.</summary>
    public string? PreviousAverageWeightReference { get; set; }

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Backend-set actor (FK → users RESTRICT).</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}