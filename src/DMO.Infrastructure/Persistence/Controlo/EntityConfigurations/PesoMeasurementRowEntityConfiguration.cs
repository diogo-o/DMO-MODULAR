using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>peso_measurement_rows</c> table.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.2/§17. The CHECKs <c>row_position &gt;= 1</c>,
/// <c>water_weight_g &gt; 0</c>, <c>capacity_cm3 &gt; 0</c> and <c>glass_weight_g &gt; 0</c> are
/// the database backstop: the capacity/glass CHECKs are <b>never weakened or removed</b> and a
/// violation (SQLSTATE 23514) maps to the same <c>ResultNonPositive</c> refusal the validator
/// raises (C2, §20.2). The dense-position unique key
/// <c>peso_measurement_rows_peso_position_key</c> is the ordering + rows-of-Peso query index.
/// </remarks>
public sealed class PesoMeasurementRowEntityConfiguration : IEntityTypeConfiguration<PesoMeasurementRowEntity>
{
    /// <summary>Database name of the (peso_id, row_position) unique key.</summary>
    public const string PesoPositionUniqueConstraintName = "peso_measurement_rows_peso_position_key";

    /// <summary>Database name of the <<c>capacity_cm3</c> CHECK backstop (C2).</summary>
    public const string CapacityCheckConstraintName = "peso_measurement_rows_capacity_check";

    /// <summary>Database name of the <c>glass_weight_g</c> CHECK backstop (C2).</summary>
    public const string GlassCheckConstraintName = "peso_measurement_rows_glass_check";

    /// <summary>Database name of the Peso foreign key.</summary>
    public const string PesoForeignKeyConstraintName = "FK_peso_measurement_rows_pesos_peso_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PesoMeasurementRowEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("peso_measurement_rows", table =>
        {
            table.HasCheckConstraint("peso_measurement_rows_position_check", "row_position >= 1");
            table.HasCheckConstraint("peso_measurement_rows_weight_check", "water_weight_g > 0");
            table.HasCheckConstraint(CapacityCheckConstraintName, "capacity_cm3 > 0");
            table.HasCheckConstraint(GlassCheckConstraintName, "glass_weight_g > 0");
        });

        builder.HasKey(row => row.PesoMeasurementRowId);
        builder.Property(row => row.PesoMeasurementRowId)
            .HasColumnName("peso_measurement_row_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(row => row.PesoId)
            .HasColumnName("peso_id")
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

        builder.HasOne<PesoEntity>()
            .WithMany()
            .HasForeignKey(row => row.PesoId)
            .HasConstraintName(PesoForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(row => new { row.PesoId, row.RowPosition })
            .IsUnique()
            .HasDatabaseName(PesoPositionUniqueConstraintName);
    }
}