namespace DMO.Application.Controlo.Approve;

/// <summary>
/// The supplied review-identification facts of ONE side of the Comparação relation (P2-T06
/// contract §17.2 / AC-CP2): enough current/previous context for human understanding of the
/// comparison — always the supplied facts, never inference. Every member is backend-published by
/// the future carrier contract; the composition never re-derives or completes them.
/// </summary>
public sealed record ComparisonPesoContext(
    Guid PesoId,
    string? Reference,
    string? ProductionNumber,
    DateTimeOffset? SubmittedAt);

/// <summary>
/// The authoritative carrier of the persisted Comparação relation (P2-T06 contract §17.2 /
/// AC-CP1): the current side and the previous side of <c>current_peso_id → previous_peso_id</c>.
/// The previous side's <see cref="ComparisonPesoContext.PesoId"/> IS the persisted
/// <c>previous_peso_id</c> — there is no other id anywhere in the carrier, so no substitution
/// is representable.
/// </summary>
/// <remarks>
/// This is the seam's SUPPLIED input, not a persistence carrier: no table, no column, no
/// repository, no route defines it (AC-CP4). It is the shape the future Comparação remainder
/// contract (its own gate) will publish through the shared read model; until then no producer
/// exists and the seam stays dormant.
/// </remarks>
public sealed record ComparisonCarrier(
    ComparisonPesoContext Current,
    ComparisonPesoContext Previous);

/// <summary>
/// The composed comparison review context (P2-T06 contract §17.2 / AC-CP1/AC-CP2): the EXACT
/// supplied relation with the EXACT supplied current/previous context — no substitution, no
/// fallback, no mutation. The relation ids are derived only from the supplied sides.
/// </summary>
public sealed record ComparisonComposition(
    ComparisonPesoContext Current,
    ComparisonPesoContext Previous)
{
    /// <summary>The exact supplied id of the current side.</summary>
    public Guid CurrentPesoId => Current.PesoId;

    /// <summary>The exact supplied <c>previous_peso_id</c> — retained verbatim, never invented
    /// and never replaced.</summary>
    public Guid PreviousPesoId => Previous.PesoId;
}

/// <summary>
/// The P2-T06 Comparação composition seam (contract §26.4 row CP4): the pure, application-level,
/// read-only composition function that pins HOW the review composes the persisted relation WHEN
/// a future authoritative carrier is supplied.
/// </summary>
/// <remarks>
/// <para>
/// Contract: given a supplied <see cref="ComparisonCarrier"/>, the composition exposes exactly
/// the supplied <c>previous_peso_id</c> and the supplied current/previous context
/// (AC-CP1/AC-CP2); without a carrier it returns <see langword="null"/> — the deliberate absence
/// (AC-CP4: no fabricated relation, no synthetic comparison state, no heuristic pairing, no
/// "latest Peso" substitution).
/// </para>
/// <para>
/// The function is dormant by construction: it has no production call site today because no
/// authoritative carrier exists in the current schema (Comparação remainder is NOT AUTHORIZED).
/// It is incapable of inventing, searching for, choosing or persisting a previous Peso — its
/// only input is the supplied carrier and it performs no query and no mutation.
/// </para>
/// </remarks>
public static class ComparisonComposer
{
    /// <summary>
    /// Composes the exact-relation review context from the supplied authoritative carrier, or
    /// returns <see langword="null"/> when no carrier exists. The returned composition retains
    /// the exact supplied <c>previous_peso_id</c> and the exact supplied current/previous context
    /// (same instances — nothing is copied, rebuilt, substituted or inferred); the supplied
    /// carrier is never mutated.
    /// </summary>
    public static ComparisonComposition? Compose(ComparisonCarrier? carrier) =>
        carrier is null
            ? null
            : new ComparisonComposition(carrier.Current, carrier.Previous);
}