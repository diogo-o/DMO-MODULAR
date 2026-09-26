namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>mf_contexts</c> table: the MF production context of one Job On.
/// </summary>
/// <remarks>
/// Identical shape to <see cref="CmContextEntity"/>; the frozen Tool type is fixed to <c>MF</c> by
/// the database check, so an MF context can never record a CM or BQ frozen type.
/// </remarks>
public sealed class MfContextEntity
{
    /// <summary>Primary key (Job On-owned context identity; never a Tool identity).</summary>
    public Guid MfId { get; set; }

    /// <summary>The owning production occurrence (FK, ON DELETE RESTRICT).</summary>
    public Guid JobOnId { get; set; }

    /// <summary>The canonical Tool used in this context (FK, ON DELETE RESTRICT).</summary>
    public Guid ToolId { get; set; }

    /// <summary>Frozen Tool type: the database check fixes it to <c>MF</c>.</summary>
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
