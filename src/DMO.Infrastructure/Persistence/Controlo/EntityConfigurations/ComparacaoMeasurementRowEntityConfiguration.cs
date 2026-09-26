using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>comparacao_measurement_rows</c> table: one NEW comparison
/// measurement row of one compared CM subject, identified by the natural
/// <c>(comparacao_id, cm_id, row_position)</c> primary key.
/// </summary>
/// <remarks>
/// The composite primary key IS the unique invariant (one reading per position per subject) —
/// no reading UUID exists. The composite RESTRICT foreign key binds the rows to the subject's
/// natural key (<c>comparacao_cm_subjects</c>); the CHECKs are the C2-style backstop of the
/// re-measurement results (a violation maps to the same <c>ResultNonPositive</c> refusal the
/// shared calculation path raises). Rows are physically separated from
/// <c>peso_measurement_rows</c>: comparison measurements can never be confused with the initial
/// Peso measurements.</remarks>
public sealed class ComparacaoMeasurementRowEntityConfiguration : IEntityTypeConfiguration<ComparacaoMeasurementRowEntity>
{
    /// <summary>Database name of the <c>row_position</c> CHECK.</summary>
    public const string PositionCheckConstraintName = "comparacao_measurement_rows_position_check";

    /// <summary>Database name of the <c>water_weight_g</c> CHECK.</summary>
    public const string WeightCheckConstraintName = "comparacao_measurement_rows_weight_check";

    /// <summary>Database name of the <c>capacity_cm3</c> CHECK backstop (C2).</summary>
    public const string CapacityCheckConstraintName = "comparacao_measurement_rows_capacity_check";

    /// <summary>Database name of the <c>glass_weight_g</c> CHECK backstop (C2).</summary>
    public const string GlassCheckConstraintName = "comparacao_measurement_rows_glass_check";

    /// <summary>Database name of the subject foreign key (composite, to the natural key).</summary>
    public const string SubjectForeignKeyConstraintName = "FK_comparacao_measurement_rows_comparacao_cm_subjects_comparacao_id_cm_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ComparacaoMeasurementRowEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("comparacao_measurement_rows", table =>
        {
            table.HasCheckConstraint(PositionCheckConstraintName, "row_position >= 1");
            table.HasCheckConstraint(WeightCheckConstraintName, "water_weight_g > 0");
            table.HasCheckConstraint(CapacityCheckConstraintName, "capacity_cm3 > 0");
            table.HasCheckConstraint(GlassCheckConstraintName, "glass_weight_g > 0");
        });

        // The natural key (comparacao_id, cm_id, row_position) IS the primary key — one reading
        // per position per compared CM; no reading UUID exists.
        builder.HasKey(row => new { row.ComparacaoId, row.CmId, row.RowPosition });

        builder.Property(row => row.ComparacaoId)
            .HasColumnName("comparacao_id")
            .IsRequired();

        builder.Property(row => row.CmId)
            .HasColumnName("cm_id")
            .IsRequired();

        builder.Property(row => row.RowPosition)
            .HasColumnName("row_position")
            .IsRequired();

        builder.Property(row => row.WaterWeightG)
            .HasColumnName("water_weight_g")
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(row => row.CapacityCm3)
            .HasColumnName("capacity_cm3")
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(row => row.GlassWeightG)
            .HasColumnName("glass_weight_g")
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(row => row.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<ComparacaoCmSubjectEntity>()
            .WithMany()
            .HasForeignKey(row => new { row.ComparacaoId, row.CmId })
            .HasConstraintName(SubjectForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);
    }
}