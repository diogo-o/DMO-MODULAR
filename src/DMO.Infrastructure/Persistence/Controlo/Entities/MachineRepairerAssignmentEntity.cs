namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>machine_repairer_assignments</c> table: one independent current
/// assignment per machine.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.4. <see cref="Machine"/> is a closed code value (CHECK
/// <c>B1..C3</c>) and <b>UNIQUE</b> (one row per machine). There is no machine registry, no
/// <c>machine_id</c> scheme and no line/group column; changing one machine never touches another
/// (MAC2–MAC4). No assignment-history table exists (Q-HIST).
/// </remarks>
public sealed class MachineRepairerAssignmentEntity
{
    /// <summary>Primary key (backend-allocated).</summary>
    public Guid MachineRepairerAssignmentId { get; set; }

    /// <summary>The machine code (B1/B2/B3/C1/C2/C3; closed CHECK, UNIQUE).</summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>The assigned repairer (FK → repairers RESTRICT).</summary>
    public Guid RepairerId { get; set; }

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}