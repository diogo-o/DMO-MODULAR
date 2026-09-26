using DMO.Application.Controlo.Approve;
using DMO.Domain.Controlo;

namespace DMO.Application.Repositories;

/// <summary>
/// The P2-T06 review repository contract (contract §8.2, exact).
/// </summary>
/// <remarks>
/// <para>
/// Reads load backend-reported review facts; the decision write applies the status transition on
/// the existing <c>pesos</c> row (version-guarded) <b>and</b> the append-only decision row in ONE
/// transaction (§10) — all-or-nothing. Binding rules are the accepted ones (P2-T04 §12.2 /
/// P2-T05 §20.2): <c>CancellationToken</c> mandatory last; writes open their own transaction;
/// private static projection mapping; PostgreSQL constraint violations map onto typed failures
/// (SQLSTATE <c>23514</c> on the reason CHECK → the same validator token, never a 500); the
/// repository owns no domain rule.</para>
/// <para>
/// The Peso row <b>read</b> of the review sheet never comes from this repository: it uses the
/// CLOSED <c>IPesoRepository.GetByIdAsync</c> through the shared application service
/// (<c>IControloCreateService.GetAsync</c>). The decision <b>update</b> of the <c>pesos</c>
/// status/submitted columns is a P2-T06 write path, authorized by P2-T05 §5.7/§29
/// ("P2-T06 writes <c>'aprovado'</c>/<c>'nao_aprovado'</c> on the same row"; "…reopen is
/// P2-T06").</para>
/// </remarks>
public interface IPesoReviewRepository
{
    /// <summary>
    /// The pending list — the reviewable predicate (<c>submitted_at IS NOT NULL</c> AND
    /// <c>status = 'pendente'</c>), filters applied in SQL (§15.2), deterministic ordering, one
    /// page.
    /// </summary>
    Task<IReadOnlyList<PesoReviewRow>> GetPendingAsync(
        PendingListQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Histórico de Pesos — records visible to Approve (submitted or with at least one decision
    /// event; §15.2), filters applied in SQL, one page.
    /// </summary>
    Task<IReadOnlyList<PesoReviewRow>> GetHistoryAsync(
        HistoryListQuery query,
        CancellationToken cancellationToken);

    /// <summary>The decision trail of one <c>peso_id</c>, ordered by <c>decided_at ASC</c>.</summary>
    Task<IReadOnlyList<PesoReviewDecision>> GetDecisionsAsync(
        Guid pesoId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies one decision event atomically: the status transition on the existing <c>pesos</c>
    /// row (version-guarded) + the append-only decision row, in ONE transaction (§10). The state
    /// preconditions (exists/version/reviewable) are refused by the service; the version guard
    /// here is the racing backstop (every decision transition bumps the version).
    /// </summary>
    Task<PesoReviewDecision> DecisionAsync(
        PesoReviewDecision decision,
        int expectedPesoVersion,
        CancellationToken cancellationToken);
}