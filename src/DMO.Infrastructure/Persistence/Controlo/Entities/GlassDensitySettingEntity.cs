namespace DMO.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence entity for the <c>glass_density_settings</c> table: the CURRENT operational glass
/// density (g/cm³) per canonical processo.
/// </summary>
/// <remarks>
/// Authority: P2-T05 post-closure glass-density correction contract §5.1/§5.2. <see cref="Processo"/>
/// is the PK and a closed two-value token (CHECK <c>NNPB</c>/<c>PS</c>); <see cref="DensityGCm3"/>
/// is <c>numeric(18,4)</c> with the positivity CHECK; <see cref="Version"/> is the optimistic
/// concurrency token (seed rows carry version 1, the sibling-settings convention). Exactly two
/// rows exist — seeded with the provenance-backed authoritative values — and no row is ever
/// deleted. No per-<c>tool_id</c> density exists anywhere (Owner rule R1).
/// </remarks>
public sealed class GlassDensitySettingEntity
{
    /// <summary>The canonical processo token (NNPB/PS; PK, closed CHECK).</summary>
    public string Processo { get; set; } = string.Empty;

    /// <summary>The current operational glass density in g/cm³ (<c>numeric(18,4)</c>, &gt; 0).</summary>
    public decimal DensityGCm3 { get; set; }

    /// <summary>Optimistic-concurrency token.</summary>
    public int Version { get; set; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last-update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}