namespace DMO.Application.Controlo.Approve;

/// <summary>
/// The P2-T06 Controlo Approve service contract (contract §9.1, exact): the review/decision core
/// over the SAME persisted <c>peso_id</c> created and submitted by P2-T05.
/// </summary>
/// <remarks>
/// <para>
/// The service composes: <c>IControloCreateService.GetAsync</c> (the SHARED application read →
/// <c>Found(PesoSheetReadModel)</c> — the only source of Peso facts, RD1), <c>IPesoReviewRepository</c>
/// (list queries + the decision write) and <c>ICurrentAccountContext</c> (the backend actor).
/// The Web layer never queries the database directly; the service never touches P2-T04/P2-T05
/// repositories for writes; decision routes accept no Peso facts, no warnings, no calculation
/// output and no actor/time (AC-D2/D4).</para>
/// <para>
/// Approve/reject/reopen are HUMAN-only transitions on the same row with backend attribution
/// (actor/time), version-guarded (stale → 409 <c>stale-version</c>, nothing written) and atomic
/// (transition + decision event all-or-nothing). P2-T06 is never an editor of measurement facts
/// (AC-O5/R1/R2).</para>
/// </remarks>
public interface IControloApproveService
{
    /// <summary>The pending/review list (the reviewable predicate query — a query, not a queue).</summary>
    Task<ReviewResult> GetPendingAsync(PendingListQuery query, CancellationToken cancellationToken);

    /// <summary>The local Histórico de Pesos list (filters applied by the backend).</summary>
    Task<ReviewResult> GetHistoryAsync(HistoryListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// The exact-record review sheet: the shared PesoSheetReadModel + the decision trail + the
    /// review availability (§15.3/§16). Read-only; never writes and never bumps.
    /// </summary>
    Task<ReviewResult> GetReviewSheetAsync(Guid pesoId, CancellationToken cancellationToken);

    /// <summary>The complete decision trail of one <c>peso_id</c>, ordered <c>decided_at ASC</c>.</summary>
    Task<ReviewResult> GetDecisionsAsync(Guid pesoId, CancellationToken cancellationToken);

    /// <summary>Human approve on the SAME <c>peso_id</c> (identity + version only).</summary>
    Task<ReviewResult> ApproveAsync(ApprovePesoCommand command, CancellationToken cancellationToken);

    /// <summary>Human reject with a mandatory non-blank reason on the SAME <c>peso_id</c>.</summary>
    Task<ReviewResult> RejectAsync(RejectPesoCommand command, CancellationToken cancellationToken);

    /// <summary>Human reopen with a mandatory non-blank reason on the SAME <c>peso_id</c>.</summary>
    Task<ReviewResult> ReopenAsync(ReopenPesoCommand command, CancellationToken cancellationToken);
}