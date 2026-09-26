namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>job_ons</c> table: one production occurrence.
/// </summary>
/// <remarks>
/// <para>
/// Captured facts are exactly <see cref="Reference"/>, <see cref="ProductionNumber"/>,
/// <see cref="Machine"/> and the optional <see cref="ProductionDate"/> — plus the system facts
/// <see cref="JobOnId"/>, <see cref="CopiedFromJobOnId"/>, <see cref="Version"/> and the timestamps.
/// </para>
/// <para>
/// Deliberately absent — each a settled authority decision: <c>processo</c>, any status/state
/// column, <c>production_id</c>, <c>job_on_revision_id</c>, quantity snapshots, Pegamentos facts,
/// drop/stoppage/weight/notes blocks and every reverse-ID array.
/// </para>
/// <para>
/// <see cref="Version"/> is the optimistic-concurrency token. Context rows carry no version: the
/// Job On version protects every context write in the same transaction.
/// </para>
/// </remarks>
public sealed class JobOnEntity
{
    /// <summary>Primary key (application-allocated production-occurrence identity).</summary>
    public Guid JobOnId { get; set; }

    /// <summary>Production reference (non-blank).</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Production number as entered, stored verbatim (text).</summary>
    public string ProductionNumber { get; set; } = string.Empty;

    /// <summary>One settled operational machine code.</summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>
    /// The planned production date used as the edit/delete threshold; planning data, never proof
    /// that production occurred.
    /// </summary>
    public DateOnly? ProductionDate { get; set; }

    /// <summary>Explicit duplication-source lineage (self-FK, ON DELETE RESTRICT); never a revision.</summary>
    public Guid? CopiedFromJobOnId { get; set; }

    /// <summary>Optimistic-concurrency token; starts at 1.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
