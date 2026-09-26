namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// One CM-context projection of the P2-T05 anchor traversal: the accepted P2-T04 <c>cm_id</c> with
/// its owning occurrence and its direct canonical Tool (frozen triple).
/// </summary>
/// <remarks>
/// This is a <b>read-only projection</b> of the P2-T04 <c>cm_contexts</c> row: it resolves exactly
/// the contracted traversal <c>cm_id → tool_id → jobon_id</c> (§6.1, §7.2, §26.3) and never writes,
/// never recreates context and never duplicates the Job On model (AC-R2: every Job On production
/// fact still comes from the Job On application contract). No second CM identity is ever minted.
/// </remarks>
public sealed record CmContextProjection(
    Guid CmId,
    Guid JobOnId,
    Guid ToolId,
    string FrozenToolType,
    string FrozenToolReference,
    string FrozenToolLot);

/// <summary>
/// The P2-T05 anchor-traversal read: resolves a production anchor <c>cm_id</c> to the real P2-T04
/// <c>cm_contexts</c> row (its <c>jobon_id</c>, <c>tool_id</c> and frozen triple), or <c>null</c>.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T05 contract §6.1 ("reached by traversal <c>peso_id → cm_id → jobon_id</c> (+
/// <c>tool_id</c> for processo)"), §7.2 ("<c>cmId → tool_id → processo</c> feeds the glass-density
/// resolution") and §26.3 (<c>PesoContextProjection</c> "composed from <c>cm_contexts</c> +
/// <c>tools</c>"). The shipped Job On application contracts are keyed by <c>jobon_id</c> (ficha) or
/// <c>tool_id</c> (the accepted additive candidates read); neither can resolve a bare <c>cm_id</c>,
/// so P2-T05 exposes this narrow read-only seam inside its own application area — the single
/// literal-shape reading of the composition rule, disclosed in the implementation response.</para>
/// <para>
/// The read performs no write, no version bump and no creation (P2-T04 read-transaction rule), and
/// it resolves only the contracted traversal facts — reference/production number/machine still come
/// from <c>IJobOnService</c>.</para>
/// </remarks>
public interface IPesoContextRead
{
    /// <summary>Resolves the real <c>cm_contexts</c> row behind the anchor, or <c>null</c>.</summary>
    Task<CmContextProjection?> GetCmContextAsync(Guid cmId, CancellationToken cancellationToken);
}