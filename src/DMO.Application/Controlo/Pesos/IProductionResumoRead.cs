using DMO.Domain.Tools;

namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// The production summary (Resumo da produção) read model: the Controlo entry projection of ONE
/// concrete production occurrence.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T05 contract §31.1 (OWNER CLARIFICATION — "Controlo receives the production through
/// the Resumo"; the Resumo is the entry point carrying the production context that populates the
/// Peso surface; <c>resumo_id → jobon_id</c>; the <c>resumo_id</c> record itself remains an
/// unimplemented P2-T05 handoff remainder — no table, column or route for a Resumo RECORD exists or
/// is created). This read model is the projection the entry surface consumes: it resolves the
/// production facts (reference, production number, machine, production date) and the CM context
/// (<c>cm_id</c>, direct canonical <c>tool_id</c>, frozen triple and the live Tool facts needed by
/// the Peso surface — <c>processo</c> consumed through <c>tool_id</c>, never re-entered and never
/// duplicated on the Peso).</para>
/// <para>
/// Light-packet rule (OWNER CLARIFICATION decision 7): the carrier contains exactly the facts this
/// surface needs. The live Tool summary carries reference/lot/processo/quantity only — the
/// compatible-machine rows are deliberately NOT loaded here (they are not rendered by the entry
/// surface) and no Peso/Boquilhas/output list of the production is loaded: history is only loaded
/// when the operator asks for it.</para>
/// <para>
/// This is a <b>read-only projection</b> over the real P2-T04 rows: it never writes, never creates
/// a production aggregate and never copies Job On data into a second authority (AC-R2: every Job On
/// production fact still comes from the real <c>job_ons</c>/<c>cm_contexts</c>/<c>tools</c> rows
/// through the single persistence context). The Job On remains the planning centre and the only
/// written authority for these facts.</para>
/// </remarks>
public sealed record ProductionResumoReadModel(
    Guid JobOnId,
    string Reference,
    string ProductionNumber,
    string Machine,
    DateOnly? ProductionDate,
    int Version,
    CmResumoProjection? Cm);

/// <summary>
/// The CM context of the Resumo: the real <c>cm_id</c>, its direct canonical <c>tool_id</c>, the
/// P2-T04 frozen triple and the live Tool facts needed by the Peso entry (reference, lot, processo,
/// quantity). <c>null</c> when the production has no CM context yet (missing-context recovery of
/// §4.3 stays the operator's explicit path).
/// </summary>
/// <remarks>
/// The frozen triple is never refreshed from the live facts and the live facts are never copied
/// onto any Peso: both keep their established P2-T04/P2-T05 semantics (§6.1). The frozen <c>type</c>
/// is the context's own <c>tool_type</c> (fixed to <c>CM</c> by the <c>cm_contexts</c> CHECK), so
/// the surface renders the Tool type without a second Tool read.
/// </remarks>
public sealed record CmResumoProjection(
    Guid CmId,
    Guid ToolId,
    string FrozenToolType,
    string FrozenToolReference,
    string FrozenToolLot,
    string LiveToolReference,
    string LiveToolLot,
    Processo? Processo,
    int? Quantity);

/// <summary>
/// The Resumo da produção read: the single surgical projection of one production occurrence
/// (jobon_id → production facts + CM context), or <c>null</c> when the occurrence does not exist.
/// </summary>
/// <remarks>
/// <para>
/// Authority: P2-T05 contract §31.1 and the OWNER CLARIFICATION surgical-queries / light-packets
/// rule (decision 7). The read is keyed by the real <c>jobon_id</c> and is resolved by ONE
/// context-specific statement over <c>job_ons</c> LEFT JOIN <c>cm_contexts</c> LEFT JOIN <c>tools</c>
/// — no global scan, no loading of every Job On/Tool/Peso to filter later, no second production
/// lookup model.</para>
/// <para>
/// Layering note (mirrors the accepted <c>IPesoContextRead</c> seam): the P2-T04 Job On application
/// contract offers the full ficha (all context slots + full live projections) but no narrower
/// production entry projection, and the closed P2-T04 persistence surface may not carry
/// Controlo-vocabulary members (P2-T04 BND7 regression). This narrow read-only seam therefore lives
/// in the Controlo Create application area, exactly like the accepted cm-anchor traversal read.</para>
/// </remarks>
public interface IProductionResumoRead
{
    /// <summary>Resolves the Resumo da produção of one occurrence, or <c>null</c> (read-only).</summary>
    Task<ProductionResumoReadModel?> GetResumoAsync(
        Guid jobOnId,
        CancellationToken cancellationToken);
}