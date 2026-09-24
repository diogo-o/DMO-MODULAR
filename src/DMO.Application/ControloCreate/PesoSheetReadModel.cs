using DMO.Application.JobOn;

namespace DMO.Application.ControloCreate;

/// <summary>
/// The published read-only Peso sheet carrier (C → D seam, exact shape of P2-T05 contract §26.3).
/// </summary>
/// <remarks>
/// <para>
/// The statuses/values are backend facts; presentation rounds to ≤ 2 decimal places only at render
/// time; the read is a read-only projection — composing it never writes, never bumps and never
/// creates (P2-T04 read-transaction rule). P2-T06 consumes this shape through its <b>own</b>
/// <c>controlo-approve</c>-gated routes (P2-T06's contract) and must not fork the renderer
/// (§29).</para>
/// <para>
/// <see cref="CmId"/>/<see cref="ToolId"/> are the exclusive anchor (exactly one non-null).
/// <see cref="Context"/> is the production case (frozen triple + live projection), 
/// <see cref="Pending"/> the pending case, and <see cref="Production"/> the traversal facts
/// (reference/production number/machine through <c>cm_id</c>; <c>null</c> while pending — §6.2:
/// Job-On labels flow through documented traversal only, never frozen on the Peso).</para>
/// </remarks>
public sealed record PesoSheetReadModel(
    Guid PesoId,
    int Version,
    string Status,
    Guid? CmId,
    Guid? ToolId,
    PesoContextProjection? Context,
    PesoPendingProjection? Pending,
    PesoProductionProjection? Production,
    Guid? CreatedByUserId,
    DateTimeOffset CreatedAt,
    Guid? SubmittedByUserId,
    DateTimeOffset? SubmittedAt,
    decimal WaterTemperature,
    decimal? VolumeMarisaBq,
    decimal? VolumePuncaoPu,
    decimal? GlassDensityGCm3,
    string? PreviousProductionEndReference,
    string? PreviousAverageWeightReference,
    IReadOnlyList<PesoRowReadModel> Rows);

/// <summary>
/// The production-case context projection: the real <c>cm_id</c>, its <c>tool_id</c>, the P2-T04
/// frozen triple and the live Tool projection (composed from <c>cm_contexts</c> + <c>tools</c>,
/// §26.3; the frozen triple is never refreshed from the live projection).
/// </summary>
public sealed record PesoContextProjection(
    Guid CmId,
    Guid ToolId,
    string FrozenToolType,
    string FrozenToolReference,
    string FrozenToolLot,
    ToolSummaryProjection Tool);

/// <summary>The pending-case projection: the truthful direct Tool anchor plus its live projection.</summary>
public sealed record PesoPendingProjection(Guid ToolId, ToolSummaryProjection Tool);

/// <summary>
/// The traversal facts of the production case: reference, production number, machine and the
/// production DATE through <c>cm_id → jobon_id</c> (never stored on the Peso; <c>null</c> while
/// pending). <see cref="ProductionDate"/> is the Job On production date — the record's control
/// date; it is never substituted by <c>SubmittedAt</c> (which remains submission/audit
/// information only).
/// </summary>
public sealed record PesoProductionProjection(
    string Reference,
    string ProductionNumber,
    string Machine,
    DateOnly? ProductionDate);

/// <summary>One Peso row of the read model: stable row id, dense position and the three facts.</summary>
public sealed record PesoRowReadModel(
    Guid PesoMeasurementRowId,
    int RowPosition,
    decimal WaterWeightG,
    decimal CapacityCm3,
    decimal GlassWeightG);