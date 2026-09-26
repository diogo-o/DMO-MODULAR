using DMO.Application.Controlo.Pesos;
using DMO.Domain.Controlo;

namespace DMO.Application.Controlo.Approve;

/// <summary>
/// The review-sheet read model of P2-T06 (contract §15.3/§16): approval-only presentation facts
/// composed <b>around</b> the shared P2-T05 <c>PesoSheetReadModel</c> — the same C# record type,
/// never copied, never forked and never altered (RD1/RD3).
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §16 (shared Peso read-model reuse — NO FORK). Composition, not
/// replacement: the Peso facts model is never extended-with-semantics or replaced; the wrapper
/// adds only the approval-only facts — the decision trail and the review availability with its
/// disabled reasons (DecisionBar contract). No member of <c>PesoSheetReadModel</c> is redefined or
/// hidden by P2-T06 types.</remarks>
public sealed record ReviewSheetReadModel(
    PesoSheetReadModel Peso,
    IReadOnlyList<DecisionItemReadModel> Decisions,
    ReviewAvailability Availability);

/// <summary>
/// The review availability of one Peso (contract §15.3): which decisions are possible, with the
/// associated disabled reasons shown by the DecisionBar.
/// </summary>
/// <remarks>
/// <c>CanApprove</c>/<c>CanReject</c> are true only for the reviewable predicate
/// (<c>submitted_at IS NOT NULL</c> AND <c>status = 'pendente'</c>); <c>CanReopen</c> is true for
/// reviewable or decided records (a draft — never submitted — is Create's object).</remarks>
public sealed record ReviewAvailability(
    bool CanApprove,
    string? ApproveDisabledReason,
    bool CanReject,
    string? RejectDisabledReason,
    bool CanReopen,
    string? ReopenDisabledReason)
{
    /// <summary>A record in the review circuit that has not been decided yet.</summary>
    public static ReviewAvailability Reviewable() =>
        new(CanApprove: true, null, CanReject: true, null, CanReopen: true, null);

    /// <summary>A record in the review circuit that was already decided (actions: reopen only).</summary>
    public static ReviewAvailability Decided() =>
        new(
            CanApprove: false, "Este Peso já foi decidido; não pode ser aprovado ou rejeitado novamente.",
            CanReject: false, "Este Peso já foi decidido; não pode ser aprovado ou rejeitado novamente.",
            CanReopen: true, null);

    /// <summary>A draft that never entered the review circuit (Create's object; nothing to decide).</summary>
    public static ReviewAvailability Draft() =>
        new(
            CanApprove: false, "Este Peso ainda não foi submetido para aprovação.",
            CanReject: false, "Este Peso ainda não foi submetido para aprovação.",
            CanReopen: false, "Este Peso ainda não foi submetido para aprovação.");
}