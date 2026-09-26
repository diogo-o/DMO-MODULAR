namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>tool_machines</c> table: one row per machine the Tool works on.
/// </summary>
/// <remarks>
/// The machine is a settled code value (<c>B1</c>, <c>B2</c>, <c>B3</c>, <c>C1</c>, <c>C2</c>,
/// <c>C3</c>), never a registry foreign key: there is no <c>machines</c> table, no <c>machine_id</c>,
/// no line/group column and no <c>is_primary</c> flag.
/// </remarks>
public sealed class ToolMachineEntity
{
    /// <summary>Primary key.</summary>
    public Guid ToolMachineId { get; set; }

    /// <summary>The owning canonical Tool (FK, ON DELETE RESTRICT).</summary>
    public Guid ToolId { get; set; }

    /// <summary>Related Tool.</summary>
    public ToolEntity? Tool { get; set; }

    /// <summary>One settled operational machine code.</summary>
    public string Machine { get; set; } = string.Empty;
}
