using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>glass_density_settings</c> table.
/// </summary>
/// <remarks>
/// Authority: P2-T05 post-closure glass-density correction contract §5.1. PK <c>processo</c>
/// (the closed two-value set, text + CHECK — the same posture as
/// <c>machine_repairer_assignments.machine</c>; no FK and no second process catalog),
/// <c>numeric(18,4)</c> density with the strict-positivity CHECK (the same type/posture as the
/// frozen <c>pesos.glass_density_g_cm3</c>), the sibling-settings version convention
/// (<c>DEFAULT 1</c>, concurrency token) and <c>now()</c> timestamps. No extra index: the PK
/// covers the only query shapes (read both rows; read one row).
/// </remarks>
public sealed class GlassDensitySettingEntityConfiguration : IEntityTypeConfiguration<GlassDensitySettingEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GlassDensitySettingEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("glass_density_settings", table =>
        {
            // The closed canonical process set (Owner rule: NNPB and PS are fixed entries).
            table.HasCheckConstraint(
                "glass_density_settings_processo_check",
                "processo IN ('NNPB','PS')");

            // Every operative density must be strictly positive (R7).
            table.HasCheckConstraint(
                "glass_density_settings_density_check",
                "density_g_cm3 > 0");
        });

        builder.HasKey(setting => setting.Processo);

        builder.Property(setting => setting.Processo)
            .HasColumnName("processo")
            .IsRequired();

        builder.Property(setting => setting.DensityGCm3)
            .HasColumnName("density_g_cm3")
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(setting => setting.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(setting => setting.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(setting => setting.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");
    }
}