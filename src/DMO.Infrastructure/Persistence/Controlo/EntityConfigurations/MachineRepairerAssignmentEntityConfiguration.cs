using DMO.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DMO.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// EF configuration for the <c>machine_repairer_assignments</c> table.
/// </summary>
/// <remarks>
/// Authority: P2-T05 contract §16.4/§17. Closed machine-code CHECK
/// (<c>B1 B2 B3 C1 C2 C3</c>), UNIQUE machine (one independent row per machine), RESTRICT FK to
/// <c>repairers</c> and the FK-supporting repairer index. No line/group concept, no machine
/// registry, no <c>machine_id</c>, no history table (Q-HIST).
/// </remarks>
public sealed class MachineRepairerAssignmentEntityConfiguration : IEntityTypeConfiguration<MachineRepairerAssignmentEntity>
{
    /// <summary>Database name of the one-row-per-machine unique key.</summary>
    public const string MachineUniqueConstraintName = "machine_repairer_assignments_machine_key";

    /// <summary>Database name of the repairer foreign key.</summary>
    public const string RepairerForeignKeyConstraintName = "FK_machine_repairer_assignments_repairers_repairer_id";

    /// <summary>Database name of the FK-supporting repairer index.</summary>
    public const string RepairerIndexName = "machine_repairer_assignments_repairer_idx";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MachineRepairerAssignmentEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("machine_repairer_assignments", table =>
        {
            table.HasCheckConstraint(
                "machine_repairer_assignments_machine_check",
                "machine IN ('B1','B2','B3','C1','C2','C3')");
        });

        builder.HasKey(assignment => assignment.MachineRepairerAssignmentId);
        builder.Property(assignment => assignment.MachineRepairerAssignmentId)
            .HasColumnName("machine_repairer_assignment_id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(assignment => assignment.Machine)
            .HasColumnName("machine")
            .IsRequired();

        builder.Property(assignment => assignment.RepairerId)
            .HasColumnName("repairer_id")
            .IsRequired();

        builder.Property(assignment => assignment.Version)
            .HasColumnName("version")
            .IsRequired()
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        builder.Property(assignment => assignment.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Property(assignment => assignment.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<RepairerEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.RepairerId)
            .HasConstraintName(RepairerForeignKeyConstraintName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(assignment => assignment.Machine)
            .IsUnique()
            .HasDatabaseName(MachineUniqueConstraintName);

        builder.HasIndex(assignment => assignment.RepairerId)
            .HasDatabaseName(RepairerIndexName);
    }
}