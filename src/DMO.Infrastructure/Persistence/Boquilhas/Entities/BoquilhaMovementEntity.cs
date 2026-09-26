namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>boquilha_movements</c> table: one quantity movement/event on one
/// Boquilhas register.
/// </summary>
/// <remarks>
/// Authority: P2-T07 OWNER CLARIFICATION + the preserved §6.3 shapes. The row is <b>the single
/// quantity event</b>: editing replaces the current values of the same row (version-guarded) and
/// inserts one audit row — no second movement row is ever created. The type CHECK fixes the closed
/// set <c>saida|entrada|entrada_sem_reparacao</c> (<c>Editar</c> is an action, never a type;
/// <c>inicio</c>/<c>irreparavel</c> are superseded); quantities are positive whole units;
/// <c>recorded_at</c> is the immutable backend receipt timestamp written exactly once; a Saída
/// requires <c>machine</c> + <c>repairer_id</c> (CHECK) and both are historically preserved. An
/// Entrada sem reparação is a NORMAL movement: it returns quantity from repair and records the
/// returned boquilhas were not repaired — it never mutates the canonical Tool row/state. There is
/// no expected/excess fact pair (the excecional computation is superseded) and no delete path.
/// </remarks>
public sealed class BoquilhaMovementEntity
{
    /// <summary>Primary key (backend-allocated inside the append transaction).</summary>
    public Guid MovementId { get; set; }

    /// <summary>The owning register (FK RESTRICT).</summary>
    public Guid BoquilhasId { get; set; }

    /// <summary>The closed-set type token: <c>saida</c> | <c>entrada</c> | <c>entrada_sem_reparacao</c>.</summary>
    public string MovementType { get; set; } = string.Empty;

    /// <summary>Whole-unit BQ count; always positive (CHECK).</summary>
    public int Quantity { get; set; }

    /// <summary>Operator-editable operational date (may be LATER than the production end date —
    /// movements after production has ended are valid and keep the production as the historical
    /// context; never reorders the ledger).</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Immutable system receipt timestamp (never rewritten by any later statement).</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Backend recording actor (FK RESTRICT; unchanged by later edits).</summary>
    public Guid RecordedByUserId { get; set; }

    /// <summary>Line context "where recorded"; required on Saída (CHECK).</summary>
    public string? Machine { get; set; }

    /// <summary>The repairer used for the movement; required on Saída (CHECK); historically preserved.</summary>
    public Guid? RepairerId { get; set; }

    /// <summary>Motive/detail/observations "where recorded" (NULL or non-blank).</summary>
    public string? Observations { get; set; }

    /// <summary>Optimistic-concurrency token of the movement (default 1; +1 per committed edit).</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC; edited rows only).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}