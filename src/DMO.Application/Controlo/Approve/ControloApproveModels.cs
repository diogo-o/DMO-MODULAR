using DMO.Domain.Controlo;

namespace DMO.Application.Controlo.Approve;

// ---------------------------------------------------------------------------------------------
// Queries (§15.1/§15.2 — exact)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The pending/review list query (P2-T06 contract §15.1, exact). Every filter is a backend SQL
/// predicate over backend-reported reviewable facts; paging is 1-based with
/// <c>1 &lt;= PageSize &lt;= 100</c>; ordering is <c>submitted_at ASC</c> with the
/// <c>peso_id ASC</c> tie-break.
/// </summary>
public sealed record PendingListQuery(
    string? Reference,
    string? ProductionNumber,
    string? Machine,
    string? Processo,
    string? ToolReference,
    DateTimeOffset? SubmittedFrom,
    DateTimeOffset? SubmittedTo,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// The local Histórico list query (P2-T06 contract §15.2, exact). The <see cref="ReviewState"/>
/// filter uses the closed vocabulary <c>submetido | pendente | aprovado | nao_aprovado</c> — a
/// <b>filter vocabulary, not a status</b> (<c>submetido</c> and <c>pendente</c> both select the
/// reviewable subset <c>submitted_at IS NOT NULL AND status = 'pendente'</c>); ordering is
/// <c>submitted_at DESC</c> with the <c>peso_id DESC</c> tie-break.
/// </summary>
public sealed record HistoryListQuery(
    string? ReviewState,
    string? Reference,
    string? ProductionNumber,
    string? Machine,
    string? Processo,
    string? ToolReference,
    DateTimeOffset? SubmittedFrom,
    DateTimeOffset? SubmittedTo,
    DateTimeOffset? DecidedFrom,
    DateTimeOffset? DecidedTo,
    Guid? DecidedByUserId,
    string? Decision,
    int Page = 1,
    int PageSize = 50);

// ---------------------------------------------------------------------------------------------
// Repository read carrier (§8.2)
// ---------------------------------------------------------------------------------------------

/// <summary>One backend-reported review list row (the <c>PesoReviewRow</c> carrier of §8.2).</summary>
/// <remarks>
/// All facts are backend-reported: the exact persisted <c>peso_id</c>, the observed version, the
/// current status token, the anchor facts (<c>cm_id</c> XOR <c>tool_id</c>, the exclusive-anchor
/// CHECK), the traversal production facts (reference/production number/machine through
/// <c>cm_id → job_ons</c>, NULL while pending), the frozen/pending Tool reference and the
/// processo (through the anchor), the submission handoff facts, the backend-counted measurement
/// row count, and the decision-trail facts (whether the record was ever decided, its last
/// decision summary and its decision count). Nothing is reconstructed client-side.
/// </remarks>
public sealed record PesoReviewRow(
    Guid PesoId,
    int Version,
    string Status,
    Guid? CmId,
    Guid? ToolId,
    string? Reference,
    string? ProductionNumber,
    string? Machine,
    string? ToolReference,
    string? Processo,
    DateTimeOffset? SubmittedAt,
    Guid? SubmittedByUserId,
    int RowCount,
    bool HasDecisionTrail,
    DecisionSummary? LastDecision,
    int DecisionCount);

/// <summary>The last decision summary of a Peso (trail facts, §15.2 row shape).</summary>
public sealed record DecisionSummary(
    string Decision,
    Guid DecidedByUserId,
    DateTimeOffset DecidedAt,
    string? Reason);

// ---------------------------------------------------------------------------------------------
// Application read models (§15.1/§15.2 — exact)
// ---------------------------------------------------------------------------------------------

/// <summary>One pending/review list row (P2-T06 contract §15.1, exact).</summary>
public sealed record PendingItemReadModel(
    Guid PesoId,
    int Version,
    string Status,
    Guid? CmId,
    Guid? ToolId,
    string? Reference,
    string? ProductionNumber,
    string? Machine,
    string? ToolReference,
    string? Processo,
    DateTimeOffset SubmittedAt,
    Guid SubmittedByUserId,
    int RowCount,
    bool HasDecisionTrail);

/// <summary>One local Histórico row (P2-T06 contract §15.2, exact).</summary>
public sealed record HistoryItemReadModel(
    Guid PesoId,
    int Version,
    string Status,
    Guid? CmId,
    Guid? ToolId,
    string? Reference,
    string? ProductionNumber,
    string? Machine,
    string? ToolReference,
    string? Processo,
    DateTimeOffset? SubmittedAt,
    Guid? SubmittedByUserId,
    DecisionSummary? LastDecision,
    int DecisionCount);

/// <summary>One decision-trail item (P2-T06 contract §15.3, exact).</summary>
public sealed record DecisionItemReadModel(
    Guid PesoReviewDecisionId,
    string Decision,
    Guid DecidedByUserId,
    DateTimeOffset DecidedAt,
    string? Reason,
    string PriorStatus,
    int PesoVersionAtDecision);

// ---------------------------------------------------------------------------------------------
// Decision commands (§9.1/§11.2 — identity/version/reason only, by construction)
// ---------------------------------------------------------------------------------------------

/// <summary>Human approve carrier: identity + observed version only (AC-D1/D2).</summary>
public sealed record ApprovePesoCommand(Guid PesoId, int ExpectedVersion);

/// <summary>Human reject carrier: identity + observed version + mandatory non-blank reason.</summary>
public sealed record RejectPesoCommand(Guid PesoId, int ExpectedVersion, string Reason);

/// <summary>Human reopen carrier: identity + observed version + mandatory non-blank reason.</summary>
public sealed record ReopenPesoCommand(Guid PesoId, int ExpectedVersion, string Reason);

// ---------------------------------------------------------------------------------------------
// Result union (§12.1 — exact)
// ---------------------------------------------------------------------------------------------

/// <summary>The closed P2-T06 result union (§12.1).</summary>
public abstract record ReviewResult
{
    public sealed record PendingFound(IReadOnlyList<PendingItemReadModel> Rows, int Total) : ReviewResult;
    public sealed record HistoryFound(IReadOnlyList<HistoryItemReadModel> Rows, int Total) : ReviewResult;
    public sealed record ReviewSheet(ReviewSheetReadModel Sheet) : ReviewResult;
    public sealed record DecisionsFound(IReadOnlyList<DecisionItemReadModel> Decisions) : ReviewResult;
    public sealed record Approved(Guid PesoId, int Version, DateTimeOffset DecidedAt) : ReviewResult;
    public sealed record Rejected(Guid PesoId, int Version, DateTimeOffset DecidedAt) : ReviewResult;
    public sealed record Reopened(Guid PesoId, int Version, DateTimeOffset DecidedAt) : ReviewResult;
    public sealed record ValidationFailed(IReadOnlyList<string> Errors) : ReviewResult;
    public sealed record NotFound(Guid PesoId) : ReviewResult;
    public sealed record Refused(ReviewRefusalReason Reason, string Message) : ReviewResult;
}

/// <summary>The closed persisted-state refusal reasons of the decision core (§12.1).</summary>
public enum ReviewRefusalReason
{
    /// <summary>409 <c>stale-version</c> — the observed <c>pesos.version</c> is no longer current; nothing written.</summary>
    StaleVersion,

    /// <summary>409 <c>not-reviewable</c> — the record is outside the allowed states (a draft).</summary>
    NotReviewable,

    /// <summary>409 <c>already-decided</c> — the record is already decided; no second event and no flip.</summary>
    AlreadyDecided,
}

/// <summary>The closed validation code set of the P2-T06 surface (§12.2/§9.2).</summary>
public static class ControloApproveValidationErrors
{
    /// <summary>Reject without a non-blank reason.</summary>
    public const string RejectReasonRequired = "REJECT_REASON_REQUIRED";

    /// <summary>Reopen without a non-blank reason.</summary>
    public const string ReopenReasonRequired = "REOPEN_REASON_REQUIRED";

    /// <summary>Unknown/ill-formed filter values (incl. page/pageSize out of bounds).</summary>
    public const string FilterInvalid = "FILTER_INVALID";

    /// <summary>Defensive: a review sheet loaded for a non-approvable state cannot be decided.</summary>
    public const string ApprovalNotAvailable = "APPROVAL_NOT_AVAILABLE";
}