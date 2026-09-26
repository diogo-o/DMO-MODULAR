namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>users</c> table.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CompanyNumber"/> is the canonical USER-facing login identifier (UNIQUE).
/// <see cref="Email"/> is the provider credential carrier and is mandatory for any usable
/// USER identity (NOT NULL + nonblank CHECK): the Supabase password grant authenticates with
/// the carrier email. <see cref="AuthIdentityId"/> is the provider subject (UNIQUE, internal
/// linkage only). <see cref="TemplateId"/> remains nullable (no effective Template → fails
/// closed later).
/// </para>
/// </remarks>
public sealed class UserEntity
{
    /// <summary>Primary key.</summary>
    public Guid UserId { get; set; }

    /// <summary>Provider subject (internal linkage only).</summary>
    public string AuthIdentityId { get; set; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Canonical USER login identifier.</summary>
    public string CompanyNumber { get; set; } = string.Empty;

    /// <summary>Provider credential carrier email (mandatory nonblank).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Free-text presentation-only role label (grants nothing).</summary>
    public string? Role { get; set; }

    /// <summary>Whether the account is active.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Nullable Template association (no access semantics in P1-T03).</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>Related Template (FK, ON DELETE RESTRICT).</summary>
    public TemplateEntity? Template { get; set; }

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}