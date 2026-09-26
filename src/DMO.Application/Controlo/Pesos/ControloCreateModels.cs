using DMO.Application.JobOn;

namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// The command carriers of the Peso create/measurement core and the closed Peso result set.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §7.1–§7.4, §20.3 and §26.1.
/// <para>
/// The identity rule (PID4/AC-P4): <c>peso_id</c> never appears in any command or request as a
/// client-supplied value for creation — the backend allocates it inside the create transaction.
/// No carrier carries <c>jobon_id</c>, <c>tool_id</c> beyond the accepted anchors, a
/// <c>production_id</c>, a client-minted identity or a draft-key alias.</para>
/// </remarks>
public sealed record CalculatePesoCommand(
    Guid? CmId,
    Guid? PendingToolId,
    decimal WaterTemperature,
    decimal? VolumeMarisaBq,
    decimal? VolumePuncaoPu,
    string? PreviousProductionEndReference,
    string? PreviousAverageWeightReference,
    IReadOnlyList<decimal> RowWaterWeightsG);

/// <summary>
/// The Peso create command: exactly one anchor (<see cref="CmId"/> xor <see cref="PendingToolId"/>)
/// plus the Peso-owned input facts; the backend allocates <c>peso_id</c> and every row id.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §7.1. <see cref="CreatedByUserId"/> is the backend-set actor
/// (current USER) per <c>CONTROLO.md</c> §15; it is resolved by the Web layer from
/// <c>ICurrentAccountContext</c> and never supplied by the client.
/// </remarks>
public sealed record CreatePesoCommand(
    Guid? CmId,
    Guid? PendingToolId,
    decimal WaterTemperature,
    decimal? VolumeMarisaBq,
    decimal? VolumePuncaoPu,
    string? PreviousProductionEndReference,
    string? PreviousAverageWeightReference,
    IReadOnlyList<decimal> RowWaterWeightsG,
    Guid CreatedByUserId);

/// <summary>
/// The Peso draft-edit command: the same <c>peso_id</c>, the observed version and the full input
/// fact set (the anchor is not editable here — association is the dedicated action, §4.4).
/// </summary>
public sealed record UpdatePesoCommand(
    Guid PesoId,
    int ExpectedVersion,
    decimal WaterTemperature,
    decimal? VolumeMarisaBq,
    decimal? VolumePuncaoPu,
    string? PreviousProductionEndReference,
    string? PreviousAverageWeightReference,
    IReadOnlyList<decimal> RowWaterWeightsG);

/// <summary>
/// The Peso submit command: transitions the SAME <c>peso_id</c> into the reviewable handoff with
/// backend state/attribution (§7.4).
/// </summary>
public sealed record SubmitPesoCommand(
    Guid PesoId,
    int ExpectedVersion,
    Guid SubmittedByUserId);

/// <summary>
/// The explicit pending → production association command (§4.4, route 10): the candidate
/// <c>cm_id</c> must resolve to the Peso's anchor <c>tool_id</c>, and the direct Tool anchor is
/// cleared on success.
/// </summary>
public sealed record AssociatePesoCommand(
    Guid PesoId,
    Guid CmId,
    int ExpectedVersion);

/// <summary>One computed per-row result of a calculation: the entered weight plus both derived facts.</summary>
/// <remarks>
/// Authority: P2-T05 contract §5.3/§5.4. The per-row capacity and glass weight are first-class
/// results (full <c>numeric(18,4)</c> precision); presentation rounds to at most two decimal
/// places only (MES7/AC-M7).
/// </remarks>
public sealed record PesoRowCalculation(
    int RowPosition,
    decimal WaterWeightG,
    decimal CapacityCm3,
    decimal GlassWeightG);

/// <summary>
/// The stateless calculation payload (Route 8): echoed anchor, the resolved water density and
/// glass density display facts and the per-row results. No write, no version, no id allocation
/// (§7.2, AC-R7). The water-density fact is the application-resolved value of the entered
/// temperature's authoritative table entry (Owner clarification
/// WATER_TEMPERATURE_TO_WATER_DENSITY_LOOKUP) — an echo of the automatic resolution, never an
/// operator-entered value.
/// </summary>
public sealed record PesoCalculation(
    Guid? CmId,
    Guid? PendingToolId,
    decimal WaterTemperature,
    decimal WaterDensityGCm3,
    decimal GlassDensityGCm3,
    IReadOnlyList<PesoRowCalculation> Rows);

/// <summary>
/// The typed reason of a Peso refusal (the exact transport tokens are in §26.2).
/// </summary>
public enum PesoRefusalReason
{
    /// <summary>The row changed after it was observed; nothing written.</summary>
    StaleVersion,

    /// <summary>A Create-side mutation of a submitted Peso is refused.</summary>
    AlreadySubmitted,

    /// <summary>Association is offered only while pending; the Peso is already production-bound.</summary>
    AlreadyAssociated,

    /// <summary>The candidate <c>cm_id</c> does not resolve to the anchor <c>tool_id</c>.</summary>
    AssociationMismatch,

    /// <summary>The water density or glass-density mapping is unresolvable; no invented value, nothing written.</summary>
    CalculationConfigurationMissing,

    /// <summary>Dependent facts exist; nothing deleted.</summary>
    DependencyExists,
}

/// <summary>
/// The closed Peso result set.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §26.1. No result type carries an approval decision, a document
/// state, a permission decision or a navigation target (BND2/AC-Y2).
/// </remarks>
public abstract record PesoResult
{
    private PesoResult()
    {
    }

    /// <summary>The real backend-allocated <c>peso_id</c> and <c>version = 1</c>.</summary>
    public sealed record Created(Guid PesoId, int Version) : PesoResult;

    /// <summary>The draft was updated; the version incremented exactly once.</summary>
    public sealed record Updated(Guid PesoId, int Version) : PesoResult;

    /// <summary>The same <c>peso_id</c> became reviewable with backend attribution.</summary>
    public sealed record Submitted(Guid PesoId, int Version, DateTimeOffset SubmittedAt) : PesoResult;

    /// <summary>The pending Peso was bound to the explicit candidate <c>cm_id</c>.</summary>
    public sealed record Associated(Guid PesoId, int Version, Guid CmId) : PesoResult;

    /// <summary>The stateless calculation result (no write).</summary>
    /// <remarks>
    /// The positional member is named <c>Value</c> rather than <c>Calculation</c> because a nested
    /// record type and a member of the same name cannot coexist in C# (CS8866); the type identity
    /// <c>PesoResult.Calculation</c> and the positional pattern are unchanged (accepted P2-T04
    /// reading for <c>JobOnResult.Ficha</c>).
    /// </remarks>
    public sealed record Calculation(PesoCalculation Value) : PesoResult;

    /// <summary>The read-only Peso sheet (published C → D carrier shape, §26.3).</summary>
    public sealed record Found(PesoSheetReadModel Sheet) : PesoResult;

    /// <summary>The real association candidates resolving to the anchor Tool.</summary>
    /// <remarks>
    /// The positional member is named <c>Value</c> (same nested-type/member rule as
    /// <see cref="Calculation"/>).
    /// </remarks>
    public sealed record Candidates(IReadOnlyList<PesoAssociationCandidate> Value) : PesoResult;

    /// <summary>The exact contracted validation codes; nothing was written.</summary>
    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : PesoResult;

    /// <summary>The requested Peso does not exist.</summary>
    public sealed record NotFound(Guid PesoId) : PesoResult;

    /// <summary>A typed, actionable refusal; nothing was written.</summary>
    public sealed record Refused(PesoRefusalReason Reason, string Message) : PesoResult;
}