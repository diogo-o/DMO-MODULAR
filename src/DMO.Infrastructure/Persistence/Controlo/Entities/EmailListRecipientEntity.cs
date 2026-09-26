namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>email_list_recipients</c> table: one recipient address of one
/// named list.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.7. Each row is exactly one <c>address</c> (trimmed non-blank
/// CHECK; minimal shape validated by the application). <c>UNIQUE (email_list_id, address)</c>: one
/// address once per list; the same address may appear in several lists. No <c>version</c>: the
/// parent list's version protects the set (parent-protects-child precedent). No display
/// name/position (S20).
/// </remarks>
public sealed class EmailListRecipientEntity
{
    /// <summary>Primary key (backend-allocated).</summary>
    public Guid EmailListRecipientId { get; set; }

    /// <summary>The owning list (FK → email_lists RESTRICT).</summary>
    public Guid EmailListId { get; set; }

    /// <summary>The recipient address.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}