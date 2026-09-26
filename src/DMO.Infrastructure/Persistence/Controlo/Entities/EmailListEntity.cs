namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>email_lists</c> table: one named email recipient list.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.6. <see cref="Name"/> is trimmed non-blank and <b>UNIQUE</b>
/// (<c>email_lists_name_key</c> — a named list must be unambiguously addressable, Q-NAME).
/// </remarks>
public sealed class EmailListEntity
{
    /// <summary>Primary key (backend-allocated).</summary>
    public Guid EmailListId { get; set; }

    /// <summary>The unique named-list name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optimistic-concurrency token (protects the name and the recipient set).</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}