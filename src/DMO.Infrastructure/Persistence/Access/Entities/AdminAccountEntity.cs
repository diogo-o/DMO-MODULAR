namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the single <c>admin_accounts</c> row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AdminId"/> is the fixed, code-owned constant <see cref="DMO.Application.Accounts.AdminAccountId"/>
/// (no DB default, <c>ValueGeneratedNever</c>); the database enforces the single-ADMIN
/// invariant with the <c>admin_accounts_singleton_id_check</c> CHECK (every row must use the
/// constant UUID) plus the primary key (that UUID only once).
/// </para>
/// <para>
/// ADMIN is not a USER: there is no Template association and no company number here.
/// </para>
/// </remarks>
public sealed class AdminAccountEntity
{
    /// <summary>Primary key — the fixed singleton <see cref="DMO.Application.Accounts.AdminAccountId"/>.</summary>
    public Guid AdminId { get; set; }

    /// <summary>Provider subject of the existing Supabase Auth ADMIN identity (internal linkage only).</summary>
    public string AuthIdentityId { get; set; } = string.Empty;

    /// <summary>ADMIN display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ADMIN email (the dedicated ADMIN login identifier / credential carrier).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Whether the ADMIN is active.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}