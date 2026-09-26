namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>bq_contexts</c> table: the BQ production context of one Job On.
/// </summary>
/// <remarks>
/// Identical shape to <see cref="CmContextEntity"/>; the frozen Tool type is fixed to <c>BQ</c> by
/// the database check. It is a Job On context and never a Boquilhas aggregate, movement or balance.
/// </remarks>
public sealed class BqContextEntity
{
    /// <summary>Primary key (Job On-owned context identity; never a Tool identity).</summary>
    public Guid BqId { get; set; }

    /// <summary>The owning production occurrence (FK, ON DELETE RESTRICT).</summary>
    public Guid JobOnId { get; set; }

    /// <summary>The canonical Tool used in this context (FK, ON DELETE RESTRICT).</summary>
    public Guid ToolId { get; set; }

    /// <summary>Frozen Tool type: the database check fixes it to <c>BQ</c>.</summary>
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
