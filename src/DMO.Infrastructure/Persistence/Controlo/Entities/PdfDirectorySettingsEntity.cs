namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>pdf_directory_settings</c> table: the single-row base-directory
/// setting.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.5. <see cref="Singleton"/> is always <c>true</c> (CHECK +
/// UNIQUE): the physical single-row mechanism. <see cref="BaseDirectory"/> is an absolute
/// <b>server-host</b> filesystem path (Q-PDF), trimmed non-blank. An absent row = the explicit
/// <c>not-configured</c> state. The path is never an identity and never printed into a document.
/// </remarks>
public sealed class PdfDirectorySettingsEntity
{
    /// <summary>Primary key (backend-allocated).</summary>
    public Guid PdfDirectorySettingId { get; set; }

    /// <summary>The single-row mechanism (always true; CHECK + UNIQUE).</summary>
    public bool Singleton { get; set; } = true;

    /// <summary>The operator-configured server-host base directory (absolute path).</summary>
    public string BaseDirectory { get; set; } = string.Empty;

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}