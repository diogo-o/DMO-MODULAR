namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>repairers</c> table: the canonical repairer register entry.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.3. <see cref="Name"/> is the <b>only</b> business field
/// (trimmed non-blank CHECK); no address/email/phone/supplier-code/tax/contact-person field exists
/// (§10.1, REP4/AC-D4). No unique name (Q-REP), no status/active column (§10.3), no delete path.
/// </remarks>
public sealed class RepairerEntity
{
    /// <summary>Primary key (backend-allocated).</summary>
    public Guid RepairerId { get; set; }

    /// <summary>The repairer name (the only required business data).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}