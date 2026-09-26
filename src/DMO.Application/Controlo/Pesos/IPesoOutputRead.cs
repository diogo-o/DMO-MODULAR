namespace DMO.Application.Controlo.Pesos;

/// <summary>
/// The Peso output read: the read-only Controlo seam that resolves which production-bound Pesos
/// belong to ONE Job On occurrence (the <c>jobon_id → peso_id</c> relation of the outputs slice).
/// </summary>
/// <remarks>
/// <para>
/// A Peso is an output of a production exactly when its production anchor <c>cm_id</c> is a real
/// <c>cm_contexts</c> row of that occurrence (the P2-T05 anchor rule: <c>peso_id → cm_id → jobon_id</c>,
/// §6.1 — never a stored <c>jobon_id</c> on the Peso and never a reverse array anywhere). The read
/// returns only the related <c>peso_id</c> values: a light packet, never the whole Peso relation and
/// never the whole registry (OWNER CLARIFICATION surgical-queries rule).</para>
/// <para>
/// This narrow read-only seam lives in the Controlo Create application area exactly like the accepted
/// <c>IPesoContextRead</c> seam: the data is Controlo's (<c>pesos</c>), the P2-T04 persistence surface
/// may not carry foreign-vocabulary members (P2-T04 BND7 regression), and the Job On area reads the
/// relation through the owner's contract instead of querying the owner's tables. Read-only: no
/// write, no version bump and no creation (P2-T04 read-transaction rule).</para>
/// </remarks>
public interface IPesoOutputRead
{
    /// <summary>
    /// Lists every <c>peso_id</c> whose production anchor is a real <c>cm_contexts</c> row of
    /// <paramref name="jobOnId"/>, in stable order (creation order, then id). Never a global scan:
    /// the relation is scoped to ONE occurrence.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListByJobOnIdAsync(
        Guid jobOnId,
        CancellationToken cancellationToken);
}