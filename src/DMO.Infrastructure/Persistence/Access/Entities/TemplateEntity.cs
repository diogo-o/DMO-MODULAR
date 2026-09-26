namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>templates</c> table.
/// </summary>
/// <remarks>
/// No active/inactive lifecycle: Templates use Create/Edit/Delete. <see cref="Version"/> is
/// the optimistic-concurrency token; <see cref="LandingDestinationId"/> is nullable (its
/// validity is later Access/Module logic, P1-T04).
/// </remarks>
public sealed class TemplateEntity
{
    /// <summary>Primary key.</summary>
    public Guid TemplateId { get; set; }

    /// <summary>Template display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Nullable landing destination identifier.</summary>
    public string? LandingDestinationId { get; set; }

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}