namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>boquilha_movement_audit</c> table: one before/after edit-audit row
/// of one movement.
/// </summary>
/// <remarks>
/// Authority: P2-T07 contract §6.4. One row per edit, written in the SAME transaction as the
/// movement UPDATE, with the exact before/after values of every editable field and the backend
/// editor/time. It is <b>audit history, not a quantity event</b> — no balance effect, never
/// rendered as a movement row. Append-only by construction: no UPDATE/DELETE route, repository
/// member or SQL exists for this table (AC-MG3).</remarks>
public sealed class BoquilhaMovementAuditEntity
{
    /// <summary>Primary key (identifies audit rows, never quantity events).</summary>
    public Guid MovementAuditId { get; set; }

    /// <summary>The audited movement (FK RESTRICT).</summary>
    public Guid MovementId { get; set; }

    /// <summary>Backend editor (FK RESTRICT; never client-supplied).</summary>
    public Guid EditedByUserId { get; set; }

    /// <summary>Backend edit timestamp (the authoritative edit time).</summary>
    public DateTimeOffset EditedAt { get; set; }

    /// <summary>Quantity before the edit (always positive).</summary>
    public int BeforeQuantity { get; set; }

    /// <summary>Quantity after the edit (always positive).</summary>
    public int AfterQuantity { get; set; }

    /// <summary>Business date before the edit.</summary>
    public DateOnly BeforeBusinessDate { get; set; }

    /// <summary>Business date after the edit.</summary>
    public DateOnly AfterBusinessDate { get; set; }

    /// <summary>Machine before the edit (NULL = absent).</summary>
    public string? BeforeMachine { get; set; }

    /// <summary>Machine after the edit (NULL = absent).</summary>
    public string? AfterMachine { get; set; }

    /// <summary>Repairer before the edit (NULL = absent).</summary>
    public Guid? BeforeRepairerId { get; set; }

    /// <summary>Repairer after the edit (NULL = absent).</summary>
    public Guid? AfterRepairerId { get; set; }

    /// <summary>Observations before the edit (NULL = absent).</summary>
    public string? BeforeObservations { get; set; }

    /// <summary>Observations after the edit (NULL = absent).</summary>
    public string? AfterObservations { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}