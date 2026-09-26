namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>boquilhas</c> table: the register IDENTITY carrier of the
/// movement history of one REAL production/BQ context — or of one canonical BQ Tool while no
/// Job On exists yet (the transitional pré-JobOn anchor of P2-T07 §34).
/// </summary>
/// <remarks>
/// Authority: P2-T07 OWNER CLARIFICATION §34. The row identifies the register: either the REAL
/// production/BQ context (<c>bq_id</c>, UNIQUE — one register per BQ context, DB-enforced) or,
/// transitionally, the canonical BQ <c>tool_id</c> (FK → <c>tools</c>, <c>bq_id</c> NULL). The
/// CHECK <c>boquilhas_anchor_check</c> enforces exactly one anchor (<c>(bq_id IS NULL) &lt;&gt;
/// (tool_id IS NULL)</c>) — never both, never neither. The association write (bq_id set,
/// provisional tool anchor cleared, <c>version</c> + 1, guarded by the version token) is the ONLY
/// update the row ever receives. There is NO <c>status</c>/<c>opening_date</c>/
/// <c>utilisation_percent</c>/<c>observations</c>: no lifecycle state machine exists and the
/// opening facts are removed. Creating the register never manufactures a quantity movement —
/// existence is the row itself.
/// </remarks>
public sealed class BoquilhaEntity
{
    /// <summary>Primary key (backend-allocated inside the create transaction).</summary>
    public Guid BoquilhasId { get; set; }

    /// <summary>
    /// The REAL production anchored <c>bq_contexts</c> row (FK RESTRICT; UNIQUE — one register per
    /// BQ context). NULL while the register is in the transitional pré-JobOn state. Never a fake
    /// Job On / fake bq id / <c>production_id</c>.
    /// </summary>
    public Guid? BqId { get; set; }

    /// <summary>
    /// The provisional canonical BQ Tool anchor of the pré-JobOn state (FK → <c>tools</c>
    /// RESTRICT; NULL once the register is production-linked). The SAME UUID is the association
    /// proof: <c>bq_contexts.tool_id</c> must equal this anchor for the association to be offered.
    /// </summary>
    public Guid? ToolId { get; set; }

    /// <summary>Optimistic-concurrency token of the sole register write: the association.</summary>
    public int Version { get; set; }

    /// <summary>Backend opening actor (FK RESTRICT).</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}