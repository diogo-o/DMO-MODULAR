using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>pesos</c> table: one Peso control/result fact.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.1/§17. Maps the exclusive-anchor CHECK
/// <c>(cm_id IS NULL) + (tool_id IS NULL) = 1</c>, the three-value status CHECK, the temperature
/// range, the volume/density/SAP-reference CHECKs, the two RESTRICT FKs to the P2-T04 contexts and
/// canonical Tools, the two actor FKs to <c>users</c> and the <c>version</c> concurrency token.
/// </remarks>
public sealed class PesoEntityConfiguration : IEntityTypeConfiguration<PesoEntity>
{
    /// <summary>Database name of the exclusive-anchor CHECK.</summary>
    public const string AnchorCheckConstraintName = "pesos_anchor_check";

    /// <summary>Database name of the three-value status CHECK.</summary>
    public const string StatusCheckConstraintName = "pesos_status_check";

    /// <summary>Database name of the temperature-range CHECK.</summary>
    public const string TemperatureCheckConstraintName = "pesos_temperature_check";

    /// <summary>Database name of the CM-context foreign key.</summary>
    public const string CmForeignKeyConstraintName = "FK_pesos_cm_contexts_cm_id";

    /// <summary>Database name of the canonical Tool foreign key.</summary>
    public const string ToolForeignKeyConstraintName = "FK_pesos_tools_tool_id";

    /// <summary>Database name of the submitter foreign key.</summary>
    public const string SubmittedByForeignKeyConstraintName = "FK_pesos_users_submitted_by_user_id";

    /// <summary>Database name of the creator foreign key.</summary>
    public const string CreatedByForeignKeyConstraintName = "FK_pesos_users_created_by_user_id";

    /// <summary>Database name of the FK-supporting CM index.</summary>
    public const string CmIndexName = "IX_pesos_cm_id";

    /// <summary>Database name of the FK-supporting Tool index.</summary>
    public const string ToolIndexName = "IX_pesos_tool_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PesoEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("pesos", table =>
        {
            table.HasCheckConstraint(AnchorCheckConstraintName, "((cm_id IS NULL)::int + (tool_id IS NULL)::int) = 1");
            table.HasCheckConstraint(StatusCheckConstraintName, "status IN ('pendente','aprovado','nao_aprovado')");
            table.HasCheckConstraint(TemperatureCheckConstraintName, "water_temperature >= 5 AND water_temperature <= 35");
            table.HasCheckConstraint("pesos_marisa_volume_check", "volume_marisa_bq IS NULL OR volume_marisa_bq >= 0");
            table.HasCheckConstraint("pesos_puncao_volume_check", "volume_puncao_pu IS NULL OR volume_puncao_pu >= 0");
            table.HasCheckConstraint("pesos_density_check", "glass_density_g_cm3 IS NULL OR glass_density_g_cm3 > 0");
            table.HasCheckConstraint(
                "pesos_sap_reference_check",
                "previous_production_end_reference IS NULL OR btrim(previous_production_end_reference) <> ''");
            table.HasCheckConstraint(
                "pesos_sap_weight_check",
                "previous_average_weight_reference IS NULL OR btrim(previous_average_weight_reference) <> ''");
        });

        builder.HasKey(peso => peso.PesoId);
        builder.Property(peso => peso.PesoId)
            .HasColumnName("peso_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(peso => peso.CmId)
            .HasColumnName("cm_id");

        builder.Property(peso => peso.ToolId)
            .HasColumnName("tool_id");

        builder.Property(peso => peso.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasDefaultValue("pendente");

        builder.Property(peso => peso.SubmittedAt)
            .HasColumnName("submitted_at");

        builder.Property(peso => peso.SubmittedByUserId)
            .HasColumnName("submitted_by_user_id");

        builder.Property(peso => peso.WaterTemperature)
            .HasColumnName("water_temperature")
            .IsRequired()
            .HasPrecision(4, 1);

        builder.Property(peso => peso.VolumeMarisaBq)
            .HasColumnName("volume_marisa_bq")
            .HasPrecision(18, 4);

        builder.Property(peso => peso.VolumePuncaoPu)
            .HasColumnName("volume_puncao_pu")
            .HasPrecision(18, 4);

        builder.Property(peso => peso.GlassDensityGCm3)
            .HasColumnName("glass_density_g_cm3")
            .HasPrecision(18, 4);

        builder.Property(peso => peso.PreviousProductionEndReference)
            .HasColumnName("previous_production_end_reference");

        builder.Property(peso => peso.PreviousAverageWeightReference)
            .HasColumnName("previous_average_weight_reference");

        builder.Property(peso => peso.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(peso => peso.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(peso => peso.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(peso => peso.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<CmContextEntity>()
            .WithMany()
            .HasForeignKey(peso => peso.CmId)
            .HasConstraintName(CmForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ToolEntity>()
            .WithMany()
            .HasForeignKey(peso => peso.ToolId)
            .HasConstraintName(ToolForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(peso => peso.SubmittedByUserId)
            .HasConstraintName(SubmittedByForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(peso => peso.CreatedByUserId)
            .HasConstraintName(CreatedByForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(peso => peso.CmId)
            .HasDatabaseName(CmIndexName);

        builder.HasIndex(peso => peso.ToolId)
            .HasDatabaseName(ToolIndexName);
    }
}