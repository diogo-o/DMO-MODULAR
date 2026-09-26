namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>comparacao_cm_subjects</c> table: one compared CM subject of one
/// comparison event, identified by the natural relationship <c>comparacao_id + cm_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// The subject's identity IS the natural key <c>(comparacao_id, cm_id)</c> (the composite
/// primary key): <see cref="CmId"/> is the EXACT existing CM identity of the original production
/// context (<c>cm_contexts</c>) — the frozen CM identity of that JobOn/production preserving the
/// link to the JobOn context, the canonical <c>tool_id</c> and the frozen tool snapshot. No new
/// CM identity exists, nothing is copied from reference/lot text and there is no
/// comparison-specific replacement for <c>cm_id</c>.</para>
/// <para>
/// The decision is individual per compared CM and FINAL inside one comparison event: the current
/// decision state lives on this row (<see cref="Decision"/>/<see cref="DecidedByUserId"/>/
/// <see cref="DecidedAt"/>/<see cref="Reason"/>); a second decision of the same subject is
/// refused (never an overwrite). The CHECKs enforce the closed two-value vocabulary, the
/// put-aside justification rule and the state-column consistency; <see cref="Version"/> is the
/// subject's optimistic-concurrency token.</para>
/// </remarks>
public sealed class ComparacaoCmSubjectEntity
{
    /// <summary>The owning comparison event (composite PK part 1; FK → comparacoes RESTRICT).</summary>
    public Guid ComparacaoId { get; set; }

    /// <summary>The EXACT canonical CM identity of the production context (composite PK part 2;
    /// FK → cm_contexts RESTRICT).</summary>
    public Guid CmId { get; set; }

    /// <summary>Backend-set selection actor (FK → users RESTRICT).</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Selection instant (UTC, default <c>now()</c>).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Final individual decision token; NULL = undecided (CHECK-paired vocabulary).</summary>
    public string? Decision { get; set; }

    /// <summary>Optional reason: required for <c>colocar_de_parte</c>, never fabricated for <c>manter</c>.</summary>
    public string? Reason { get; set; }

    /// <summary>Backend-authored decision actor (FK → users RESTRICT); NULL until decided.</summary>
    public Guid? DecidedByUserId { get; set; }

    /// <summary>Backend-clock decision instant; NULL until decided.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Optimistic-concurrency token; guards measurement recording and the final decision.</summary>
    public int Version { get; set; }

    /// <summary>Last-update instant (UTC, default <c>now()</c>).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}