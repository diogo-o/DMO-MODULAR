namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>tools</c> table: the canonical Tool registry row.
/// </summary>
/// <remarks>
/// <para>
/// The identity tuple is (<see cref="ToolType"/>, <see cref="Reference"/>, <see cref="Lot"/>): a
/// different lot is operationally a different Tool. <see cref="Processo"/> and <see cref="Quantity"/>
/// are nullable Tool-owned facts: <c>null</c> means not applicable/not established.
/// </para>
/// <para>
/// Deliberately absent: any version (nothing updates a Tool in P2-T04), technical condition,
/// operational note, utilisation, drawing/revision, change-request columns, a classification table
/// and every reverse-ID array.
/// </para>
/// </remarks>
public sealed class ToolEntity
{
    /// <summary>Primary key (application-allocated canonical identity).</summary>
    public Guid ToolId { get; set; }

    /// <summary>Tool family token: <c>CM</c>, <c>MF</c> or <c>BQ</c>.</summary>
    public string ToolType { get; set; } = string.Empty;

    /// <summary>Tool reference (identity fact, non-blank).</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Tool lot (identity fact, non-blank).</summary>
    public string Lot { get; set; } = string.Empty;

    /// <summary><c>NNPB</c>, <c>PS</c> or <c>null</c> when not applicable.</summary>
    public string? Processo { get; set; }

    /// <summary>Canonical Tool total where canonical; <c>null</c> when not established.</summary>
    public int? Quantity { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The compatible-machine rows of this Tool (one row per compatible machine).</summary>
    public ICollection<ToolMachineEntity> Machines { get; set; } = [];
}
