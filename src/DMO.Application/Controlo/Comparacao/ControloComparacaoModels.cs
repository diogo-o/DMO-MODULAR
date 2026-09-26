using DMO.Application.Controlo.Pesos;

namespace DMO.Application.Controlo.Comparacao;

/// <summary>
/// The command carriers of the Peso Comparação core and the closed Comparação result set.
/// </summary>
/// <remarks>
/// <para>
/// Comparação is the OPTIONAL child of the initial Peso: <c>peso_id</c> → <c>comparacao_id</c> →
/// one-or-more compared CM subjects. A NEW comparison allocates a NEW <c>comparacao_id</c>; the
/// identity of a compared CM inside one comparison event is the natural relationship
/// <c>comparacao_id + cm_id</c> — the EXACT existing CM identity of the original production
/// context (the frozen <c>cm_contexts</c> identity preserving the link to the JobOn context, the
/// canonical <c>tool_id</c> and the frozen tool snapshot of the production). No command ever
/// carries a newly-minted CM identity, a <c>previous_peso_id</c>, a <c>production_id</c>, a
/// duplicate Tool identity or any value derived from reference/lot text. No carrier carries an
/// average, an approval, a tolerance or a manufacturing-adjustment fact.</para>
/// <para>
/// The identity rule (accepted P2-T05 PID4 convention): <c>comparacao_id</c> is backend-allocated;
/// no client ever supplies or guesses one. Actors (created-by / decided-by / confirmed-by) are
/// backend-set facts carried by the commands, exactly like the accepted
/// <c>CreatePesoCommand.CreatedByUserId</c> convention; the Web layer would resolve them from
/// the current account context.</para>
/// </remarks>
public sealed record StartComparacaoCommand(
    Guid PesoId,
    Guid CreatedByUserId);

/// <summary>Selects ONE compared CM subject for an existing comparison event (the comparison may
/// contain 1 CM, 2 CMs, 4 CMs, … — never a fixed number, never required to contain all CMs).
/// <c>cm_id</c> is the EXACT existing frozen CM identity; nothing is inferred from text.</summary>
public sealed record AddComparacaoCmSubjectCommand(
    Guid ComparacaoId,
    Guid CmId,
    Guid CreatedByUserId);

/// <summary>
/// Records the NEW comparison measurements of one compared CM subject (whole-set replacement of
/// the subject's rows): the operator re-entered water weights are computed with the SAME Peso
/// calculation path over the INITIAL Peso's frozen facts (its water temperature, volumes and
/// frozen glass density) — the comparison never invents conditions and never touches the initial
/// Peso rows.
/// </summary>
public sealed record RecordComparacaoMeasurementsCommand(
    Guid ComparacaoId,
    Guid CmId,
    int ExpectedSubjectVersion,
    IReadOnlyList<decimal> RowWaterWeightsG);

/// <summary>
/// The INDIVIDUAL explicit human decision of ONE compared CM (<c>manter</c> /
/// <c>colocar_de_parte</c>). The decision is per compared CM, never global for the comparison,
/// never implied by the general Peso approval and never automatic. Putting a CM aside requires
/// justification; maintaining it requires no fabricated reason. The decision is FINAL inside one
/// comparison event: a second decision of the same compared CM is refused.
/// </summary>
public sealed record DecideComparacaoCmCommand(
    Guid ComparacaoId,
    Guid CmId,
    int ExpectedSubjectVersion,
    string Decision,
    string? Reason,
    Guid DecidedByUserId);

/// <summary>
/// The explicit human confirmation of a comparison event: refused while any MEASURED CM subject
/// lacks its final individual decision (every measured subject must be decided first).
/// </summary>
public sealed record ConfirmComparacaoCommand(
    Guid ComparacaoId,
    int ExpectedVersion,
    Guid ConfirmedByUserId);

/// <summary>The typed reason of a Comparação refusal.</summary>
public enum ComparacaoRefusalReason
{
    /// <summary>The row changed after it was observed; nothing written.</summary>
    StaleVersion,

    /// <summary>A mutation of an already-confirmed comparison event is refused.</summary>
    AlreadyConfirmed,

    /// <summary>Measurement recording after the final decision, or a second decision of the same
    /// compared CM, is refused (the decision is final inside one comparison event).</summary>
    AlreadyDecided,

    /// <summary>A per-CM decision requires committed comparison measurements first.</summary>
    SubjectNotMeasured,

    /// <summary>Confirmation is refused while a measured CM subject lacks its decision.</summary>
    MeasurementsWithoutDecision,

    /// <summary>Confirmation of a comparison event without any selected CM is refused.</summary>
    NoCmSubjects,

    /// <summary>The comparison conditions (the initial Peso's frozen glass density or the authoritative
    /// water density for its temperature) are not resolvable; no value is invented and nothing written.</summary>
    CalculationConfigurationMissing,

