using DMO.Domain.Controlo;

namespace DMO.Domain.ControloComparacao;

/// <summary>
/// One Peso Comparação EVENT: a comparison occurrence tied to one <c>peso_id</c>, with one child
/// record per compared CM subject.
/// </summary>
/// <remarks>
/// <para>
/// Identity model: <c>peso_id</c> → the initial Peso control (unchanged, authoritative frozen
/// control of its moment) and, OPTIONALLY, <c>peso_id</c> → <c>comparacao_id</c> →
/// one-or-more compared CM subjects. A NEW comparison always allocates a NEW
/// <c>comparacao_id</c> (a Peso may have several comparison events over time); the same CM of
/// the same production is reused through the SAME <c>cm_id</c>. A Peso with no comparison event
/// behaves exactly as today; a comparison event with no committed subjects yet has no effect on
/// the initial Peso.</para>
/// <para>
/// Every Comparação write lives in the comparison tables only: nothing here ever mutates the
/// initial Peso's measurements, calculated values, average, approval, PDF or historical meaning
/// (the initial Peso record and its rows are never written by this aggregate).</para>
/// <para>
/// <see cref="ConfirmedAt"/>/<see cref="ConfirmedByUserId"/> are the explicit human
/// confirmation stamp: a comparison event can only be confirmed when every measured CM subject
/// has its (final) individual decision; before that it is not functionally complete.</para>
/// <para>
/// <see cref="Version"/> is the optimistic-concurrency token of the event (subject adds and
/// the confirmation stamp guard and bump it; subject-local writes guard the subject's own
/// version).</para>
/// </remarks>
public sealed record Comparacao(
    ComparacaoId ComparacaoId,
    PesoId PesoId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    Guid? ConfirmedByUserId,
    int Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ComparacaoCmSubject> Subjects);