namespace DMO.Domain.ControloComparacao;

/// <summary>
/// One compared CM subject of a Peso Comparação event: the selected canonical <c>cm_id</c>, its
/// NEW comparison measurement rows, its derived results and its INDIVIDUAL (final) decision.
/// </summary>
/// <remarks>
/// <para>
/// The compared CM identity is the natural relationship <c>comparacao_id + cm_id</c>: the
/// subject is identified by its owning comparison event and the EXACT existing CM identity of
/// the original production context (<c>cm_contexts</c>) — the frozen CM identity of that
/// JobOn/production that preserves the link to the JobOn context, the canonical <c>tool_id</c>
/// and the frozen tool snapshot of the production. No new CM identity is created for the
/// comparison, nothing is copied from reference/lot text and later inferred, and there is no
/// comparison-specific replacement for <c>cm_id</c>. A new comparison event always allocates a
/// NEW <c>comparacao_id</c>; reusing the same CM in the same production reuses the SAME
/// <c>cm_id</c> — that is how multiple comparison events stay distinct while remaining
/// connected to the same production context. The schema enforces
/// (comparacao_id, cm_id) as the subject's primary key.</para>
/// <para>
/// Each selected CM is measured again; the comparison may contain any number of subjects
/// (1 CM, 2 CMs, 4 CMs, …) and is never required to contain all CMs. The decision is
/// <b>individual per compared CM</b> and <b>final inside one comparison event</b>:
/// <see cref="Decision"/> (nullable — no decision yet), the backend actor/time
/// (<see cref="DecidedByUserId"/>/<see cref="DecidedAt"/>) and the optional
/// <see cref="Reason"/> (required for <c>colocar_de_parte</c>, never fabricated for
/// <c>manter</c>). A second decision is refused (never an overwrite, silent or otherwise);
/// <see cref="HasMeasurements"/> is the derived "measured" fact used by the completeness rule:
/// every measured subject must have an explicit decision before the comparison event can be
/// confirmed.</para>
/// <para>
/// <see cref="Version"/> is the optimistic-concurrency token of the subject (the accepted
/// <c>Peso.version</c> convention): measurement recording and the decision guard and bump it.</para>
/// </remarks>
public sealed record ComparacaoCmSubject(
    ComparacaoId ComparacaoId,
    Guid CmId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    ComparacaoCmDecisionKind? Decision,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? Reason,
    int Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ComparacaoMeasurementRow> Rows)
{
    /// <summary>Whether the subject has committed comparison measurement rows (the "measured" fact).</summary>
    public bool HasMeasurements => Rows.Count > 0;

    /// <summary>Whether the subject carries its explicit (final) individual decision.</summary>
    public bool IsDecided => Decision is not null;
}