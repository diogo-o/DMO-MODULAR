namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>cm_contexts</c> table: the CM production context of one Job On.
/// </summary>
/// <remarks>
/// <para>
/// The row carries the direct canonical <see cref="ToolId"/> relation <b>and</b> the frozen triple
/// (<see cref="ToolType"/>, <see cref="ToolReference"/>, <see cref="ToolLot"/>) captured at the
/// moment of the explicit human Tool selection. A later change to the canonical Tool never rewrites
/// it, and no propagation path exists.
/// </para>
/// <para>
/// Deliberately absent: <c>processo</c>, quantity/stock values, operational note, Baffle/calote,
/// measurements, Boquilhas facts, any status column, any machine column and any <c>version</c>
/// column.
/// </para>
/// </remarks>
public sealed class CmContextEntity
{
    /// <summary>Primary key (Job On-owned context identity; never a Tool identity).</summary>
    public Guid CmId { get; set; }

    /// <summary>The owning production occurrence (FK, ON DELETE RESTRICT).</summary>
    public Guid JobOnId { get; set; }

    /// <summary>The canonical Tool used in this context (FK, ON DELETE RESTRICT).</summary>
    public Guid ToolId { get; set; }

    /// <summary>Frozen Tool type: the database check fixes it to <c>CM</c>.</summary>
    public string ToolType { get; set; } = string.Empty;

    /// <summary>Frozen Tool reference as used at this occurrence.</summary>
    public string ToolReference { get; set; } = string.Empty;

    /// <summary>Frozen Tool lot as used at this occurrence.</summary>
    public string ToolLot { get; set; } = string.Empty;

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