    /// <summary>A referenced record (the Peso, a CM context or an actor) vanished concurrently.</summary>
    DependencyExists,
}

/// <summary>
/// The closed Comparação result set.
/// </summary>
/// <remarks>
/// No result type carries an approval decision, a document state, a permission decision or a
/// navigation target. <see cref="NoComparison"/> is the deliberate ABSENCE carrier: a Peso with
/// no comparison is a fully valid normal Peso and must behave exactly as today.</remarks>
public abstract record ComparacaoResult
{
    private ComparacaoResult()
    {
    }

    /// <summary>The real backend-allocated <c>comparacao_id</c> and <c>version = 1</c>.</summary>
    public sealed record Started(Guid ComparacaoId, int Version) : ComparacaoResult;

    /// <summary>One compared CM subject was selected; the event version incremented exactly once.</summary>
    public sealed record SubjectAdded(Guid ComparacaoId, int Version, Guid CmId) : ComparacaoResult;

    /// <summary>The subject's comparison measurement rows were replaced; the subject version incremented exactly once.</summary>
    public sealed record MeasurementsRecorded(Guid ComparacaoId, Guid CmId, int SubjectVersion) : ComparacaoResult;

    /// <summary>The individual per-CM decision was applied (final inside this comparison event).</summary>
    public sealed record Decided(Guid ComparacaoId, Guid CmId, int SubjectVersion, string Decision, DateTimeOffset DecidedAt) : ComparacaoResult;

    /// <summary>The comparison event was explicitly confirmed (every measured subject decided).</summary>
    public sealed record Confirmed(Guid ComparacaoId, int Version, DateTimeOffset ConfirmedAt) : ComparacaoResult;

    /// <summary>The read-only comparison event (header + subjects + rows + decision state).</summary>
    public sealed record Found(ComparacaoReadModel Value) : ComparacaoResult;

    /// <summary>The initial Peso does not exist.</summary>
    public sealed record PesoNotFound(Guid PesoId) : ComparacaoResult;

    /// <summary>The comparison event does not exist.</summary>
    public sealed record ComparacaoNotFound(Guid ComparacaoId) : ComparacaoResult;

    /// <summary>The comparison event exists but carries no subject for the supplied CANONICAL <c>cm_id</c>.</summary>
    public sealed record CmSubjectNotFound(Guid ComparacaoId, Guid CmId) : ComparacaoResult;

    /// <summary>The Peso has NO comparison — the deliberate absence, never a synthetic comparison.</summary>
    public sealed record NoComparison(Guid PesoId) : ComparacaoResult;

    /// <summary>The exact closed validation codes; nothing was written.</summary>
    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : ComparacaoResult;

    /// <summary>A typed, actionable refusal; nothing was written.</summary>
    public sealed record Refused(ComparacaoRefusalReason Reason, string Message) : ComparacaoResult;
}

/// <summary>
/// The read-only Comparação sheet: the event tied to <c>peso_id</c>, every compared CM subject
/// with its NEW measurement rows/results, its CM context projection (the canonical tool
/// relationship resolved through <c>cm_id</c>) and its individual decision state.
/// </summary>
public sealed record ComparacaoReadModel(
    Guid ComparacaoId,
    Guid PesoId,
    int Version,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    Guid? ConfirmedByUserId,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ComparacaoCmSubjectReadModel> Subjects)
{
    /// <summary>
    /// The derived functional-completeness fact: at least one selected CM exists AND every
    /// measured subject carries its final explicit decision. Never a persisted column.
    /// </summary>
    public bool IsFunctionallyComplete =>
        Subjects.Count > 0 && Subjects.All(subject => !subject.HasMeasurements || subject.IsDecided);
}

/// <summary>One compared CM subject of the read model: the exact <c>cm_id</c>, the resolved CM
/// context (never inferred from text), the comparison rows and the individual decision.</summary>
public sealed record ComparacaoCmSubjectReadModel(
    Guid ComparacaoId,
    Guid CmId,
    CmContextProjection? Context,
    int Version,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    string? Decision,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? Reason,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ComparacaoMeasurementRowReadModel> Rows)
{
    /// <summary>Whether the subject has committed comparison measurement rows.</summary>
    public bool HasMeasurements => Rows.Count > 0;

    /// <summary>Whether the subject carries its final explicit individual decision.</summary>
    public bool IsDecided => Decision is not null;
}

/// <summary>One comparison measurement row of one subject read model (the entered weight plus
/// both backend-derived results).</summary>
public sealed record ComparacaoMeasurementRowReadModel(
    int RowPosition,
    decimal WaterWeightG,
    decimal CapacityCm3,
    decimal GlassWeightG);