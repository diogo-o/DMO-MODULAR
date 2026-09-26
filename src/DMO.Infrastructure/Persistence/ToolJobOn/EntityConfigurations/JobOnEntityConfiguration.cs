using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>job_ons</c> table: one production occurrence per row.
/// </summary>
/// <remarks>
/// Authority: P2-T04 contract §3.3 (columns), §4.2 (production uniqueness), §4.3 (self-FK RESTRICT),
/// §4.4 (checks), §4.5 (indexes) and §15 (the version concurrency token).
/// </remarks>
public sealed class JobOnEntityConfiguration : IEntityTypeConfiguration<JobOnEntity>
{
    /// <summary>Database name of the production uniqueness index.</summary>
    public const string ReferenceProductionNumberUniqueConstraintName = "job_ons_reference_production_number_key";

    /// <summary>Database name of the duplication-lineage foreign key.</summary>
    public const string CopiedFromForeignKeyConstraintName = "FK_job_ons_job_ons_copied_from_jobon_id";

    /// <summary>Database name of the lineage foreign-key-supporting index.</summary>
    public const string CopiedFromIndexName = "IX_job_ons_copied_from_jobon_id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobOnEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("job_ons", table =>
        {
            table.HasCheckConstraint("job_ons_reference_required_check", "btrim(reference) <> ''");
            table.HasCheckConstraint(
                "job_ons_production_number_required_check",
                "btrim(production_number) <> ''");
            table.HasCheckConstraint(
                "job_ons_machine_check",
                "machine IN ('B1','B2','B3','C1','C2','C3')");
        });

        builder.HasKey(jobOn => jobOn.JobOnId);
        builder.Property(jobOn => jobOn.JobOnId)
            .HasColumnName("jobon_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(jobOn => jobOn.Reference)
            .HasColumnName("reference")
            .IsRequired();

        builder.Property(jobOn => jobOn.ProductionNumber)
            .HasColumnName("production_number")
            .IsRequired();

        builder.Property(jobOn => jobOn.Machine)
            .HasColumnName("machine")
            .IsRequired();

        builder.Property(jobOn => jobOn.ProductionDate)
            .HasColumnName("production_date");

        builder.Property(jobOn => jobOn.CopiedFromJobOnId)
            .HasColumnName("copied_from_jobon_id");

        // Explicit duplication-source lineage (structural, never a revision). The RESTRICT self-FK
        // makes a recorded source itself protected: persisted lineage is never silently nulled.
        builder.HasOne<JobOnEntity>()
            .WithMany()
            .HasForeignKey(jobOn => jobOn.CopiedFromJobOnId)
            .HasConstraintName(CopiedFromForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(jobOn => jobOn.CopiedFromJobOnId)
            .HasDatabaseName(CopiedFromIndexName);

        // The version token protects the occurrence and every context write in the same transaction.
        builder.Property(jobOn => jobOn.Version)
            .HasColumnName("version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(jobOn => jobOn.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(jobOn => jobOn.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(jobOn => new { jobOn.Reference, jobOn.ProductionNumber })
            .IsUnique()
            .HasDatabaseName(ReferenceProductionNumberUniqueConstraintName);
    }
}
