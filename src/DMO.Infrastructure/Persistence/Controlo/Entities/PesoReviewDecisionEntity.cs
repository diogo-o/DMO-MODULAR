namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>peso_review_decisions</c> table: one immutable Peso review
/// decision event (approve / reject / reopen) on one <c>peso_id</c>.
/// </summary>
/// <remarks>
/// Authority: P2-T06 contract §6.1. The event carries the Peso Identity Rule's audit facts —
/// who (<see cref="DecidedByUserId"/>), when (<see cref="DecidedAt"/>) and which record version
/// (<see cref="PesoVersionAtDecision"/>) — plus <see cref="PriorStatus"/> and the optional
/// reason. Rows are <b>append-only and immutable after COMMIT</b>: no update path and no
/// soft-delete column exist. The row stores <b>no</b> Peso operational fact (no water
/// temperature, no density, no rows, no results) — the record remains the single source of
/// truth.</remarks>
public sealed class PesoReviewDecisionEntity
{
    /// <summary>Primary key (backend-allocated inside the decision transaction).</summary>
    public Guid PesoReviewDecisionId { get; set; }

    /// <summary>The decided Peso (FK → pesos RESTRICT; the decision binds the SAME record).</summary>
    public Guid PesoId { get; set; }

    /// <summary>Closed decision token: <c>aprovado</c> | <c>nao_aprovado</c> | <c>reaberto</c>.</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>Backend-authored actor (FK → users RESTRICT; never client-supplied).</summary>
    public Guid DecidedByUserId { get; set; }

    /// <summary>Backend-clock decision instant; the authoritative decision timestamp.</summary>
    public DateTimeOffset DecidedAt { get; set; }

    /// <summary>Optional reason: required for reject and reopen, always NULL for approve (CHECK).</summary>
    public string? Reason { get; set; }

    /// <summary>The Peso status BEFORE this transition (trail readability).</summary>
    public string PriorStatus { get; set; } = string.Empty;

    /// <summary>The <c>pesos.version</c> observed by the actor at decision time (&gt;= 1).</summary>
    public int PesoVersionAtDecision { get; set; }

    /// <summary>Creation instant (UTC, default <c>now()</c>).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}