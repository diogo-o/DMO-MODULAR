using DMO.Domain.ControloComparacao;

namespace DMO.Application.Repositories;

/// <summary>
/// The single Peso Comparação repository contract: the optional comparison aggregate over its own
/// tables, with the compared CM subjects keyed by the natural <c>comparacao_id + cm_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reads load the comparison with its full subject/row set. Writes open their own
/// transaction and map PostgreSQL constraint violations onto the typed application failures
/// (<c>ComparacaoPersistenceException</c>): RESTRICT FK violations → <c>DependencyExists</c>,
/// the reason/decision-state CHECK backstops → their typed reasons, the
/// (comparacao_id, cm_id) primary-key duplication → <c>DuplicateCmSelected</c>, and the
/// row-result CHECKs → <c>ResultNonPositive</c>. Every write leaves the INITIAL Peso table
/// untouched: the comparison aggregate never mutates the Peso's measurements, results, status,
/// approval or version.</para>
/// <para>
/// Optimistic concurrency follows the accepted Peso convention: the event <c>version</c> guards
/// subject-adds and the confirmation stamp; the subject <c>version</c> guards measurement
/// recording and the decision. The subject identity is the natural relationship
/// <c>(comparacao_id, cm_id)</c> — the exact frozen CM identity of the production context; no
/// surrogate subject identity exists.</para>
/// </remarks>
public interface IComparacaoRepository
{
    /// <summary>Reads one comparison event with its full subject/row set, or <c>null</c>.</summary>
    Task<Comparacao?> GetByIdAsync(Guid comparacaoId, CancellationToken cancellationToken);

    /// <summary>Reads the comparison event of one Peso, or <c>null</c> when the Peso has none
    /// (a Peso may have several events; the caller gets the newest by creation).</summary>
    Task<Comparacao?> GetByPesoIdAsync(Guid pesoId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the comparison event HEADER only (version = 1); the initial Peso row is not touched.
    /// </summary>
    Task<Comparacao> StartedAsync(Comparacao comparacao, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts one compared CM subject (natural key <c>comparacao_id + cm_id</c>) and bumps the
    /// event version exactly once, in ONE transaction.
    /// </summary>
    Task<Comparacao> SubjectAddedAsync(
        ComparacaoCmSubject subject,
        int expectedComparacaoVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the whole measurement-row set of one subject and bumps the subject version exactly
    /// once, in ONE transaction. Refused when a decision already exists (the final decision binds
    /// the recorded rows).
    /// </summary>
    Task<Comparacao> MeasurementsRecordedAsync(
        ComparacaoCmSubject subject,
        int expectedSubjectVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the FINAL individual per-CM decision state to the subject (decision, justification,
    /// actor/time; subject version += 1) in ONE transaction. A second decision of the same subject
    /// is refused.
    /// </summary>
    Task<Comparacao> DecidedAsync(
        ComparacaoCmSubject subject,
        int expectedSubjectVersion,
        CancellationToken cancellationToken);

    /// <summary>Applies the explicit confirmation stamp (confirmed_at/confirmed_by + event
    /// version += 1) in ONE transaction.</summary>
    Task<Comparacao> ConfirmedAsync(
        Comparacao comparacao,
        int expectedComparacaoVersion,
        CancellationToken cancellationToken);
}